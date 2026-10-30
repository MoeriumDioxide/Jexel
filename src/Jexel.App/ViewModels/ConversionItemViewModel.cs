using CommunityToolkit.Mvvm.ComponentModel;
using Jexel.App.Helpers;
using Jexel.Core.Models;

namespace Jexel.App.ViewModels;

/// <summary>
/// 文件列表中一行的视图模型：左侧是输入文件，右侧是对应的输出文件。
/// </summary>
public sealed partial class ConversionItemViewModel : ObservableObject
{
    private readonly ConversionPlanItem _model;

    [ObservableProperty]
    private string _destinationFileName = "—";

    [ObservableProperty]
    private long _destinationSize;

    [ObservableProperty]
    private string _destinationSizeText = "—";

    [ObservableProperty]
    private string _reductionText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenDestination))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [NotifyPropertyChangedFor(nameof(IsSucceeded))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private ConversionState _state;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDestination))]
    private string? _destinationPath;

    /// <summary>创建行视图模型。</summary>
    /// <param name="model">底层计划项。</param>
    public ConversionItemViewModel(ConversionPlanItem model)
    {
        _model = model;

        var info = model.SourceInfo;
        SourceFormat = info?.Format ?? "未知";
        SourceDimensions = info?.Dimensions ?? "—";
        SourceSize = info?.FileSize ?? 0;
        SourceSizeText = SourceSize > 0 ? ByteSizeFormatter.Format(SourceSize) : "—";
        SourceDetails = info is null
            ? "无法读取"
            : $"{SourceFormat} · {SourceDimensions} · {SourceSizeText}";

        _destinationPath = model.DestinationPath;
        _destinationFileName = model.DestinationFileName ?? "—";
        _errorMessage = model.Message;
        _state = model.Status switch
        {
            PlanItemStatus.Ready => ConversionState.Pending,
            PlanItemStatus.Skipped => ConversionState.Skipped,
            PlanItemStatus.Unsupported or PlanItemStatus.Missing => ConversionState.Failed,
            _ => ConversionState.Pending,
        };
    }

    /// <summary>底层计划项。</summary>
    public ConversionPlanItem Model => _model;

    /// <summary>源文件绝对路径。</summary>
    public string SourcePath => _model.SourcePath;

    /// <summary>源文件名。</summary>
    public string SourceFileName => _model.SourceFileName;

    /// <summary>源格式名称。</summary>
    public string SourceFormat { get; }

    /// <summary>源像素尺寸。</summary>
    public string SourceDimensions { get; }

    /// <summary>源字节数。</summary>
    public long SourceSize { get; }

    /// <summary>源体积文本。</summary>
    public string SourceSizeText { get; }

    /// <summary>源信息摘要行。</summary>
    public string SourceDetails { get; }

    /// <summary>目标文件是否已生成。</summary>
    public bool HasDestination => !string.IsNullOrEmpty(DestinationPath);

    /// <summary>是否可以打开目标文件。</summary>
    public bool CanOpenDestination
        => State == ConversionState.Succeeded
           && !string.IsNullOrEmpty(DestinationPath)
           && File.Exists(DestinationPath);

    /// <summary>是否转换成功。</summary>
    public bool IsSucceeded => State == ConversionState.Succeeded;

    /// <summary>是否失败。</summary>
    public bool IsFailed => State == ConversionState.Failed;

    /// <summary>状态描述文本。</summary>
    public string StatusText => State switch
    {
        // 计划项的提示（例如"请先选择输出文件夹"）优先于默认文案。
        ConversionState.Pending => _model.Message ?? "等待转换",
        ConversionState.Converting => "转换中…",
        ConversionState.Succeeded => "已完成",
        ConversionState.Failed => "失败",
        ConversionState.Skipped => _model.Message ?? "已跳过",
        ConversionState.Canceled => "已取消",
        _ => string.Empty,
    };

    /// <summary>依据执行结果刷新显示数据。</summary>
    /// <param name="outcome">转换结果。</param>
    public void ApplyOutcome(ConversionOutcome outcome)
    {
        State = outcome.State;
        ErrorMessage = outcome.ErrorMessage;

        if (outcome.Succeeded)
        {
            DestinationPath = outcome.DestinationPath;
            DestinationFileName = Path.GetFileName(outcome.DestinationPath) ?? "—";
            DestinationSize = outcome.DestinationSize;
            DestinationSizeText = ByteSizeFormatter.Format(outcome.DestinationSize);
            ReductionText = ByteSizeFormatter.FormatReduction(
                outcome.SizeReductionRatio, outcome.SourceSize, outcome.DestinationSize);
        }
        else
        {
            DestinationSize = 0;
            DestinationSizeText = "—";
            ReductionText = string.Empty;
            if (outcome.DestinationPath is not null)
            {
                DestinationPath = outcome.DestinationPath;
            }
        }
    }

    /// <summary>恢复到等待状态（重新开始转换前调用）。</summary>
    public void ResetToPending()
    {
        State = ConversionState.Pending;
        ErrorMessage = null;
        DestinationSize = 0;
        DestinationSizeText = "—";
        ReductionText = string.Empty;
    }

    /// <summary>标记该行正在转换。</summary>
    public void MarkConverting()
    {
        // 已经处于终态的行（例如按"跳过"策略处理的行）不应回到运行态。
        if (State is ConversionState.Succeeded
            or ConversionState.Failed
            or ConversionState.Skipped
            or ConversionState.Canceled)
        {
            return;
        }

        State = ConversionState.Converting;
    }

    /// <summary>
    /// 选项变更后同步计划项的最新目标路径与状态。
    /// 计划状态（就绪 / 跳过 / 不可读）与执行结果（成功 / 失败 / 取消）是两回事：
    /// 前者随时会因设置变化而重算，后者只由真实转换产生，不能被覆盖。
    /// </summary>
    public void RefreshFromPlan()
    {
        DestinationPath = _model.DestinationPath;
        DestinationFileName = _model.DestinationFileName ?? "—";

        if (State is ConversionState.Succeeded or ConversionState.Failed or ConversionState.Canceled)
        {
            OnPropertyChanged(nameof(StatusText));
            return;
        }

        State = _model.Status switch
        {
            PlanItemStatus.Ready => ConversionState.Pending,
            PlanItemStatus.Skipped => ConversionState.Skipped,
            PlanItemStatus.Unsupported or PlanItemStatus.Missing => ConversionState.Failed,
            _ => ConversionState.Pending,
        };

        // 提示信息（例如"目标文件已存在，按当前策略跳过。"）随计划状态一起更新。
        ErrorMessage = _model.Message;
        DestinationSize = 0;
        DestinationSizeText = "—";
        ReductionText = string.Empty;

        OnPropertyChanged(nameof(StatusText));
    }
}
