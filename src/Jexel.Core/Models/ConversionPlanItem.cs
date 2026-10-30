namespace Jexel.Core.Models;

/// <summary>
/// 转换计划中的一项：一条源文件到目标文件的映射及其当前状态。
/// </summary>
public sealed class ConversionPlanItem
{
    /// <summary>创建计划项。</summary>
    /// <param name="sourcePath">源文件绝对路径。</param>
    /// <param name="destinationPath">目标文件绝对路径；当状态非 Ready 时可能为 null。</param>
    /// <param name="status">初始状态。</param>
    /// <param name="sourceInfo">源图像元信息。</param>
    /// <param name="message">附加说明（错误原因等）。</param>
    public ConversionPlanItem(
        string sourcePath,
        string? destinationPath,
        PlanItemStatus status,
        ImageInfo? sourceInfo = null,
        string? message = null)
    {
        SourcePath = sourcePath;
        DestinationPath = destinationPath;
        Status = status;
        SourceInfo = sourceInfo;
        Message = message;
    }

    /// <summary>源文件绝对路径。</summary>
    public string SourcePath { get; }

    /// <summary>目标文件绝对路径。</summary>
    public string? DestinationPath { get; internal set; }

    /// <summary>计划状态。</summary>
    public PlanItemStatus Status { get; internal set; }

    /// <summary>源图像元信息。</summary>
    public ImageInfo? SourceInfo { get; internal set; }

    /// <summary>附加说明。</summary>
    public string? Message { get; internal set; }

    /// <summary>源文件名。</summary>
    public string SourceFileName => Path.GetFileName(SourcePath);

    /// <summary>目标文件名。</summary>
    public string? DestinationFileName => DestinationPath is null ? null : Path.GetFileName(DestinationPath);

    /// <summary>该目标文件是否由重命名策略产生（用于界面提示）。</summary>
    public bool WasRenamed { get; internal set; }
}
