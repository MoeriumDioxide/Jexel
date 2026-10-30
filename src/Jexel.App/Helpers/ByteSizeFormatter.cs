using System.Globalization;

namespace Jexel.App.Helpers;

/// <summary>
/// 把字节数格式化为界面友好的文本。
/// </summary>
public static class ByteSizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>格式化为带单位的字符串，例如 "2.4 MB"。</summary>
    public static string Format(long bytes)
    {
        if (bytes < 0)
        {
            return "—";
        }

        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var format = value >= 100 ? "0" : value >= 10 ? "0.0" : "0.00";
        return string.Create(CultureInfo.InvariantCulture, $"{value.ToString(format, CultureInfo.InvariantCulture)} {Units[unit]}");
    }

    /// <summary>格式化体积变化，例如 "减小 59.2%" 或 "增加 3.1%"。</summary>
    public static string FormatReduction(double ratio, long sourceBytes, long destinationBytes)
    {
        if (sourceBytes <= 0 || destinationBytes <= 0)
        {
            return string.Empty;
        }

        var percent = Math.Abs(ratio) * 100;
        if (percent < 0.05)
        {
            return "体积基本持平";
        }

        return ratio > 0
            ? $"减小 {percent:0.#}%"
            : $"增加 {percent:0.#}%";
    }
}
