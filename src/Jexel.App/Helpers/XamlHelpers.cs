using Jexel.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Jexel.App.Helpers;

/// <summary>
/// 供 x:Bind 直接调用的转换函数，避免为每种状态额外编写 Converter 类。
/// </summary>
public static class XamlHelpers
{
    /// <summary>true 时显示。</summary>
    public static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>false 时显示。</summary>
    public static Visibility Collapsed(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>字符串非空时显示。</summary>
    public static Visibility VisibleIfNotEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>状态对应的语义色。</summary>
    public static Brush StateBrush(ConversionState state)
    {
        var (key, fallback) = state switch
        {
            ConversionState.Succeeded => ("SystemFillColorSuccessBrush", Color.FromArgb(255, 0x6C, 0xC1, 0x6C)),
            ConversionState.Failed => ("SystemFillColorCriticalBrush", Color.FromArgb(255, 0xFF, 0x5F, 0x5F)),
            ConversionState.Converting => ("AccentFillColorDefaultBrush", Color.FromArgb(255, 0x00, 0x78, 0xD4)),
            _ => ("TextFillColorSecondaryBrush", Color.FromArgb(255, 0x8A, 0x8A, 0x8A)),
        };

        return GetBrush(key, fallback);
    }

    /// <summary>状态对应的 Segoe Fluent 图标字形。</summary>
    public static string StateGlyph(ConversionState state) => state switch
    {
        ConversionState.Pending => "\uE823",
        ConversionState.Converting => "\uE895",
        ConversionState.Succeeded => "\uE73E",
        ConversionState.Failed => "\uE783",
        ConversionState.Skipped => "\uE738",
        ConversionState.Canceled => "\uE711",
        _ => "\uE9AE",
    };

    private static readonly Dictionary<string, Brush> BrushCache = [];

    /// <summary>
    /// 取出主题画刷；若主题字典中暂无该键，则退回一个在浅色与深色主题下都清晰可辨的语义色，
    /// 保证界面永远不会出现颜色缺失。
    /// </summary>
    private static Brush GetBrush(string resourceKey, Color fallback)
    {
        if (BrushCache.TryGetValue(resourceKey, out var cached))
        {
            return cached;
        }

        Brush brush;
        if (Application.Current?.Resources is { } resources
            && resources.TryGetValue(resourceKey, out var value)
            && value is Brush themed)
        {
            brush = themed;
        }
        else
        {
            brush = new SolidColorBrush(fallback);
        }

        BrushCache[resourceKey] = brush;
        return brush;
    }
}
