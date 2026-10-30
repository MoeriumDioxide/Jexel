namespace Jexel.Core.Models;

/// <summary>
/// 输出文件位置策略。
/// </summary>
public enum OutputLocationMode
{
    /// <summary>输出到用户指定的文件夹。</summary>
    CustomFolder = 0,

    /// <summary>原地输出：输出文件与源文件位于同一目录。</summary>
    AlongsideSource = 1,
}
