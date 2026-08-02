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
    /// Derives up to two initials from an email address.
    /// </summary>
    /// <remarks>
    /// The local part is the only name the app has until milestone 9.13 adds a display name,
    /// so "ada.lovelace@example.test" gives "AL" and "ada@example.test" gives "A". Separators
    /// are the ones that actually appear in addresses; a local part that is all digits or all
    /// punctuation has no useful initials, and a bare circle is better than a misleading
    /// letter.
    /// <para>
    /// First and <i>last</i> segment rather than the first two, so "ada.byron.lovelace" gives
    /// "AL" and not "AB" - a middle name is not what anyone expects to see in a monogram.
    /// </para>
    /// </remarks>
    public static string InitialsFrom(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return "";
        }

        var localPart = email.Trim().Split('@')[0];

        var letters = localPart
            .Split(['.', '_', '-', '+'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
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
