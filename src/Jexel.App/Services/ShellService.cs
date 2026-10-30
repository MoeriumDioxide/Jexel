using System.Diagnostics;

namespace Jexel.App.Services;

/// <summary>
/// 与资源管理器交互的辅助方法：打开文件、定位文件。
/// </summary>
internal static class ShellService
{
    /// <summary>用系统默认程序打开文件。返回是否成功发起调用。</summary>
    /// <param name="path">文件路径。</param>
    public static bool OpenFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        return Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    /// <summary>在资源管理器中选中该文件。</summary>
    /// <param name="path">文件路径。</param>
    public static bool RevealInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        // /select 需要目标路径，逗号后不留空格。
        return Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    private static bool Start(ProcessStartInfo startInfo)
    {
        try
        {
            Process.Start(startInfo);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
