namespace Jexel.Core.Models;

/// <summary>
/// 计划项在当前状态下的可用性。
/// </summary>
public enum PlanItemStatus
{
    /// <summary>已就绪，可以转换。</summary>
    Ready = 0,

    /// <summary>不可读取或格式不受支持。</summary>
    Unsupported = 1,

    /// <summary>目标文件已存在且策略为跳过。</summary>
    Skipped = 2,

    /// <summary>源文件不存在或无法访问。</summary>
    Missing = 3,
}

/// <summary>
/// 单个文件的转换执行结果状态。
/// </summary>
public enum ConversionState
{
    /// <summary>尚未处理。</summary>
    Pending = 0,

    /// <summary>正在转换。</summary>
    Converting = 1,

    /// <summary>转换成功。</summary>
    Succeeded = 2,

    /// <summary>转换失败。</summary>
    Failed = 3,

    /// <summary>已跳过。</summary>
    Skipped = 4,

    /// <summary>已取消。</summary>
    Canceled = 5,
}
