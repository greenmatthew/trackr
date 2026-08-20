using NSubstitute;
using Trackr.Mobile.Core.Auth;
using Trackr.Mobile.Core.Nutrition;
using Trackr.Shared.Auth;
using Trackr.Shared.Nutrition;

namespace Trackr.Mobile.Tests;

/// <summary>
/// A <see cref="DisplayUnits"/> over an account with the preferences a test cares about.
/// </summary>
/// <remarks>
/// This is what <see cref="ICurrentAccount"/> exists for: the unit preferences are a property of
/// the signed-in account, and standing up a whole <c>AuthSession</c> - token store, cache, a fake
/// server answering <c>/me</c> - to say "this account reads kilojoules" would be several times the
/// size of the test that needed it.
/// </remarks>
internal static class Units
{
    /// <summary>The defaults, which are the units the API itself uses.</summary>
    public static DisplayUnits Metric { get; } = For();

    public static DisplayUnits For(
        EnergyUnit energy = EnergyUnit.Kilocalories,
        MeasurementSystem measurements = MeasurementSystem.Metric)
    {
        var account = Substitute.For<ICurrentAccount>();

        account.CurrentUser.Returns(new MeResponse(
            Guid.NewGuid(),
            "account@example.test",
            TwoFactorEnabled: false,
            AvatarUpdatedUtc: null,
            TimeZoneId: null,
            DisplayName: null,
            EnergyUnit: energy,
            MeasurementSystem: measurements));

        return new DisplayUnits(account);
    }
}
