using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PcStatsMonitor.Controls;

/// <summary>
/// Turns a 0-100 percentage into proportional <see cref="GridLength"/> stars, so a two-column
/// Grid can draw a fill bar with no pixel measurement involved.
///
/// Used instead of a templated ProgressBar: that template's PART_Track had no content, so WPF
/// measured it at zero and sized the indicator to zero, leaving the memory bar looking greyed
/// out with no coloured fill (client round 21, item 4).
/// </summary>
public class PercentToGridLengthConverter : IValueConverter
{
    /// <summary>Pass "fill" for the used portion, anything else for the remainder.</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double percent = ToPercent(value);
        bool isFill = string.Equals(parameter as string, "fill", StringComparison.OrdinalIgnoreCase);
        return new GridLength(isFill ? percent : 100 - percent, GridUnitType.Star);
    }

    private static double ToPercent(object value)
    {
        double percent = value switch
        {
            double d => d,
            float f => f,
            int i => i,
            _ => 0
        };

        if (double.IsNaN(percent) || double.IsInfinity(percent)) return 0;
        return Math.Clamp(percent, 0, 100);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
