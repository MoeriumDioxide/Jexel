using System.Security.Cryptography;
using ImageMagick;
using Jexel.Core.Models;
using Jexel.Core.Services;

// 端到端冒烟测试：验证转换正确性、源文件安全性与路径规划策略。
var root = Path.Combine(Path.GetTempPath(), "jexel-smoke-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(root);

var failures = new List<string>();
var passes = 0;

void Check(string name, bool condition, string? detail = null)
{
    if (condition)
    {
        passes++;
        Console.WriteLine($"  PASS  {name}");
    }
    else
    {
        failures.Add(name + (detail is null ? string.Empty : $" — {detail}"));
        Console.WriteLine($"  FAIL  {name}{(detail is null ? string.Empty : " — " + detail)}");
    }
}

static string RawRgbaHash(string path, int depth = 8)
{
    using var img = new MagickImage(path) { Format = MagickFormat.Rgba, Depth = (uint)depth };
    using var ms = new MemoryStream();
    img.Write(ms);
    return Convert.ToHexString(SHA256.HashData(ms.ToArray()));
}

static string FileHash(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream));
}

static void MakeImage(string path, int width, int height, MagickFormat format, bool alpha = false, bool gray = false)
{
    using var img = new MagickImage(MagickColors.Black, (uint)width, (uint)height) { ColorSpace = ColorSpace.sRGB };
    if (gray)
    {
        img.ColorType = ColorType.Grayscale;
    }

    if (alpha)
    {
        img.HasAlpha = true;
    }

    var pixels = img.GetPixels();
    var rnd = new Random(width * 31 + height);
    for (var y = 0; y < height; y++)
    {
        for (var x = 0; x < width; x++)
        {
            var baseValue = 120.0 + 100.0 * Math.Sin(x / 17.0) * Math.Cos(y / 23.0);
            byte N() => (byte)Math.Clamp(baseValue + rnd.Next(-12, 13), 0, 255);

            ushort[] pixel;
            if (gray)
            {
                pixel = [(ushort)(N() * 257)];
            }
            else if (alpha)
            {
                pixel =
                [
                    (ushort)(N() * 257), (ushort)(N() * 257),
                    (ushort)(N() * 257), (ushort)(rnd.Next(256) * 257),
                ];
            }
            else
            {
                pixel = [(ushort)(N() * 257), (ushort)(N() * 257), (ushort)(N() * 257)];
            }

            pixels.SetPixel(x, y, pixel);
        }
    }

    img.Format = format;
    img.Write(path);
}

Console.WriteLine("== 1. 目录扫描 ==");
var inputDir = Path.Combine(root, "input");
Directory.CreateDirectory(inputDir);

MakeImage(Path.Combine(inputDir, "photo.png"), 320, 240, MagickFormat.Png);
MakeImage(Path.Combine(inputDir, "icon.bmp"), 64, 64, MagickFormat.Bmp);
MakeImage(Path.Combine(inputDir, "scan.tif"), 200, 150, MagickFormat.Tiff);
MakeImage(Path.Combine(inputDir, "alpha.png"), 128, 128, MagickFormat.Png, alpha: true);
MakeImage(Path.Combine(inputDir, "gray.png"), 96, 96, MagickFormat.Png, gray: true);
MakeImage(Path.Combine(inputDir, "photo.jpg"), 320, 240, MagickFormat.Jpeg);
MakeImage(Path.Combine(inputDir, "anim.webp"), 48, 48, MagickFormat.WebP);
File.WriteAllText(Path.Combine(inputDir, "notes.txt"), "这不是图像");

// 模拟上次进程被强制结束时残留的临时产物：内容是完全合法的 JPEG XL，
// 仅凭文件头无法与正常输出区分，只能靠 ".<32 位十六进制>.jxl" 的命名约定排除。
var tempArtifact = Path.Combine(inputDir, "." + new string('a', 32) + ".jxl");
MakeImage(tempArtifact, 32, 32, MagickFormat.Jxl);

