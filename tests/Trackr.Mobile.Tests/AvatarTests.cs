using Trackr.Mobile.Core.ViewModels;

namespace Trackr.Mobile.Tests;

public class AvatarTests
{
    [Theory]
    // One name gives one letter rather than padding it out with the domain, which is not
    // part of anyone's name.
    [InlineData("ada@example.test", "A")]
    [InlineData("owner@example.test", "O")]
    // Separators that actually turn up in local parts.
    [InlineData("ada.lovelace@example.test", "AL")]
    [InlineData("ada_lovelace@example.test", "AL")]
    [InlineData("ada-lovelace@example.test", "AL")]
    // Plus-addressing: the tag is not a surname, but taking it is better than dropping the
    // second letter entirely, and it is at least stable for a given address.
    [InlineData("ada+trackr@example.test", "AT")]
    // Never more than fit in the circle.
    [InlineData("ada.byron.lovelace@example.test", "AL")]
    [InlineData("ADA.LOVELACE@example.test", "AL")]
    [InlineData("  ada.lovelace@example.test  ", "AL")]
    // Leading punctuation or digits in a segment: take the first actual letter.
    [InlineData("1ada.2lovelace@example.test", "AL")]
    public void Initials_come_from_the_local_part(string email, string expected) =>
        Assert.Equal(expected, Avatar.InitialsFrom(email));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    // Nothing letter-shaped to work with. An empty circle beats a misleading letter.
    [InlineData("12345@example.test")]
    [InlineData("...@example.test")]
    public void Initials_are_empty_when_there_is_no_name_to_take_them_from(string? email) =>
        Assert.Equal("", Avatar.InitialsFrom(email));
}
