using Jexel.Core.Models;

namespace Jexel.Core.Services;

/// <summary>
/// 依据源文件列表与选项生成可预览、可执行的转换计划。
/// 计划阶段就会为每个文件计算好目标路径，用户因此在开始转换前就能看到输出文件名。
/// </summary>
public static class ConversionPlanner
{
    /// <summary>
    /// 生成转换计划。重复的源文件会被自动忽略。
    /// </summary>
    /// <param name="sourcePaths">源文件路径集合。</param>
    /// <param name="options">转换选项。</param>
    /// <param name="reservedTargets">
    /// 已被占用的目标路径集合；跨批次追加文件时传入同一个集合即可避免目标重名。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static IReadOnlyList<ConversionPlanItem> Build(
        IEnumerable<string> sourcePaths,
        ConversionOptions options,
        ISet<string>? reservedTargets = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        ArgumentNullException.ThrowIfNull(options);

        var plan = new List<ConversionPlanItem>();
        var seenSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        reservedTargets ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawPath in sourcePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(rawPath))
            {
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(rawPath);
            }
            catch (Exception)
            {
                continue;
            }

            if (!seenSources.Add(fullPath))
            {
                continue;
            }

            var item = BuildItem(fullPath, options, reservedTargets);
            plan.Add(item);
        }

        return plan;
    }

    /// <summary>重新计算单个计划项的目标路径（选项变更后调用）。</summary>
    public static ConversionPlanItem Replan(
        ConversionPlanItem item,
        ConversionOptions options,
        ISet<string>? reservedTargets = null)
    {
        // 不可读或缺失的文件不参与输出规划。
        if (item.Status is PlanItemStatus.Missing or PlanItemStatus.Unsupported)
        {
            return item;
        }

        var destination = OutputPathPlanner.ResolveDestination(
            item.SourcePath, options, out var renamed, reservedTargets);

        item.DestinationPath = destination;
        item.WasRenamed = renamed;

        if (destination is not null)
        {
            item.Status = PlanItemStatus.Ready;
            item.Message = null;
            return item;
        }

        // 区分两种空目标：输出位置还没配置好，还是按"跳过"策略主动放弃。
        var needsOutputFolder = options.Location == OutputLocationMode.CustomFolder
                                && string.IsNullOrWhiteSpace(options.OutputFolder);

        item.Status = needsOutputFolder ? PlanItemStatus.Ready : PlanItemStatus.Skipped;
        item.Message = needsOutputFolder ? "请先选择输出文件夹。" : "目标文件已存在，按当前策略跳过。";

        return item;
    }

    private static ConversionPlanItem BuildItem(
        string fullPath,
        ConversionOptions options,
        ISet<string> reservedTargets)
    {
        if (!File.Exists(fullPath))
        {
            return new ConversionPlanItem(fullPath, null, PlanItemStatus.Missing, null, "文件不存在。");
        }

        if (!ImageFormatCatalog.TryReadInfo(fullPath, out var info))
        {
            return new ConversionPlanItem(fullPath, null, PlanItemStatus.Unsupported, null, "无法识别的图像格式。");
        }

        var item = new ConversionPlanItem(fullPath, null, PlanItemStatus.Ready, info);
        return Replan(item, options, reservedTargets);
    }
}
