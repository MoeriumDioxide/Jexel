using System.Diagnostics;
using Jexel.Core.Models;

namespace Jexel.Core.Services;

/// <summary>
/// 批量转换进度快照。
/// </summary>
/// <param name="CompletedCount">已完成数量（含失败与跳过）。</param>
/// <param name="TotalCount">计划总数。</param>
/// <param name="CurrentFileName">当前处理的文件名。</param>
/// <param name="Outcome">刚完成项的结果，开始时为 null。</param>
/// <param name="CurrentSourcePath">当前正在处理的源文件绝对路径，用于界面定位到具体行。</param>
public sealed record ConversionProgress(
    int CompletedCount,
    int TotalCount,
    string CurrentFileName,
    ConversionOutcome? Outcome,
    string? CurrentSourcePath = null)
{
    /// <summary>完成百分比（0-100）。</summary>
    public double Percent => TotalCount <= 0 ? 0 : Math.Clamp(CompletedCount * 100.0 / TotalCount, 0, 100);
}

/// <summary>
/// 串行批量转换器。
/// <para>
/// 刻意采用串行执行：libjxl 自身已使用多线程，并行处理多个文件只会造成线程争抢，
/// 既不会更快，也会让内存峰值与进度显示都变得不可控。
/// </para>
/// </summary>
public sealed class BatchConverter
{
    private readonly JxlEncoder _encoder;

    /// <summary>创建批量转换器。</summary>
    /// <param name="encoder">单文件编码器；留空则使用默认实现。</param>
    public BatchConverter(JxlEncoder? encoder = null)
    {
        _encoder = encoder ?? new JxlEncoder();
    }

    /// <summary>
    /// 依次转换计划中所有处于可转换状态的项。
    /// 任何单个文件的失败都会记录在结果中而不会中断整批任务。
    /// </summary>
    /// <param name="plan">转换计划。</param>
    /// <param name="options">转换选项。</param>
    /// <param name="progress">进度回调。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<ConversionSummary> RunAsync(
        IReadOnlyList<ConversionPlanItem> plan,
        ConversionOptions options,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(options);

        var stopwatch = Stopwatch.StartNew();
        var outcomes = new List<ConversionOutcome>(plan.Count);
        var runnable = plan.Where(static p => p.Status == PlanItemStatus.Ready).ToList();
        var total = runnable.Count;
        var completed = 0;
        var canceled = false;

        // 不可执行的项目也要给出结果，界面才能完整展示每一行的最终状态。
        foreach (var item in plan.Where(static p => p.Status != PlanItemStatus.Ready))
        {
            var state = item.Status switch
            {
                PlanItemStatus.Skipped => ConversionState.Skipped,
                PlanItemStatus.Missing or PlanItemStatus.Unsupported => ConversionState.Failed,
                _ => ConversionState.Skipped,
            };

            outcomes.Add(new ConversionOutcome(
                item.SourcePath,
                item.DestinationPath,
                state,
                item.SourceInfo?.FileSize ?? 0,
                0,
                TimeSpan.Zero,
                item.Message));
        }

        foreach (var item in runnable)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                canceled = true;
                outcomes.Add(new ConversionOutcome(
                    item.SourcePath, item.DestinationPath, ConversionState.Canceled,
                    item.SourceInfo?.FileSize ?? 0, 0, TimeSpan.Zero, "已取消"));
                completed++;
                progress?.Report(new ConversionProgress(completed, total, item.SourceFileName, null, item.SourcePath));
                continue;
            }

            // 缺少目标路径说明输出位置尚未配置——记为跳过而不是中断整批任务。
            if (item.DestinationPath is not { } destination)
            {
                outcomes.Add(new ConversionOutcome(
                    item.SourcePath, null, ConversionState.Skipped,
                    item.SourceInfo?.FileSize ?? 0, 0, TimeSpan.Zero,
                    item.Message ?? "未配置输出位置。"));
                completed++;
                progress?.Report(new ConversionProgress(completed, total, item.SourceFileName, null, item.SourcePath));
                continue;
            }

            progress?.Report(new ConversionProgress(completed, total, item.SourceFileName, null));

            var outcome = await _encoder
                .EncodeAsync(item.SourcePath, destination, options, cancellationToken)
                .ConfigureAwait(false);

            outcomes.Add(outcome);
            completed++;
            progress?.Report(new ConversionProgress(completed, total, item.SourceFileName, outcome));
        }

        stopwatch.Stop();
        return new ConversionSummary(outcomes, stopwatch.Elapsed, canceled);
    }
}
