using System.Globalization;

namespace Trackr.Mobile.Core.Nutrition;

/// <summary>
/// Reading a number a person typed.
/// </summary>
/// <remarks>
/// Every editable figure in this app is bound as <em>text</em> and parsed here - the confirmation
/// card's macros, a daily target, a weight. Binding a decimal directly would turn a half-typed or
/// unreadable entry into zero, and a silent zero in a nutrient field is the exact failure CLAUDE.md
/// section 2 puts first. An unreadable value blocks the save instead.
/// <para>
/// The current culture first and the invariant one second, because a keyboard may offer either
/// separator regardless of the phone's locale, and "1,5" from somebody in Berlin and "1.5" from the
/// same keyboard both mean one and a half.
/// </para>
/// </remarks>
public static class Numbers
{
    private const NumberStyles Styles =
        NumberStyles.AllowDecimalPoint
        | NumberStyles.AllowThousands
        | NumberStyles.AllowLeadingWhite
        | NumberStyles.AllowTrailingWhite;

    /// <summary>Whether the text reads as a number that is not negative.</summary>
    public static bool TryRead(string? text, out decimal value)
    {
        value = 0m;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!decimal.TryParse(text, Styles, CultureInfo.CurrentCulture, out value)
            && !decimal.TryParse(text, Styles, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        // No leading sign is allowed above, so this catches nothing a keyboard can produce - it is
        // here for a value arriving from anywhere else, since a negative gram would pass every
        // other check and then be summed into a day's total.
        return value >= 0m;
    }

    /// <summary>The value, or zero when it does not read. Only for callers that checked first.</summary>
    public static decimal Read(string? text) => TryRead(text, out var value) ? value : 0m;

    /// <summary>An optional figure: null for blank, and false when it is there but unreadable.</summary>
    /// <remarks>
    /// The distinction a body measurement needs. Blank means "I am not telling you my weight",
    /// which is a complete answer; "72kg" with the unit typed in is a mistake to point out rather
    /// than a field to clear.
    /// </remarks>
    public static bool TryReadOptional(string? text, out decimal? value)
    {
        value = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!TryRead(text, out var parsed))
        {
            return false;
        }

        value = parsed;

        return true;
    }
}
