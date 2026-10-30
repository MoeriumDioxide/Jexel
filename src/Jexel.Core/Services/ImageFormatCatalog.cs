using System.Collections.Frozen;
using ImageMagick;
using Jexel.Core.Models;

namespace Jexel.Core.Services;

/// <summary>
/// 图像格式能力目录：负责判定文件是否为可读取的图像，并提供文件选择对话框所需的过滤器。
/// 能力来自 Magick.NET（ImageMagick）实际编译进运行时的解码器列表，而非硬编码猜测。
/// </summary>
public static class ImageFormatCatalog
{
    /// <summary>JPEG XL 的输出扩展名。</summary>
    public const string JxlExtension = ".jxl";

    /// <summary>编码临时文件名中 GUID 的十六进制字符数。</summary>
    private const int TempArtifactGuidLength = 32;

    /// <summary>
    /// 面向用户的常见图像扩展名白名单（小写、含前导点）。
    /// 用于文件夹扫描的快速预筛选，最终仍以内容头探测为准。
    /// </summary>
    public static readonly FrozenSet<string> CommonExtensions = new[]
    {
        ".png", ".apng", ".jpg", ".jpeg", ".jpe", ".jfif", ".jif", ".jfi",
        ".gif", ".bmp", ".dib", ".tif", ".tiff", ".tga", ".icb", ".vda", ".vst",
        ".webp", ".avif", ".heic", ".heif", ".jxl", ".jp2", ".j2k", ".jpc", ".jpf", ".jpx",
        ".psd", ".psb", ".ico", ".cur", ".dds", ".exr", ".hdr", ".rgbe",
        ".qoi", ".svg", ".svgz", ".mvg", ".miff", ".mng", ".jng",
        ".ppm", ".pgm", ".pbm", ".pnm", ".pfm", ".pam", ".xpm", ".xbm",
        ".pcx", ".dpx", ".fits", ".viff", ".sgi", ".rgb", ".rgba", ".sun", ".ras",
        ".pict", ".pct", ".pic", ".pdb", ".palm", ".otb", ".wbmp", ".avs", ".cin",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>用于文件选择对话框的 Win32 过滤器字符串。</summary>
    public static string FileDialogFilter { get; } = BuildFilter();

    private static string BuildFilter()
    {
        var patterns = CommonExtensions.Select(static e => "*" + e).ToArray();
        var all = string.Join(";", patterns);
        return $"所有支持的图像 ({all})|{all}|" +
               $"JPEG XL (*{JxlExtension})|*{JxlExtension}|" +
               "JPEG (*.jpg;*.jpeg;*.jpe)|*.jpg;*.jpeg;*.jpe|" +
               "PNG (*.png;*.apng)|*.png;*.apng|" +
               "WebP 与 AVIF (*.webp;*.avif)|*.webp;*.avif|" +
               "HEIF 系列 (*.heic;*.heif)|*.heic;*.heif|" +
               "TIFF 与 BMP (*.tif;*.tiff;*.bmp)|*.tif;*.tiff;*.bmp|" +
               "GIF (*.gif)|*.gif|" +
               "所有文件 (*.*)|*.*";
    }

    /// <summary>扩展名是否在常见白名单内。</summary>
    public static bool HasCommonExtension(string path)
        => CommonExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// 判断文件名是否为本工具编码时留下的临时产物（<c>.&lt;32 位十六进制&gt;.jxl</c>）。
    /// 这类文件内容是合法的 JPEG XL，仅凭文件头无法与正常输出区分，只能靠命名约定识别。
    /// 正常流程会删除它们，但进程被强制结束时可能残留，扫描目录时必须排除，
    /// 否则会被当成待转换图像再次导入。
    /// </summary>
    /// <param name="path">文件路径或文件名。</param>
    public static bool IsTemporaryArtifact(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Length != TempArtifactGuidLength + 1 + JxlExtension.Length
            || name[0] != '.'
            || !name.EndsWith(JxlExtension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var guidPart = name.AsSpan(1, TempArtifactGuidLength);
        foreach (var character in guidPart)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 通过读取文件头判断该文件能否被解码，并返回轻量元信息。
    /// 该操作只读取文件头，不会把整幅图像载入内存。
    /// </summary>
    public static bool TryReadInfo(string path, out ImageInfo info)
    {
        info = default;
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var fileSize = new FileInfo(path).Length;
            if (fileSize == 0)
            {
                return false;
            }

            var imageInfo = new MagickImageInfo(path);
            info = new ImageInfo(
                NormalizeFormatName(imageInfo.Format),
                (int)imageInfo.Width,
                (int)imageInfo.Height,
                fileSize);
            return true;
        }
        catch (Exception)
        {
            // 损坏文件、权限不足、非图像内容统一视为不可读。
            return false;
        }
    }

    /// <summary>把 Magick.NET 的格式枚举名规范为适合展示的大写名称。</summary>
    public static string NormalizeFormatName(MagickFormat format)
    {
        var name = format.ToString();
        return name.ToUpperInvariant() switch
        {
            "JPEG" or "JPG" or "JPE" => "JPEG",
            "PNG" or "PNG24" or "PNG32" or "PNG48" or "PNG64" or "PNG8" or "PNG00" => "PNG",
            "TIFF" or "TIF" or "TIFF64" => "TIFF",
            "WEBP" => "WEBP",
            "AVIF" => "AVIF",
            "HEIC" or "HEIF" => "HEIF",
            "JXL" => "JPEG XL",
            "BMP" or "BMP2" or "BMP3" => "BMP",
            "GIF" or "GIF87" => "GIF",
            "JP2" or "J2K" or "JPC" => "JPEG 2000",
            _ => name.ToUpperInvariant(),
        };
    }

    /// <summary>
    /// 从一个目录枚举所有可转换的候选文件。
    /// 先按扩展名白名单预筛选以减少 IO，再逐个读取文件头确认。
    /// </summary>
    /// <param name="folder">要扫描的文件夹。</param>
    /// <param name="searchOption">是否递归子目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static IReadOnlyList<string> EnumerateConvertibleFiles(
        string folder,
        SearchOption searchOption = SearchOption.TopDirectoryOnly,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folder))
        {
            return Array.Empty<string>();
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*", new EnumerationOptions
            {
                RecurseSubdirectories = searchOption == SearchOption.AllDirectories,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System | FileAttributes.Temporary,
            });
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!HasCommonExtension(file))
            {
                continue;
            }

            // 上次异常退出残留的 ".<guid>.jxl" 是合法图像，只能靠命名约定排除。
            if (IsTemporaryArtifact(file))
            {
                continue;
            }

            if (TryReadInfo(file, out _))
            {
                result.Add(file);
            }
        }

        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }
}
