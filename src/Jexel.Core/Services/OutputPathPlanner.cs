using Jexel.Core.Models;

namespace Jexel.Core.Services;

/// <summary>
/// 输出路径规划器。
/// 核心安全约束：
/// 1. 任何情况下都不会把目标路径指向源文件本身；
/// 2. 目标路径与源文件同目录时，若仅扩展名不同则视为安全（不冲突）；
/// 3. 目标已存在时按策略处理，默认追加序号生成唯一名，绝不静默覆盖。
/// </summary>
public static class OutputPathPlanner
{
    /// <summary>
    /// 计算单个源文件的目标路径。
    /// </summary>
    /// <param name="sourcePath">源文件绝对路径。</param>
    /// <param name="options">转换选项。</param>
    /// <param name="wasRenamed">是否因冲突而被重命名。</param>
    /// <param name="reserved">本次批次内已被占用的目标路径集合（大小写不敏感）。</param>
    /// <returns>目标绝对路径；尚未配置输出位置或按策略跳过时返回 null。</returns>
    public static string? ResolveDestination(
        string sourcePath,
        ConversionOptions options,
        out bool wasRenamed,
        ISet<string>? reserved = null)
    {
        wasRenamed = false;

        var sourceFullPath = Path.GetFullPath(sourcePath);

        // 目录尚未确定时返回 null 由调用方标记为"待配置"，而不是抛出异常中断整批导入。
        var directory = options.Location == OutputLocationMode.AlongsideSource
            ? Path.GetDirectoryName(sourceFullPath)
            : options.OutputFolder;

        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        directory = Path.GetFullPath(directory);
        var baseName = Path.GetFileNameWithoutExtension(sourceFullPath);
        var candidate = Path.Combine(directory, baseName + ImageFormatCatalog.JxlExtension);

        // 目标与源是同一个文件：源本身已是 .jxl，此时必须避免覆写源文件。
        var isSelfTarget = string.Equals(candidate, sourceFullPath, StringComparison.OrdinalIgnoreCase);

        if (!isSelfTarget && IsAvailable(candidate, reserved))
        {
            reserved?.Add(candidate);
            return candidate;
        }

        switch (options.ConflictPolicy)
        {
            case ConflictPolicy.Skip:
                // 仅当冲突来自已有文件时跳过；自指目标必须改名，否则会破坏源文件。
                if (!isSelfTarget && File.Exists(candidate))
                {
                    return null;
                }
                break;
        }

        if (options.ConflictPolicy == ConflictPolicy.Overwrite && !isSelfTarget)
        {
            // 覆盖模式：只有当目标确实不是源文件本身时才允许直接覆盖。
            reserved?.Add(candidate);
            return candidate;
        }

        // 重命名模式（也是自指/跳过场景的兜底）：寻找可用的唯一名称。
        var unique = FindUniqueName(directory, baseName, sourceFullPath, reserved);
        wasRenamed = true;
        return unique;
    }

    private static bool IsAvailable(string candidate, ISet<string>? reserved)
    {
        if (reserved is not null && reserved.Contains(candidate))
        {
            return false;
        }

        return !File.Exists(candidate);
    }

    private static string FindUniqueName(
        string directory,
        string baseName,
        string sourceFullPath,
        ISet<string>? reserved)
    {
        for (var index = 1; index < 10_000; index++)
        {
            var candidate = Path.Combine(directory, $"{baseName} ({index}){ImageFormatCatalog.JxlExtension}");
            var conflictsWithSource = string.Equals(candidate, sourceFullPath, StringComparison.OrdinalIgnoreCase);
            if (!conflictsWithSource && IsAvailable(candidate, reserved))
            {
                reserved?.Add(candidate);
                return candidate;
            }
        }

        // 极端情况下退化为带时间戳的文件名，保证仍然可用。
        var stamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");
        var fallback = Path.Combine(directory, $"{baseName} {stamp}{ImageFormatCatalog.JxlExtension}");
        reserved?.Add(fallback);
        return fallback;
    }
}
