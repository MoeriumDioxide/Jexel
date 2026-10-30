using Microsoft.UI.Xaml;

namespace Jexel.App;

/// <summary>
/// 应用入口。
/// </summary>
public partial class App : Application
{
    private Window? _window;

    /// <summary>初始化应用并挂接全局异常处理。</summary>
    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    /// <summary>获取当前主窗口。</summary>
    public static Window? MainWindow { get; private set; }

    /// <inheritdoc />
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        MainWindow = _window;
        _window.Activate();
    }

    /// <summary>
    /// 兜底异常处理：记录日志并阻止应用直接退出，避免用户丢失已排队的任务。
    /// 需要说明的是，转换过程中的写入已经做到"临时文件 + 原子改名"，
    /// 因此任何异常都不会破坏源文件，也不会留下不完整的输出。
    /// </summary>
    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        TryWriteCrashLog(e.Exception);
    }

    private static void TryWriteCrashLog(Exception? exception)
        => LogError("crash", exception);

    /// <summary>
    /// 把异常追加写入本地日志，便于排查非致命错误。
    /// 日志位于 %LOCALAPPDATA%\Jexel\logs。
    /// </summary>
    /// <param name="category">日志分类，用作文件名。</param>
    /// <param name="exception">要记录的异常。</param>
    internal static void LogError(string category, Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Jexel",
                "logs");
            Directory.CreateDirectory(directory);

            var file = Path.Combine(directory, $"{category}-{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(
                file,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // 日志写入失败不影响应用继续运行。
        }
    }
}
