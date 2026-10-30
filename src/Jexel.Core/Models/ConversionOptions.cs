namespace Jexel.Core.Models;

/// <summary>
/// 一次转换任务的全部可配置项。
/// </summary>
public sealed class ConversionOptions
{
    /// <summary>默认无损编码 effort。</summary>
    public const int DefaultEffort = 7;

    /// <summary>默认有损质量。</summary>
    public const int DefaultQuality = 90;

    /// <summary>压缩模式，默认无损。</summary>
    public CompressionMode Mode { get; set; } = CompressionMode.Lossless;

    /// <summary>
    /// 有损模式的质量目标，取值 1-99（越大质量越高、体积越大）。
    /// 无损模式忽略此值。
    /// </summary>
    public int Quality { get; set; } = DefaultQuality;

    /// <summary>
    /// 编码强度 1-9。数值越高压缩率越高但耗时越长。
    /// 无损模式下建议 7-9；有损模式下建议 3-7。
    /// </summary>
    public int Effort { get; set; } = DefaultEffort;

    /// <summary>
    /// 输出位置策略。
    /// 默认原地输出：输出文件使用 .jxl 扩展名，与源文件天然不同名，因此无需任何额外配置即可开始转换。
    /// </summary>
    public OutputLocationMode Location { get; set; } = OutputLocationMode.AlongsideSource;

    /// <summary>输出文件夹；当 <see cref="Location"/> 为 <see cref="OutputLocationMode.CustomFolder"/> 时必填。</summary>
    public string? OutputFolder { get; set; }

    /// <summary>目标文件已存在时的处理策略，默认重命名以保护已有数据。</summary>
    public ConflictPolicy ConflictPolicy { get; set; } = ConflictPolicy.Rename;

    /// <summary>
    /// 是否在无损模式下也保留源文件的 ICC 配置文件。
    /// 关闭可减小体积，默认开启以保持色彩还原一致。
    /// </summary>
    public bool PreserveIccProfile { get; set; } = true;

    /// <summary>规范化并校验参数，返回错误信息（null 表示合法）。</summary>
    public string? Validate()
    {
        if (Quality is < 1 or > 99)
        {
            return "有损质量必须介于 1 与 99 之间。";
        }

        if (Effort is < 1 or > 9)
        {
            return "编码强度必须介于 1 与 9 之间。";
        }

        if (Location == OutputLocationMode.CustomFolder && string.IsNullOrWhiteSpace(OutputFolder))
        {
            return "请先选择输出文件夹，或改为原地输出。";
        }

        return null;
    }

    /// <summary>把有损质量映射为 libjxl 的 distance 值（仅用于界面提示）。</summary>
    public double GetApproximateDistance()
        => Math.Round((100.0 - Quality) / 10.0, 2);
}
