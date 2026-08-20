namespace Trackr.Mobile.Core.ViewModels;

/// <summary>
/// What to draw in the avatar circle when the account has no picture set.
/// </summary>
/// <remarks>
/// In Core, and separate from the view models that show it, because it is the one part of the
/// avatar that is pure logic and therefore worth testing without a device. Everything else -
/// the circle, the tap target, the picker - is XAML and platform glue.
/// </remarks>
public static class Avatar
{
    /// <summary>
    /// Derives up to two initials from a display name, or from an email address.
    /// </summary>
    /// <remarks>
    /// One method for both, because they separate the same way: "Ada Lovelace" and
    /// "ada.lovelace@example.test" both give "AL", and "ada@example.test" gives "A". Anything after
    /// an @ is dropped, which costs a display name nothing and is what makes an address readable.
    /// A value that is all digits or all punctuation has no useful initials, and a bare circle is
    /// better than a misleading letter.
    /// <para>
    /// First and <i>last</i> segment rather than the first two, so "ada.byron.lovelace" gives
    /// "AL" and not "AB" - a middle name is not what anyone expects to see in a monogram.
    /// </para>
    /// </remarks>
    public static string InitialsFrom(string? nameOrEmail)
    {
        if (string.IsNullOrWhiteSpace(nameOrEmail))
        {
            return "";
        }

        var localPart = nameOrEmail.Trim().Split('@')[0];

        var letters = localPart
            .Split([' ', '.', '_', '-', '+'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => word.FirstOrDefault(char.IsLetter))
            .Where(letter => letter != default)
            .ToArray();

        return letters.Length switch
        {
            0 => "",
            1 => new string([letters[0]]).ToUpperInvariant(),
            _ => new string([letters[0], letters[^1]]).ToUpperInvariant(),
        };
    }
}
