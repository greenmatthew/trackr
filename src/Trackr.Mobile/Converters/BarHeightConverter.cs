using System.Globalization;

namespace Trackr.Mobile.Converters;

/// <summary>
/// Turns a bar's zero-to-one fraction into a height in device-independent units.
/// </summary>
/// <remarks>
/// The view model reports a fraction and nothing else, because how tall a chart is belongs to the
/// page it is drawn on. This is the one place that decides, which is why it lives in the MAUI
/// project rather than in Core.
/// <para>
/// A logged day is never given a height of zero. A bar too short to see says "nothing here", which
/// is the one thing it must not say about a day somebody recorded - so it is floored at a couple of
/// units. Days with nothing logged do not reach here: the template hides them outright.
/// </para>
/// </remarks>
public sealed class BarHeightConverter : IValueConverter
{
    /// <summary>The tallest a bar gets, matching the chart's own height less its labels.</summary>
    public double MaxHeight { get; set; } = 120d;

    /// <summary>Enough to be visible on any density.</summary>
    public double MinHeight { get; set; } = 3d;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var fraction = value switch
        {
            double number => number,
            decimal number => (double)number,
            _ => 0d
        };

        return Math.Max(MinHeight, Math.Clamp(fraction, 0d, 1d) * MaxHeight);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("A bar's height is never read back into a total.");
}