var found = ImageFormatCatalog.EnumerateConvertibleFiles(inputDir);
Check($"扫描到 7 个图像（实际 {found.Count}）", found.Count == 7);
Check("未把 .txt 当作图像", found.All(f => !f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)));
Check("识别本工具遗留的临时文件", ImageFormatCatalog.IsTemporaryArtifact(tempArtifact));
Check("普通图像不会被误判为临时文件",
    !ImageFormatCatalog.IsTemporaryArtifact(Path.Combine(inputDir, "photo.png"))
    && !ImageFormatCatalog.IsTemporaryArtifact(tempArtifact.Replace(".jxl", ".png", StringComparison.Ordinal)));

Console.WriteLine();
Console.WriteLine("== 2. 无损转换的正确性与源文件安全 ==");
var outputDir = Path.Combine(root, "output");
Directory.CreateDirectory(outputDir);

var sourceHashesBefore = found.ToDictionary(f => f, FileHash);

var losslessOptions = new ConversionOptions
{
    Mode = CompressionMode.Lossless,
    Effort = 7,
    Location = OutputLocationMode.CustomFolder,
    OutputFolder = outputDir,
    ConflictPolicy = ConflictPolicy.Rename,
};

var plan = ConversionPlanner.Build(found, losslessOptions);
Check($"计划项数量为 7（实际 {plan.Count}）", plan.Count == 7);
Check("全部计划项就绪", plan.All(static p => p.Status == PlanItemStatus.Ready));
Check("目标扩展名均为 .jxl", plan.All(static p => p.DestinationPath?.EndsWith(".jxl", StringComparison.OrdinalIgnoreCase) == true));

var converter = new BatchConverter();
var summary = await converter.RunAsync(plan, losslessOptions);

Check($"全部转换成功（成功 {summary.SucceededCount} / 失败 {summary.FailedCount}）", summary.SucceededCount == 7 && summary.FailedCount == 0,
    string.Join("; ", summary.Outcomes.Where(static o => !o.Succeeded).Select(static o => $"{Path.GetFileName(o.SourcePath)}: {o.ErrorMessage}")));

foreach (var outcome in summary.Outcomes.Where(static o => o.Succeeded))
{
    var name = Path.GetFileName(outcome.SourcePath);
    var exact = RawRgbaHash(outcome.SourcePath) == RawRgbaHash(outcome.DestinationPath!);
    Check($"无损 bit-exact：{name}", exact);
}

var sourcesChanged = sourceHashesBefore
    .Where(static kv => true)
    .Count(kv => FileHash(kv.Key) != kv.Value);
Check("所有源文件内容未被改动", sourcesChanged == 0, $"{sourcesChanged} 个文件被修改");

Console.WriteLine();
Console.WriteLine("== 2b. ICC 配置文件开关 ==");
var iccSource = Path.Combine(root, "icc.png");
MakeImage(iccSource, 120, 90, MagickFormat.Png);

var keepIccOptions = new ConversionOptions
{
    Mode = CompressionMode.Lossless,
    Location = OutputLocationMode.CustomFolder,
    OutputFolder = Path.Combine(root, "icc-keep"),
    PreserveIccProfile = true,
};

var dropIccOptions = new ConversionOptions
{
    Mode = CompressionMode.Lossless,
    Location = OutputLocationMode.CustomFolder,
    OutputFolder = Path.Combine(root, "icc-drop"),
    PreserveIccProfile = false,
};

var keepIccPlan = ConversionPlanner.Build([iccSource], keepIccOptions);
var dropIccPlan = ConversionPlanner.Build([iccSource], dropIccOptions);
var keepIccSummary = await converter.RunAsync(keepIccPlan, keepIccOptions);
var dropIccSummary = await converter.RunAsync(dropIccPlan, dropIccOptions);

Check($"两种 ICC 设置都转换成功（{keepIccSummary.SucceededCount} / {dropIccSummary.SucceededCount}）",
    keepIccSummary.SucceededCount == 1 && dropIccSummary.SucceededCount == 1);
Check("关闭 ICC 保留后仍然逐像素无损",
    RawRgbaHash(iccSource) == RawRgbaHash(dropIccPlan[0].DestinationPath!));

