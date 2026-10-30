namespace Jexel.Core.Models;

/// <summary>
/// 当目标文件已存在时的处理策略。
/// </summary>
public enum ConflictPolicy
{
    /// <summary>永不覆盖已存在文件，自动追加 (1)、(2) 等序号生成唯一文件名。默认且最安全。</summary>
    Rename = 0,

    /// <summary>跳过该文件，不做任何写入。</summary>
    Skip = 1,

    /// <summary>覆盖已存在的输出文件。源文件在任何情况下都不会被覆盖。</summary>
    Overwrite = 2,
}
