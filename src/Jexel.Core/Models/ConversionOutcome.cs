namespace Jexel.Core.Models;

/// <summary>
/// 单个文件的转换结果。
/// </summary>
/// <param name="SourcePath">源文件路径。</param>
/// <param name="DestinationPath">实际写入的目标文件路径（失败时为 null）。</param>
/// <param name="State">结果状态。</param>
/// <param name="SourceSize">源文件字节数。</param>
/// <param name="DestinationSize">目标文件字节数（失败时为 0）。</param>
/// <param name="Elapsed">耗时。</param>
/// <param name="ErrorMessage">失败原因。</param>
public sealed record ConversionOutcome(
    string SourcePath,
    string? DestinationPath,
    ConversionState State,
    long SourceSize,
    long DestinationSize,
    TimeSpan Elapsed,
    string? ErrorMessage = null)
{
    /// <summary>是否成功。</summary>
    public bool Succeeded => State == ConversionState.Succeeded;

    /// <summary>体积变化比例（正数表示变小）。目标为 0 字节时返回 0。</summary>
    public double SizeReductionRatio
        => SourceSize <= 0 || DestinationSize <= 0
            ? 0
            : 1.0 - (double)DestinationSize / SourceSize;
}

/// <summary>
/// 单次批量转换的汇总结果。
/// </summary>
/// <param name="Outcomes">各文件结果。</param>
/// <param name="TotalElapsed">总耗时。</param>
/// <param name="WasCanceled">是否被用户取消。</param>
public sealed record ConversionSummary(
    IReadOnlyList<ConversionOutcome> Outcomes,
    TimeSpan TotalElapsed,
    bool WasCanceled)
{
    /// <summary>成功数量。</summary>
    public int SucceededCount => Outcomes.Count(o => o.Succeeded);

    /// <summary>失败数量。</summary>
    public int FailedCount => Outcomes.Count(o => o.State == ConversionState.Failed);

    /// <summary>跳过数量。</summary>
    public int SkippedCount => Outcomes.Count(o => o.State is ConversionState.Skipped or ConversionState.Canceled);

    /// <summary>成功项源文件总字节数。</summary>
    public long TotalSourceBytes => Outcomes.Where(o => o.Succeeded).Sum(o => o.SourceSize);

    /// <summary>成功项目标文件总字节数。</summary>
    public long TotalDestinationBytes => Outcomes.Where(o => o.Succeeded).Sum(o => o.DestinationSize);

    /// <summary>整体体积变化比例（正数表示变小）。</summary>
    public double TotalReductionRatio
        => TotalSourceBytes <= 0
            ? 0
            : 1.0 - (double)TotalDestinationBytes / TotalSourceBytes;
}
