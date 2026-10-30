using System.Runtime.InteropServices;
using Jexel.App.Services;
using Jexel.App.ViewModels;
using Jexel.Core.Services;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using WinRT.Interop;

namespace Jexel.App;

/// <summary>
/// 主窗口：负责窗口外观、拖放接收以及界面事件到视图模型的转发。
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int DefaultWidthDip = 1180;
    private const int DefaultHeightDip = 800;

    /// <summary>创建主窗口。</summary>
    public MainWindow()
    {
        // 必须在 InitializeComponent 之前完成赋值，x:Bind 在加载时即会求值。
        ViewModel = new MainViewModel();

        InitializeComponent();

        ViewModel.AttachDispatcher(DispatcherQueue);
        ConfigureWindow();
        HookDragAndDrop();

        Closed += (_, _) => ViewModel.CancelConversionCommand.Execute(null);

        // 命令行参数要等窗口完全激活后再处理，确保界面线程消息循环已就绪。
        Activated += OnWindowFirstActivated;
    }

    private void OnWindowFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnWindowFirstActivated;
        _ = ImportCommandLineArgumentsAsync();
    }

    /// <summary>主视图模型。</summary>
    public MainViewModel ViewModel { get; }

    private IntPtr WindowHandle => WindowNative.GetWindowHandle(this);

    // ---------------------------------------------------------------- 窗口外观

    private void ConfigureWindow()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        if (MicaController.IsSupported())
        {
            SystemBackdrop = new MicaBackdrop();
        }

        var hwnd = WindowHandle;
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        // 统一使用物理像素：GetMonitorInfo 与 AppWindow.Resize 的单位一致，
        // 避免不同 DPI 下窗口被算得过大或过小。
        var workArea = GetWorkArea(hwnd);
        var scale = GetDpiForWindow(hwnd) / 96.0;

        var width = Math.Min((int)Math.Round(DefaultWidthDip * scale), workArea.Width);
        var height = Math.Min((int)Math.Round(DefaultHeightDip * scale), workArea.Height);

        appWindow.Resize(new SizeInt32(width, height));
        appWindow.Move(new PointInt32(
            workArea.X + Math.Max(0, (workArea.Width - width) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - height) / 2)));

        // 任务栏与 Alt+Tab 使用的窗口图标。
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "JexelIcon.png");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }
        }
        catch (Exception)
        {
            // 图标缺失不影响功能。
        }
    }

    // ---------------------------------------------------------------- 拖放

    private void HookDragAndDrop()
    {
        // 列表自身也接受拖放，避免拖到列表上方时事件被内部容器吞掉。
        FileList.AllowDrop = true;
        FileList.DragOver += OnRootDragOver;
        FileList.DragLeave += OnRootDragLeave;
        FileList.Drop += OnRootDrop;
    }

    private void OnRootDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;

        if (e.DragUIOverride is { } overlay)
        {
            overlay.Caption = "添加到 Jexel";
            overlay.IsCaptionVisible = true;
            overlay.IsContentVisible = true;
        }

        ViewModel.IsDragActive = true;
    }

    private void OnRootDragLeave(object sender, DragEventArgs e)
        => ViewModel.IsDragActive = false;

    private async void OnRootDrop(object sender, DragEventArgs e)
    {
        ViewModel.IsDragActive = false;

        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items
                .Select(static item => item.Path)
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .ToList();

            if (paths.Count > 0)
            {
                await ViewModel.ImportPathsAsync(paths);
            }
        }
        catch (Exception)
        {
            ViewModel.StatusMessage = "无法读取拖入的内容。";
        }
        finally
        {
            deferral.Complete();
        }
    }

    // ---------------------------------------------------------------- 导入

    /// <summary>
    /// 处理通过命令行传入的路径，使应用可以接受"发送到"或拖到可执行文件上的用法。
    /// </summary>
    private async Task ImportCommandLineArgumentsAsync()
    {
        try
        {
            var paths = Environment.GetCommandLineArgs()
                .Skip(1)
                .Where(static p => !string.IsNullOrWhiteSpace(p) && (File.Exists(p) || Directory.Exists(p)))
                .ToList();

            if (paths.Count > 0)
            {
                await ViewModel.ImportPathsAsync(paths).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            App.LogError("import", ex);
            ViewModel.StatusMessage = $"导入失败：{ex.Message}";
        }
    }

    private async void OnAddFilesClick(object sender, RoutedEventArgs e)
    {
        var files = NativeFileDialog.PickFiles(
            WindowHandle,
            ImageFormatCatalog.FileDialogFilter,
            initialFolder: null,
            title: "选择图像文件");

        if (files.Count > 0)
        {
            await ViewModel.ImportPathsAsync(files);
        }
    }

    private async void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        var folder = NativeFileDialog.PickFolder(WindowHandle, null, "选择包含图像的文件夹");
        if (!string.IsNullOrEmpty(folder))
        {
            await ViewModel.ImportPathsAsync([folder]);
        }
    }

    private void OnPickOutputFolderClick(object sender, RoutedEventArgs e)
    {
        var folder = NativeFileDialog.PickFolder(WindowHandle, ViewModel.OutputFolder, "选择输出文件夹");
        if (!string.IsNullOrEmpty(folder))
        {
            ViewModel.OutputFolder = folder;
        }
    }

    // ---------------------------------------------------------------- 行内操作

    private void OnOpenSourceClick(object sender, RoutedEventArgs e)
    {
        if (GetRowItem(sender) is not { } item)
        {
            return;
        }

        if (!ShellService.OpenFile(item.SourcePath))
        {
            ViewModel.StatusMessage = "无法打开源文件，它可能已被移动或删除。";
        }
    }

    private void OnOpenDestinationClick(object sender, RoutedEventArgs e)
    {
        if (GetRowItem(sender) is not { } item || item.DestinationPath is not { } path)
        {
            return;
        }

        if (!ShellService.OpenFile(path))
        {
            ViewModel.StatusMessage = "无法打开输出文件，它可能已被移动或删除。";
        }
    }

    private void OnRemoveItemClick(object sender, RoutedEventArgs e)
    {
        if (GetRowItem(sender) is { } item)
        {
            ViewModel.RemoveItemCommand.Execute(item);
        }
    }

    private static ConversionItemViewModel? GetRowItem(object sender)
        => (sender as FrameworkElement)?.Tag as ConversionItemViewModel;

    private static (int X, int Y, int Width, int Height) GetWorkArea(IntPtr hwnd)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };

        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
        {
            return (
                info.Work.Left,
                info.Work.Top,
                info.Work.Right - info.Work.Left,
                info.Work.Bottom - info.Work.Top);
        }

        // 兜底：退回到主显示器尺寸。
        return (0, 0, GetSystemMetrics(SystemMetricScreenWidth), GetSystemMetrics(SystemMetricScreenHeight));
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private const uint MonitorDefaultToNearest = 2;
    private const int SystemMetricScreenWidth = 0;
    private const int SystemMetricScreenHeight = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }
}
