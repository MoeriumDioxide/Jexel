using System.Diagnostics;
using ImageMagick;
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
/// 批量转换器。
/// <para>
/// 并发策略：只有**小图**才会并行编码，大图仍然独占串行执行。
/// 理由是 libjxl 对大图本身就能吃满多核，再并行只会让多个任务互相争抢线程；
/// 而小图的图块不足以喂满线程池，串行处理会浪费 CPU。
/// </para>
/// <para>
/// 并行时同步下调 libjxl 的线程配额（核心数 ÷ 并发数），避免每个任务都尝试开满线程池
/// 造成过度订阅；串行处理大图时再恢复为不限制。
/// </para>
/// </summary>
public sealed class BatchConverter
{
    /// <summary>允许并行处理的像素上限：100 万像素。</summary>
    public const long SmallImagePixelCount = 1_000_000;

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
        var outcomeGate = new object();
        var runnable = plan.Where(static p => p.Status == PlanItemStatus.Ready).ToList();
        var total = runnable.Count;
        var completed = 0;
        var canceled = false;
        var parallelism = ResolveParallelism(runnable, options);
        var originalThreadLimit = ResourceLimits.Thread;
        var running = new List<Task>(parallelism);

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

        try
        {
            for (var index = 0; index < runnable.Count; index++)
            {
                var item = runnable[index];

                if (cancellationToken.IsCancellationRequested)
                {
                    // 先让在跑的任务结束，再把剩余项统一标记为取消。
                    await DrainAsync().ConfigureAwait(false);
                    canceled = true;

                    for (var rest = index; rest < runnable.Count; rest++)
                    {
                        var pending = runnable[rest];
                        Record(
                            new ConversionOutcome(
                                pending.SourcePath, pending.DestinationPath, ConversionState.Canceled,
                                pending.SourceInfo?.FileSize ?? 0, 0, TimeSpan.Zero, "已取消"),
                            pending);
                    }

                    break;
                }

                // 缺少目标路径说明输出位置尚未配置——记为跳过而不是中断整批任务。
                if (item.DestinationPath is not { } destination)
                {
                    Record(
                        new ConversionOutcome(
                            item.SourcePath, null, ConversionState.Skipped,
                            item.SourceInfo?.FileSize ?? 0, 0, TimeSpan.Zero,
                            item.Message ?? "未配置输出位置。"),
                        item);
                    continue;
                }

                if (parallelism == 1 || !IsSmallImage(item))
                {
                    // 大图独占：先等并行批次收尾，再把完整的线程配额交还给 libjxl。
                    await DrainAsync().ConfigureAwait(false);
                    ApplyThreadBudget(1);
                    await EncodeAsync(item, destination).ConfigureAwait(false);
                    continue;
                }

                if (running.Count >= parallelism)
                {
                    await DrainAsync().ConfigureAwait(false);
                }

                ApplyThreadBudget(parallelism);
                progress?.Report(new ConversionProgress(
                    Volatile.Read(ref completed), total, item.SourceFileName, null, item.SourcePath));

                running.Add(EncodeAsync(item, destination));
            }

            await DrainAsync().ConfigureAwait(false);
        }
        finally
        {
            // 线程配额是进程级全局设置，任何情况下都必须还原。
            ResourceLimits.Thread = originalThreadLimit;
        }

        stopwatch.Stop();
        return new ConversionSummary(outcomes, stopwatch.Elapsed, canceled);

        async Task EncodeAsync(ConversionPlanItem item, string destination)
        {
            try
            {
                var outcome = await _encoder
                    .EncodeAsync(item.SourcePath, destination, options, cancellationToken)
                    .ConfigureAwait(false);

                Record(outcome, item);
            }
            catch (Exception ex)
            {
                // 编码器内部已经捕获业务异常，这里只兜底，防止任务异常逃逸使 Task.WhenAll 抛出。
                Record(
                    new ConversionOutcome(
                        item.SourcePath, destination, ConversionState.Failed,
                        item.SourceInfo?.FileSize ?? 0, 0, TimeSpan.Zero, ex.Message),
                    item);
            }
        }

        void Record(ConversionOutcome outcome, ConversionPlanItem item)
        {
            // 并行任务会在不同线程上完成，结果列表与计数都必须同步访问。
            lock (outcomeGate)
            {
                outcomes.Add(outcome);
            }

            var done = Interlocked.Increment(ref completed);
            progress?.Report(new ConversionProgress(
                done, total, item.SourceFileName, outcome, item.SourcePath));
        }

        async Task DrainAsync()
        {
            if (running.Count == 0)
            {
                return;
            }

            await Task.WhenAll(running).ConfigureAwait(false);
            running.Clear();
        }
    }

    /// <summary>
    /// 计算本次批量的并发度。
    /// 用户显式指定时以其为准，否则只在存在多张小图时启用自动并行。
    /// </summary>
    private static int ResolveParallelism(
        IReadOnlyList<ConversionPlanItem> runnable,
        ConversionOptions options)
    {
        if (options.MaxParallelism > 0)
        {
            return Math.Clamp(options.MaxParallelism, 1, ConversionOptions.MaxParallelismLimit);
        }

        var smallCount = runnable.Count(static item => IsSmallImage(item));
        if (smallCount < 2)
        {
            return 1;
        }

        // 留一半核心给系统与界面线程，最多 4 路——小图并行的收益在此之前就已经饱和。
        return Math.Clamp(Environment.ProcessorCount / 2, 2, 4);
    }

    private static bool IsSmallImage(ConversionPlanItem item)
    {
        if (item.SourceInfo is not { } info)
        {
            return false;
        }

        return (long)info.Width * info.Height <= SmallImagePixelCount;
    }

    /// <summary>调整 libjxl 可用的工作线程数；0 表示不限制（用满所有核心）。</summary>
    private static void ApplyThreadBudget(int parallelism)
    {
        ResourceLimits.Thread = parallelism <= 1
            ? 0UL
            : (ulong)Math.Max(1, Environment.ProcessorCount / parallelism);
    }
}
