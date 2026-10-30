using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jexel.App.Helpers;
using Jexel.Core.Models;
using Jexel.Core.Services;
using Microsoft.UI.Dispatching;

namespace Jexel.App.ViewModels;

/// <summary>
/// 主界面的视图模型：管理文件清单、压缩设置、输出位置与转换编排。
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly BatchConverter _converter = new();
    private readonly HashSet<string> _knownSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ConversionItemViewModel> _rowIndex = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherQueue? _dispatcherQueue;
    private CancellationTokenSource? _cancellation;

    public MainViewModel()
    {
        Items.CollectionChanged += OnItemsChanged;
    }

    /// <summary>文件清单（界面中从左到右呈现"输入 → 输出"对比）。</summary>
    public ObservableCollection<ConversionItemViewModel> Items { get; } = [];

    // ---------------------------------------------------------------- 压缩设置

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLosslessMode))]
    [NotifyPropertyChangedFor(nameof(IsLossyMode))]
    [NotifyPropertyChangedFor(nameof(ModeDescription))]
    private CompressionMode _compressionMode = CompressionMode.Lossless;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QualityText))]
    private double _quality = ConversionOptions.DefaultQuality;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffortText))]
    private double _effort = ConversionOptions.DefaultEffort;

    /// <summary>是否在输出中保留源图像的 ICC 配置文件。</summary>
    [ObservableProperty]
    private bool _preserveIccProfile = true;

    /// <summary>无损模式单选按钮绑定（false 与 null 都会被忽略，因为取消选中不改变模式）。</summary>
    public bool? IsLosslessMode
    {
        get => CompressionMode == CompressionMode.Lossless;
        set
        {
            if (value == true && CompressionMode != CompressionMode.Lossless)
            {
                CompressionMode = CompressionMode.Lossless;
            }
        }
    }

    /// <summary>有损模式单选按钮绑定。</summary>
    public bool? IsLossyMode
    {
        get => CompressionMode == CompressionMode.Lossy;
        set
        {
            if (value == true && CompressionMode != CompressionMode.Lossy)
            {
                CompressionMode = CompressionMode.Lossy;
            }
        }
    }

    /// <summary>当前模式的说明文本。</summary>
    public string ModeDescription => CompressionMode == CompressionMode.Lossless
        ? "解码结果与源图像逐像素完全一致，适合存档与二次编辑。"
        : "按质量目标舍弃部分细节，体积通常显著更小。";

    /// <summary>质量数值显示。</summary>
    public string QualityText => $"{(int)Math.Round(Quality)}";

    /// <summary>编码强度数值显示。</summary>
    public string EffortText => $"{(int)Math.Round(Effort)}";

    // ---------------------------------------------------------------- 输出设置

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomFolderMode))]
    [NotifyPropertyChangedFor(nameof(IsAlongsideMode))]
    private OutputLocationMode _outputLocationMode = OutputLocationMode.AlongsideSource;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputFolderDisplay))]
    private string? _outputFolder;

    [ObservableProperty]
    private ConflictPolicy _conflictPolicy = ConflictPolicy.Rename;

    /// <summary>输出到指定文件夹。</summary>
    public bool? IsCustomFolderMode
    {
        get => OutputLocationMode == OutputLocationMode.CustomFolder;
        set
        {
            if (value == true && OutputLocationMode != OutputLocationMode.CustomFolder)
            {
                OutputLocationMode = OutputLocationMode.CustomFolder;
            }
        }
    }

    /// <summary>原地输出（与源文件同目录）。</summary>
    public bool? IsAlongsideMode
    {
        get => OutputLocationMode == OutputLocationMode.AlongsideSource;
        set
        {
            if (value == true && OutputLocationMode != OutputLocationMode.AlongsideSource)
            {
                OutputLocationMode = OutputLocationMode.AlongsideSource;
            }
        }
    }

    /// <summary>输出文件夹的显示文本。</summary>
    public string OutputFolderDisplay => string.IsNullOrWhiteSpace(OutputFolder)
        ? "尚未选择输出文件夹"
        : OutputFolder;

    // ---------------------------------------------------------------- 运行状态

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(StartConversionCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _statusMessage = "把图像拖到窗口里，或使用「添加文件」「添加文件夹」。";

    [ObservableProperty]
    private bool _isDragActive;

    [ObservableProperty]
    private int _fileCount;

    [ObservableProperty]
    private int _readyCount;

    [ObservableProperty]
    private string _totalSourceText = "—";

    [ObservableProperty]
    private string _totalDestinationText = "—";

    [ObservableProperty]
    private string _totalReductionText = string.Empty;

    /// <summary>是否空闲（用于启用设置控件）。</summary>
    public bool IsNotBusy => !IsBusy;

    /// <summary>是否已完成至少一个文件（用于展示汇总）。</summary>
    [ObservableProperty]
    private bool _hasResults;

    /// <summary>清单是否为空（用于显示空状态提示）。</summary>
    [ObservableProperty]
    private bool _isEmpty = true;

    /// <summary>文件数量显示文本。</summary>
    [ObservableProperty]
    private string _fileCountText = "尚未添加文件";

    /// <summary>清单中是否有文件（用于显示汇总区）。</summary>
    public bool HasAnyFile => Items.Count > 0;

    /// <summary>是否处于有损模式（用于启用质量滑块）。</summary>
    public bool IsLossySelected => CompressionMode == CompressionMode.Lossy;

    /// <summary>是否输出到指定文件夹（用于启用文件夹选择按钮）。</summary>
    public bool IsCustomFolderSelected => OutputLocationMode == OutputLocationMode.CustomFolder;

    /// <summary>目标文件冲突策略的下拉框索引。</summary>
    public int ConflictPolicyIndex
    {
        get => (int)ConflictPolicy;
        set => ConflictPolicy = value switch
        {
            1 => ConflictPolicy.Skip,
            2 => ConflictPolicy.Overwrite,
            _ => ConflictPolicy.Rename,
        };
    }

    // ---------------------------------------------------------------- 生命周期

    /// <summary>追加文件前的准备结果。</summary>
    private sealed record AppendPreparation(
        List<string> Candidates,
        ConversionOptions? Options,
        HashSet<string>? Reserved);

    /// <summary>注入 UI 线程调度器，使后台转换进度能够安全地回到界面线程。</summary>
    /// <param name="dispatcherQueue">界面线程的调度器。</param>
    public void AttachDispatcher(DispatcherQueue dispatcherQueue)
        => _dispatcherQueue = dispatcherQueue;

    /// <summary>
    /// 保证委托在界面线程上执行。
    /// 视图模型不依赖 SynchronizationContext，因此无论是在窗口构造期间还是从后台线程调用都安全——
    /// 这避免了"看似静默卡住、实为跨线程更新被吞掉"的问题。
    /// </summary>
    private Task RunOnUiThreadAsync(Action action)
        => RunOnUiThreadAsync(() =>
        {
            action();
            return true;
        });

    /// <summary>在界面线程上执行并返回结果的泛型版本。</summary>
    private Task<T> RunOnUiThreadAsync<T>(Func<T> func)
    {
        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
        {
            return Task.FromResult(func());
        }

        var completion = new TaskCompletionSource<T>();
        if (!_dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    completion.SetResult(func());
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            }))
        {
            completion.SetException(new InvalidOperationException("无法把操作调度到界面线程。"));
        }

        return completion.Task;
    }

    // ---------------------------------------------------------------- 文件导入

    /// <summary>
    /// 依据文件或文件夹路径批量导入。
    /// 文件夹会被展开为其中所有可转换的图像。
    /// </summary>
    /// <param name="paths">文件或文件夹路径。</param>
    public async Task ImportPathsAsync(IEnumerable<string> paths)
    {
        var files = new List<string>();
        var folders = new List<string>();

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (Directory.Exists(path))
            {
                folders.Add(path);
            }
            else if (File.Exists(path))
            {
                files.Add(path);
            }
        }

        if (folders.Count > 0)
        {
            await RunOnUiThreadAsync(() => StatusMessage = "正在扫描文件夹…").ConfigureAwait(false);

            var scanned = await Task.Run(() =>
            {
                var collected = new List<string>();
                foreach (var folder in folders)
                {
                    collected.AddRange(ImageFormatCatalog.EnumerateConvertibleFiles(folder));
                }

                return collected;
            }).ConfigureAwait(false);

            files.AddRange(scanned);
        }

        await AppendFilesAsync(files).ConfigureAwait(false);
    }

    /// <summary>把候选文件加入清单，自动忽略重复项与不可读文件。</summary>
    /// <param name="paths">候选文件路径。</param>
    public async Task AppendFilesAsync(IEnumerable<string> paths)
    {
        // 准备阶段会读写界面状态（Items / StatusMessage），因此整体在界面线程完成。
        var preparation = await RunOnUiThreadAsync(() =>
        {
            var collected = new List<string>();
            foreach (var path in paths)
            {
                var full = SafeFullPath(path);
                if (full is null || !File.Exists(full))
                {
                    continue;
                }

                if (_knownSources.Add(full))
                {
                    collected.Add(full);
                }
            }

            if (collected.Count == 0)
            {
                StatusMessage = "没有新增可转换的图像。";
                return new AppendPreparation(collected, null, null);
            }

            StatusMessage = $"正在读取 {collected.Count} 个文件…";
            return new AppendPreparation(collected, BuildOptions(), CollectReservedTargets());
        }).ConfigureAwait(false);

        if (preparation.Options is null || preparation.Reserved is null)
        {
            return;
        }

        // 读取文件头是一批磁盘 IO，放到后台执行以免阻塞界面。
        var plan = await Task.Run(
            () => ConversionPlanner.Build(preparation.Candidates, preparation.Options, preparation.Reserved))
            .ConfigureAwait(false);

        await RunOnUiThreadAsync(() =>
        {
            var added = 0;
            var rejected = 0;
            foreach (var item in plan)
            {
                var row = new ConversionItemViewModel(item);
                Items.Add(row);

                if (item.Status is PlanItemStatus.Ready or PlanItemStatus.Skipped)
                {
                    added++;
                }
                else
                {
                    rejected++;
                    _knownSources.Remove(item.SourcePath);
                }
            }

            StatusMessage = rejected == 0
                ? $"已添加 {added} 个文件。"
                : $"已添加 {added} 个文件，{rejected} 个无法识别已被忽略。";

            RecomputeSummary();
        }).ConfigureAwait(false);
    }

    /// <summary>移除单个文件。</summary>
    [RelayCommand]
    private void RemoveItem(ConversionItemViewModel? item)
    {
        if (item is null || IsBusy)
        {
            return;
        }

        Items.Remove(item);
        _knownSources.Remove(item.SourcePath);
        RefreshPlan();
    }

    /// <summary>清空清单。</summary>
    [RelayCommand]
    private void ClearAll()
    {
        if (IsBusy)
        {
            return;
        }

        Items.Clear();
        _knownSources.Clear();
        HasResults = false;
        ProgressPercent = 0;
        StatusMessage = "清单已清空。";
        RecomputeSummary();
    }

    // ---------------------------------------------------------------- 转换

    private bool CanStartConversion()
        => !IsBusy
           && Items.Any(static i => i.Model.Status == PlanItemStatus.Ready);

    /// <summary>开始批量转换。</summary>
    [RelayCommand(CanExecute = nameof(CanStartConversion))]
    private async Task StartConversionAsync()
    {
        var options = BuildOptions();
        var validation = options.Validate();
        if (validation is not null)
        {
            StatusMessage = validation;
            return;
        }

        // 先清空上一轮的结果，再按当前设置重新规划，
        // 这样改动输出文件夹或压缩模式后重新转换会写到正确的位置。
        foreach (var item in Items)
        {
            item.ResetToPending();
        }

        RefreshPlan();

        var plan = Items.Where(static i => i.Model.Status == PlanItemStatus.Ready)
            .Select(static i => i.Model)
            .ToList();

        if (plan.Count == 0)
        {
            StatusMessage = "没有可转换的文件。";
            return;
        }

        IsBusy = true;
        HasResults = false;
        ProgressPercent = 0;
        _cancellation = new CancellationTokenSource();
        StatusMessage = $"正在转换 {plan.Count} 个文件…";

        var progress = new Progress<ConversionProgress>(OnProgressReported);

        try
        {
            var summary = await _converter
                .RunAsync(plan, options, progress, _cancellation.Token)
                .ConfigureAwait(false);

            await RunOnUiThreadAsync(() =>
            {
                ApplyOutcomes(summary);

                var reduction = summary.TotalReductionRatio;
                StatusMessage = summary.WasCanceled
                    ? $"已取消：完成 {summary.SucceededCount} 个，失败 {summary.FailedCount} 个。"
                    : summary.FailedCount > 0
                        ? $"完成 {summary.SucceededCount} 个，失败 {summary.FailedCount} 个，耗时 {summary.TotalElapsed.TotalSeconds:0.0} 秒。"
                        : $"全部完成：{summary.SucceededCount} 个文件，体积{DescribeReduction(reduction)}，耗时 {summary.TotalElapsed.TotalSeconds:0.0} 秒。";
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await RunOnUiThreadAsync(() => StatusMessage = "转换已取消。").ConfigureAwait(false);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;

            await RunOnUiThreadAsync(() =>
            {
                IsBusy = false;
                ProgressPercent = 0;

                // 任务被取消或意外中断时，可能仍有行停留在"转换中"，复位以免界面永久停在运行态。
                foreach (var item in Items.Where(static i => i.State == ConversionState.Converting))
                {
                    item.ResetToPending();
                }

                RecomputeSummary();

                // 转换可能改变了磁盘状态，重新规划以便再次运行时正确命名。
                RefreshPlan();
            }).ConfigureAwait(false);
        }
    }

    /// <summary>取消正在进行的转换。</summary>
    [RelayCommand]
    private void CancelConversion()
        => _cancellation?.Cancel();

    private void OnProgressReported(ConversionProgress progress)
    {
        void Update()
        {
            ProgressPercent = progress.Percent;

            if (progress.Outcome is not null)
            {
                FindRow(progress.Outcome.SourcePath)?.ApplyOutcome(progress.Outcome);
                RecomputeSummary();
                return;
            }

            // 开始处理某个文件时立即把对应行标为"转换中"，用户能看出进度停在哪一行。
            if (progress.CurrentSourcePath is { } currentSource)
            {
                FindRow(currentSource)?.MarkConverting();
            }

            if (!string.IsNullOrEmpty(progress.CurrentFileName))
            {
                StatusMessage = $"正在转换 {progress.CurrentFileName}（{progress.CompletedCount + 1}/{progress.TotalCount}）";
            }
        }

        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
        {
            Update();
        }
        else
        {
            _dispatcherQueue.TryEnqueue(Update);
        }
    }

    private void ApplyOutcomes(ConversionSummary summary)
    {
        foreach (var outcome in summary.Outcomes)
        {
            FindRow(outcome.SourcePath)?.ApplyOutcome(outcome);
        }

        HasResults = summary.SucceededCount > 0;
    }

    // ---------------------------------------------------------------- 设置变更响应

    partial void OnCompressionModeChanged(CompressionMode value)
    {
        OnPropertyChanged(nameof(ModeDescription));
        OnPropertyChanged(nameof(IsLossySelected));
    }

    partial void OnOutputLocationModeChanged(OutputLocationMode value)
    {
        OnPropertyChanged(nameof(IsCustomFolderSelected));
        RefreshPlan();
    }

    partial void OnOutputFolderChanged(string? value) => RefreshPlan();

    partial void OnConflictPolicyChanged(ConflictPolicy value)
    {
        OnPropertyChanged(nameof(ConflictPolicyIndex));
        RefreshPlan();
    }

    /// <summary>按当前设置重算所有目标路径。</summary>
    public void RefreshPlan()
    {
        if (Items.Count == 0)
        {
            RecomputeSummary();
            return;
        }

        var options = BuildOptions();
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in Items)
        {
            // 已经产出结果的行必须保留其真实输出路径：
            // 若重新规划，磁盘上刚生成的文件会被当作"已存在"从而被误改成 name (1).jxl。
            if (item.State == ConversionState.Succeeded)
            {
                if (!string.IsNullOrEmpty(item.DestinationPath))
                {
                    reserved.Add(item.DestinationPath);
                }

                continue;
            }

            ConversionPlanner.Replan(item.Model, options, reserved);
            item.RefreshFromPlan();
        }

        RecomputeSummary();
    }

    private ConversionOptions BuildOptions() => new()
    {
        Mode = CompressionMode,
        Quality = (int)Math.Round(Quality),
        Effort = (int)Math.Round(Effort),
        Location = OutputLocationMode,
        OutputFolder = OutputFolder,
        ConflictPolicy = ConflictPolicy,
        PreserveIccProfile = PreserveIccProfile,
    };

    private HashSet<string> CollectReservedTargets()
    {
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Items)
        {
            if (!string.IsNullOrEmpty(item.DestinationPath))
            {
                reserved.Add(item.DestinationPath);
            }
        }

        return reserved;
    }

    // ---------------------------------------------------------------- 汇总统计

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _rowIndex.Clear();
        }
        else
        {
            if (e.OldItems is not null)
            {
                foreach (ConversionItemViewModel item in e.OldItems)
                {
                    _rowIndex.Remove(item.SourcePath);
                }
            }

            if (e.NewItems is not null)
            {
                foreach (ConversionItemViewModel item in e.NewItems)
                {
                    _rowIndex[item.SourcePath] = item;
                }
            }
        }

        RecomputeSummary();
    }

    private ConversionItemViewModel? FindRow(string sourcePath)
        => _rowIndex.TryGetValue(sourcePath, out var row) ? row : null;

    private void RecomputeSummary()
    {
        FileCount = Items.Count;
        ReadyCount = Items.Count(static i => i.Model.Status == PlanItemStatus.Ready);
        IsEmpty = Items.Count == 0;
        FileCountText = Items.Count == 0 ? "尚未添加文件" : $"共 {Items.Count} 个文件";
        OnPropertyChanged(nameof(HasAnyFile));

        var sourceBytes = Items.Where(static i => i.SourceSize > 0).Sum(static i => i.SourceSize);
        var destinationBytes = Items.Where(static i => i.IsSucceeded).Sum(static i => i.DestinationSize);

        TotalSourceText = sourceBytes > 0 ? ByteSizeFormatter.Format(sourceBytes) : "—";

        if (destinationBytes > 0)
        {
            TotalDestinationText = ByteSizeFormatter.Format(destinationBytes);
            TotalReductionText = ByteSizeFormatter.FormatReduction(
                1.0 - (double)destinationBytes / Math.Max(sourceBytes, 1),
                sourceBytes,
                destinationBytes);
        }
        else
        {
            TotalDestinationText = "—";
            TotalReductionText = string.Empty;
        }

        StartConversionCommand.NotifyCanExecuteChanged();
    }

    private static string DescribeReduction(double ratio)
        => Math.Abs(ratio) < 0.0005
            ? "基本持平"
            : ratio > 0
                ? $"减小 {ratio * 100:0.#}%"
                : $"增加 {Math.Abs(ratio) * 100:0.#}%";

    private static string? SafeFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
