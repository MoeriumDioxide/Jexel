namespace Jexel.Core.Models;

/// <summary>
/// JPEG XL 压缩模式。
/// </summary>
public enum CompressionMode
{
    /// <summary>无损：解码结果与源图像逐像素完全一致（bit-exact）。</summary>
    Lossless = 0,

    /// <summary>有损：按质量目标压缩，体积显著更小。</summary>
    Lossy = 1,
}
