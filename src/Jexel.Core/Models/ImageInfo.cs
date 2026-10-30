namespace Jexel.Core.Models;

/// <summary>
/// 源图像的轻量元信息（仅读取文件头，不解码整个图像）。
/// </summary>
/// <param name="Format">规范化后的格式名，例如 PNG、JPEG。</param>
/// <param name="Width">像素宽度。</param>
/// <param name="Height">像素高度。</param>
/// <param name="FileSize">文件字节数。</param>
public readonly record struct ImageInfo(string Format, int Width, int Height, long FileSize)
{
    /// <summary>人类可读的尺寸描述，例如 1920 × 1080。</summary>
    public string Dimensions => Width > 0 && Height > 0 ? $"{Width} × {Height}" : "未知尺寸";
}