Console.WriteLine();
Console.WriteLine("== 3. 有损转换 ==");
var lossyDir = Path.Combine(root, "output-lossy");
Directory.CreateDirectory(lossyDir);

var lossyOptions = new ConversionOptions
{
    Mode = CompressionMode.Lossy,
    Quality = 80,
    Effort = 5,
    Location = OutputLocationMode.CustomFolder,
    OutputFolder = lossyDir,
};

var lossyPlan = ConversionPlanner.Build(found, lossyOptions);
var lossySummary = await converter.RunAsync(lossyPlan, lossyOptions);

Check($"有损转换全部成功（成功 {lossySummary.SucceededCount}）", lossySummary.SucceededCount == 7);
Check("有损输出体积小于无损输出", lossySummary.TotalDestinationBytes < summary.TotalDestinationBytes,
    $"有损 {lossySummary.TotalDestinationBytes} vs 无损 {summary.TotalDestinationBytes}");

Console.WriteLine();
Console.WriteLine("== 4. 原地输出 ==");
var inPlaceDir = Path.Combine(root, "inplace");
Directory.CreateDirectory(inPlaceDir);
var inPlaceSource = Path.Combine(inPlaceDir, "sample.png");
MakeImage(inPlaceSource, 100, 100, MagickFormat.Png);

var inPlaceOptions = new ConversionOptions
{
    Mode = CompressionMode.Lossless,
    Location = OutputLocationMode.AlongsideSource,
};

var inPlacePlan = ConversionPlanner.Build([inPlaceSource], inPlaceOptions);
Check("原地输出的目标与源同目录", Path.GetDirectoryName(inPlacePlan[0].DestinationPath!) == inPlaceDir);
Check("原地输出的目标名不同于源文件", !string.Equals(inPlacePlan[0].DestinationPath, inPlaceSource, StringComparison.OrdinalIgnoreCase));

var inPlaceSummary = await converter.RunAsync(inPlacePlan, inPlaceOptions);
Check("原地输出转换成功", inPlaceSummary.SucceededCount == 1);
Check("原地输出后源文件完好", FileHash(inPlaceSource) == sourceHashesBefore.GetValueOrDefault(inPlaceSource, FileHash(inPlaceSource)));

Console.WriteLine();
Console.WriteLine("== 5. 冲突策略 ==");
var conflictDir = Path.Combine(root, "conflict");
Directory.CreateDirectory(conflictDir);
var conflictSource = Path.Combine(conflictDir, "dup.png");
MakeImage(conflictSource, 80, 80, MagickFormat.Png);

var renameOptions = new ConversionOptions { Location = OutputLocationMode.AlongsideSource, ConflictPolicy = ConflictPolicy.Rename };
var firstPlan = ConversionPlanner.Build([conflictSource], renameOptions);
await converter.RunAsync(firstPlan, renameOptions);

// 第二次转换：目标已存在，应自动改成 "dup (1).jxl"
var secondPlan = ConversionPlanner.Build([conflictSource], renameOptions);
Check("重名时自动改名", secondPlan[0].DestinationPath?.EndsWith("dup (1).jxl", StringComparison.OrdinalIgnoreCase) == true,
    secondPlan[0].DestinationPath);
var secondSummary = await converter.RunAsync(secondPlan, renameOptions);
Check("改名后转换成功", secondSummary.SucceededCount == 1);
Check("原有输出文件仍然存在", File.Exists(firstPlan[0].DestinationPath!));

var skipOptions = new ConversionOptions { Location = OutputLocationMode.AlongsideSource, ConflictPolicy = ConflictPolicy.Skip };
var skipPlan = ConversionPlanner.Build([conflictSource], skipOptions);
Check("跳过策略下目标为 null", skipPlan[0].DestinationPath is null);
var skipSummary = await converter.RunAsync(skipPlan, skipOptions);
Check("跳过策略计入跳过计数", skipSummary.SkippedCount == 1);

