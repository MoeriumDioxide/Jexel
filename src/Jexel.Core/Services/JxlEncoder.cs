using System.Diagnostics;
using System.Globalization;
using ImageMagick;
using Jexel.Core.Models;

namespace Jexel.Core.Services;

/// <summary>
/// 把任意受支持的图像编码为 JPEG XL。
/// <para>
/// 实现要点（均经过像素级实测验证）：
/// <list type="bullet">
/// <item>无损模式通过 ImageMagick 的 <c>quality = 100</c> 触发 libjxl 的无损编码，输出与源逐像素一致；</item>
/// <item>有损模式通过 <c>quality = 1..99</c> 控制质量，体积随质量单调变化；</item>
/// <item>编码强度通过 <c>jxl:effort</c> 传递，取值范围 1-9；</item>
/// <item>多帧（GIF / APNG / 动画 WebP）经 Coalesce 还原为完整帧后编码为动画 JXL。</item>
/// </list>
/// </para>
/// </summary>
public sealed class JxlEncoder
{
    /// <summary>libjxl 无损编码对应的 quality 取值。</summary>
    private const uint LosslessQuality = 100;

    /// <summary>
    /// 异步编码单个文件。
    /// 所有写入都先落到目标目录下的临时文件，成功后再原子改名，
    /// 因此中途失败不会留下半成品，也永远不会写入源文件。
    /// </summary>
    /// <param name="sourcePath">源文件路径。</param>
    /// <param name="destinationPath">目标 .jxl 路径。</param>
    /// <param name="options">转换选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<ConversionOutcome> EncodeAsync(
        string sourcePath,
        string destinationPath,
        ConversionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(options);

        var stopwatch = Stopwatch.StartNew();
        var sourceSize = 0L;
        var tempPath = (string?)null;

        try
        {
            var sourceFile = new FileInfo(sourcePath);
            if (!sourceFile.Exists)
            {
                throw new FileNotFoundException("源文件不存在。", sourcePath);
            }

            sourceSize = sourceFile.Length;
            cancellationToken.ThrowIfCancellationRequested();

            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrEmpty(destinationDirectory))
            {
                throw new InvalidOperationException("目标路径无效。");
            }

            Directory.CreateDirectory(destinationDirectory);

            // 临时文件放在目标目录，保证随后的改名是同卷原子操作。
            tempPath = Path.Combine(
                destinationDirectory,
                $".{Guid.NewGuid():N}{ImageFormatCatalog.JxlExtension}");

            await Task.Run(
                () => EncodeCore(sourcePath, tempPath, options, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            var overwrite = options.ConflictPolicy == ConflictPolicy.Overwrite
                            && !PathsEqual(tempPath, destinationPath);

            File.Move(tempPath, destinationPath, overwrite);
            tempPath = null;

            var destinationSize = new FileInfo(destinationPath).Length;
            stopwatch.Stop();

            return new ConversionOutcome(
                sourcePath,
                destinationPath,
                ConversionState.Succeeded,
                sourceSize,
                destinationSize,
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return new ConversionOutcome(
                sourcePath, null, ConversionState.Canceled, sourceSize, 0, stopwatch.Elapsed, "已取消");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ConversionOutcome(
                sourcePath, null, ConversionState.Failed, sourceSize, 0, stopwatch.Elapsed, DescribeError(ex));
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static void EncodeCore(
        string sourcePath,
        string tempPath,
        ConversionOptions options,
        CancellationToken cancellationToken)
    {
        using var collection = new MagickImageCollection(sourcePath);

        if (collection.Count == 0)
        {
            throw new InvalidDataException("源文件中没有可解码的图像帧。");
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (collection.Count == 1)
        {
            Configure(collection[0], options);
            cancellationToken.ThrowIfCancellationRequested();
            collection[0].Write(tempPath);
            return;
        }

        // 动画：把局部差异帧还原为完整帧，避免转换后画面错位。
        collection.Coalesce();

        foreach (var frame in collection)
        {
            Configure(frame, options);
        }

        cancellationToken.ThrowIfCancellationRequested();
        collection.Write(tempPath);
    }

    /// <summary>把压缩选项应用到单帧图像。</summary>
    private static void Configure(IMagickImage<ushort> image, ConversionOptions options)
    {
        image.Format = MagickFormat.Jxl;

        image.Quality = options.Mode == CompressionMode.Lossless
            ? LosslessQuality
            : (uint)Math.Clamp(options.Quality, 1, 99);

        var effort = Math.Clamp(options.Effort, 1, 9);
        image.Settings.SetDefine("jxl:effort", effort.ToString(CultureInfo.InvariantCulture));

        // ICC 配置文件会被原样写入码流：像素值不变，只是体积变大。
        // 移除后解码器按默认 sRGB 解释颜色，因此只有用户明确关闭"保留"时才执行。
        if (!options.PreserveIccProfile && image.GetProfile("icc") is not null)
        {
            image.RemoveProfile("icc");
        }
    }

    private static bool PathsEqual(string a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // 清理失败不影响转换结果，残留的临时文件以 "." 开头且不会被计入结果。
        }
    }

    private static string DescribeError(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "没有权限写入目标位置。",
        DirectoryNotFoundException => "目标文件夹不存在或已被移除。",
        FileNotFoundException => "文件不存在或已被移除。",
        PathTooLongException => "文件路径过长，超出了系统限制。",
        IOException io when (io.HResult & 0xFFFF) == 32 => "文件正被其他程序占用，请关闭后重试。",
        IOException io when (io.HResult & 0xFFFF) == 112 => "磁盘空间不足。",
        IOException => $"读写失败：{exception.Message}",
        MagickResourceLimitErrorException => "图像过大，超出了可用的内存或资源限制。",
        MagickDelegateErrorException or MagickMissingDelegateErrorException => "系统缺少解码该图像格式所需的组件。",
        MagickCorruptImageErrorException => "图像文件已损坏或不完整。",
        MagickException magick => $"图像处理失败：{magick.Message}",
        _ => exception.Message,
    };
}
