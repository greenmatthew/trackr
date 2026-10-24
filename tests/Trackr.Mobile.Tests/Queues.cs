using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Auth;
using Trackr.Mobile.Core.Platform;
using Trackr.Mobile.Core.Storage;
using Trackr.Mobile.Core.Sync;
using Trackr.Shared.Auth;
using Trackr.Shared.Nutrition;

namespace Trackr.Mobile.Tests;

/// <summary>
/// The offline queue, assembled for a test.
/// </summary>
/// <remarks>
/// The real <see cref="OutboxSync"/> over a real in-memory <see cref="LogOutbox"/>, because the one
/// decision worth testing - send it now or store it for later - is the thing a substitute would
/// stub out.
/// </remarks>
internal static class Queues
{
    /// <summary>The account queued meals are filed under, in every test that does not care which.</summary>
    public static readonly Guid UserId = Guid.Parse("0198c0de-0000-7000-8000-00000000000a");

    public static OutboxSync Sync(
        ITrackrApiClient api,
        INetworkStatus? network = null,
        LogOutbox? outbox = null,
        TimeProvider? time = null) =>
        new(
            api,
            outbox ?? LocalStore.Outbox(),
            SignedIn(),
            network ?? new FakeNetwork(),
            time ?? TimeProvider.System,
            NullLogger<OutboxSync>.Instance);

    /// <summary>
    /// A server that cannot be reached: every save comes back unsent, and worth sending again.
    /// </summary>
    /// <remarks>
    /// Stubbed even in the tests that then take the phone offline, because the queue attempts the
    /// save regardless of what the platform says about connectivity - see
    /// <see cref="INetworkStatus"/> for why it does not take that hint's word for anything.
    /// </remarks>
    public static void Unreachable(ITrackrApiClient api) =>
        api.SaveLogEntryAsync(Arg.Any<SaveLogEntryRequest>(), Arg.Any<CancellationToken>())
            .Returns(SaveLogResult.Deferred("Could not reach the server."));

    /// <summary>
    /// An account that is simply signed in.
    /// </summary>
    /// <remarks>
    /// A stub rather than a real <see cref="AuthSession"/>, which only holds a user after a round
    /// trip most of these tests have no reason to make.
    /// </remarks>
    public static ICurrentAccount SignedIn()
    {
        var account = Substitute.For<ICurrentAccount>();

        account.CurrentUser.Returns(new MeResponse(
            UserId,
            "owner@example.test",
            TwoFactorEnabled: false,
            AvatarUpdatedUtc: null,
            TimeZoneId: null,
            DisplayName: null,
            EnergyUnit: EnergyUnit.Kilocalories,
            MeasurementSystem: MeasurementSystem.Metric));

        return account;
    }
}