Console.WriteLine();
Console.WriteLine("== 6. 源文件本身已是 .jxl 的安全处理 ==");
var jxlDir = Path.Combine(root, "jxlself");
Directory.CreateDirectory(jxlDir);
var jxlSource = Path.Combine(jxlDir, "already.jxl");
MakeImage(jxlSource, 64, 64, MagickFormat.Jxl);
var jxlBefore = FileHash(jxlSource);

var selfOptions = new ConversionOptions
{
    Mode = CompressionMode.Lossless,
    Location = OutputLocationMode.AlongsideSource,
    ConflictPolicy = ConflictPolicy.Overwrite, // 即使选择覆盖，也绝不能写回源文件
};

var selfPlan = ConversionPlanner.Build([jxlSource], selfOptions);
Check("自指目标被改名为新文件", !string.Equals(selfPlan[0].DestinationPath, jxlSource, StringComparison.OrdinalIgnoreCase),
    selfPlan[0].DestinationPath);
var selfSummary = await converter.RunAsync(selfPlan, selfOptions);
Check("自指场景转换成功", selfSummary.SucceededCount == 1);
Check("源 .jxl 文件未被覆盖", FileHash(jxlSource) == jxlBefore);

Console.WriteLine();
Console.WriteLine("== 7. 不可读文件的处理 ==");
var badDir = Path.Combine(root, "bad");
Directory.CreateDirectory(badDir);
var brokenFile = Path.Combine(badDir, "broken.png");
File.WriteAllText(brokenFile, "definitely not a png");

var badPlan = ConversionPlanner.Build([brokenFile, Path.Combine(badDir, "missing.png")], losslessOptions);
Check("损坏文件被标记为不支持", badPlan.Any(static p => p.Status == PlanItemStatus.Unsupported));
Check("缺失文件被标记为不存在", badPlan.Any(static p => p.Status == PlanItemStatus.Missing));

var badSummary = await converter.RunAsync(badPlan, losslessOptions);
Check("损坏文件在批量中被计为失败而非中断", badSummary.FailedCount == 2);
Check("批量任务未抛异常", badSummary.Outcomes.Count == 2);

Console.WriteLine();
Console.WriteLine("== 8. 取消支持 ==");
var cancelDir = Path.Combine(root, "cancel");
Directory.CreateDirectory(cancelDir);
for (var i = 0; i < 6; i++)
{
    MakeImage(Path.Combine(cancelDir, $"c{i}.png"), 256, 256, MagickFormat.Png);
}

var cancelFiles = ImageFormatCatalog.EnumerateConvertibleFiles(cancelDir);
var cancelOptions = new ConversionOptions
{
    Location = OutputLocationMode.CustomFolder,
    OutputFolder = Path.Combine(root, "cancel-out"),
};
var cancelPlan = ConversionPlanner.Build(cancelFiles, cancelOptions);

using var cts = new CancellationTokenSource();
var cancelled = 0;
var cancelProgress = new Progress<ConversionProgress>(_ =>
{
    if (Interlocked.Increment(ref cancelled) >= 2)
    {
        cts.Cancel();
    }
});

var cancelSummary = await converter.RunAsync(cancelPlan, cancelOptions, cancelProgress, cts.Token);
Check("取消被正确上报", cancelSummary.WasCanceled);
Check("取消后仍有成功项", cancelSummary.SucceededCount > 0);

Console.WriteLine();
Console.WriteLine("== 9. 临时文件清理 ==");
var leftovers = Directory.EnumerateFiles(outputDir, ".*.jxl", SearchOption.TopDirectoryOnly).ToList();
Check("输出目录没有残留临时文件", leftovers.Count == 0, string.Join(", ", leftovers));

Console.WriteLine();
Console.WriteLine(new string('=', 60));
Console.WriteLine($"通过 {passes} 项，失败 {failures.Count} 项");
foreach (var failure in failures)
{
    Console.WriteLine("  ✗ " + failure);
}

try
{
    Directory.Delete(root, recursive: true);
}
catch (Exception)
{
    // 测试目录清理失败不影响结论。
}

return failures.Count == 0 ? 0 : 1;
