using NSubstitute;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Nutrition;
using Trackr.Mobile.Core.Storage;
using Trackr.Mobile.Core.Sync;
using Trackr.Mobile.Core.ViewModels;
using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Mobile.Tests;

/// <summary>
/// Milestone 14's queue: a meal somebody approved is never lost to a network.
/// </summary>
/// <remarks>
/// These run against the real <see cref="LogOutbox"/> over a real in-memory SQLite database, so
/// the SQL, the JSON round trip and the ordering are all under test rather than mocked away. Only
/// the API and the phone's connectivity are substituted, which are the two things a test host does
/// not have.
/// </remarks>
public sealed class OfflineQueueTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_meal_confirmed_with_no_network_is_queued_rather_than_lost()
    {
        var api = Substitute.For<ITrackrApiClient>();
        var network = new FakeNetwork();
        var outbox = LocalStore.Outbox();
        var sync = Queues.Sync(api, network, outbox, new FixedClock(Noon));

        Queues.Unreachable(api);

        network.GoOffline();

        var result = await sync.SubmitAsync(Meal("Porridge"), "Porridge");

        Assert.Equal(SubmitOutcome.Queued, result.Outcome);

        // Attempted first, even with the platform saying there is no network. That hint is wrong in
        // both directions - the emulator reports no network in airplane mode while the host server
        // is still reachable - and queuing a meal that would have gone straight through is the more
        // expensive mistake.
        await api.Received(1).SaveLogEntryAsync(
            Arg.Any<SaveLogEntryRequest>(),
            Arg.Any<CancellationToken>());

        var queued = Assert.Single(await outbox.ReadAsync(Queues.UserId));

        Assert.Equal("Porridge", queued.Summary);
        Assert.False(queued.Rejected);
    }

    [Fact]
    public async Task The_queue_empties_itself_when_the_network_comes_back()
    {
        var api = Substitute.For<ITrackrApiClient>();
        var network = new FakeNetwork();
        var outbox = LocalStore.Outbox();
        var sync = Queues.Sync(api, network, outbox, new FixedClock(Noon));

        api.SaveLogEntryAsync(Arg.Any<SaveLogEntryRequest>(), Arg.Any<CancellationToken>())
            .Returns(SaveLogResult.Deferred("Could not reach the server."));

        network.GoOffline();

        await sync.SubmitAsync(Meal("Porridge"), "Porridge");

        // The attempt SubmitAsync makes before queuing is not what this test is about.
        api.ClearReceivedCalls();

        api.SaveLogEntryAsync(Arg.Any<SaveLogEntryRequest>(), Arg.Any<CancellationToken>())
            .Returns(SaveLogResult.Ok(Entry()));

        // The platform announcing a network is what starts this. The flush that follows is not
        // awaited by anything in the app either, so the test waits the way Home does - by asking
        // again, which joins one in progress rather than starting a second.
        network.ComeBack();

        await sync.FlushAsync();

        Assert.Empty(await outbox.ReadAsync(Queues.UserId));

        await api.Received(1).SaveLogEntryAsync(
            Arg.Any<SaveLogEntryRequest>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The id is what makes an unwatched retry safe, so it has to be the same one every time.
    /// </summary>
    /// <remarks>
    /// The failure this guards is not the one where nothing arrives - it is the one where the write
    /// succeeded and the reply was lost, which from the phone looks identical. A fresh id per
    /// attempt would turn that into a second dinner.
    /// </remarks>
    [Fact]
    public async Task A_retry_carries_the_id_the_meal_was_queued_with()
    {
        var api = Substitute.For<ITrackrApiClient>();
        var network = new FakeNetwork();
        var outbox = LocalStore.Outbox();
        var sync = Queues.Sync(api, network, outbox, new FixedClock(Noon));

        var meal = Meal("Porridge");

        Queues.Unreachable(api);

        network.GoOffline();

        await sync.SubmitAsync(meal, "Porridge");

        // The attempt SubmitAsync makes before queuing is not what this test is about.
        api.ClearReceivedCalls();

        api.SaveLogEntryAsync(Arg.Any<SaveLogEntryRequest>(), Arg.Any<CancellationToken>())
            .Returns(SaveLogResult.Deferred("Still nothing."));

        network.ComeBackUnnoticed();

        await sync.FlushAsync();
        await sync.FlushAsync();

        await api.Received(2).SaveLogEntryAsync(
            Arg.Is<SaveLogEntryRequest>(request => request.ClientRequestId == meal.ClientRequestId),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// While anything is queued, everything queues.
    /// </summary>
    /// <remarks>
    /// A save that jumped the queue would store tonight's dinner while this morning's breakfast was
    /// still waiting, which makes "did that go through?" impossible to answer by looking at the log.
    /// </remarks>
    [Fact]
    public async Task Nothing_jumps_the_queue()
    {
        var api = Substitute.For<ITrackrApiClient>();
        var network = new FakeNetwork();
        var outbox = LocalStore.Outbox();
        var sync = Queues.Sync(api, network, outbox, new FixedClock(Noon));

        Queues.Unreachable(api);

        network.GoOffline();
        await sync.SubmitAsync(Meal("Porridge"), "Porridge");

        network.ComeBackUnnoticed();

        api.ClearReceivedCalls();
        api.SaveLogEntryAsync(Arg.Any<SaveLogEntryRequest>(), Arg.Any<CancellationToken>())
            .Returns(SaveLogResult.Deferred("Could not reach the server."));

        var result = await sync.SubmitAsync(Meal("Toast"), "Toast");

        Assert.Equal(SubmitOutcome.Queued, result.Outcome);

        var queued = await outbox.ReadAsync(Queues.UserId);

        Assert.Equal(["Porridge", "Toast"], queued.Select(meal => meal.Summary));
    }

    /// <summary>
    /// A refusal is kept and shown, never retried in a loop and never quietly dropped.
    /// </summary>
    /// <remarks>
    /// Both halves matter. Retrying a 400 forever is how a queue becomes a loop; discarding it is
    /// how an app loses food somebody logged. So it stays, marked, until a person decides.
    /// </remarks>
    [Fact]
    public async Task A_meal_the_server_refuses_stops_retrying_but_is_not_thrown_away()
    {
        var api = Substitute.For<ITrackrApiClient>();
        var network = new FakeNetwork();
        var outbox = LocalStore.Outbox();
        var sync = Queues.Sync(api, network, outbox, new FixedClock(Noon));

        Queues.Unreachable(api);

        network.GoOffline();
        await sync.SubmitAsync(Meal("Porridge"), "Porridge");
        network.ComeBackUnnoticed();

        api.SaveLogEntryAsync(Arg.Any<SaveLogEntryRequest>(), Arg.Any<CancellationToken>())
            .Returns(SaveLogResult.Rejected("That photo belongs to another entry."));

        await sync.FlushAsync();

        var stuck = Assert.Single(await outbox.ReadAsync(Queues.UserId));

        Assert.True(stuck.Rejected);
        Assert.Contains("another entry", stuck.LastError);

        api.ClearReceivedCalls();

        await sync.FlushAsync();

        await api.DidNotReceive().SaveLogEntryAsync(
            Arg.Any<SaveLogEntryRequest>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Sending again is the user's decision, and it works.</summary>
    [Fact]
    public async Task A_stuck_meal_can_be_sent_again_by_hand()
    {
        var api = Substitute.For<ITrackrApiClient>();
        var network = new FakeNetwork();
        var outbox = LocalStore.Outbox();
        var sync = Queues.Sync(api, network, outbox, new FixedClock(Noon));

        Queues.Unreachable(api);

        network.GoOffline();
        await sync.SubmitAsync(Meal("Porridge"), "Porridge");
        network.ComeBackUnnoticed();

        api.SaveLogEntryAsync(Arg.Any<SaveLogEntryRequest>(), Arg.Any<CancellationToken>())
            .Returns(SaveLogResult.Rejected("The server was full."));

        await sync.FlushAsync();

        var stuck = Assert.Single(await outbox.ReadAsync(Queues.UserId));

        api.SaveLogEntryAsync(Arg.Any<SaveLogEntryRequest>(), Arg.Any<CancellationToken>())
            .Returns(SaveLogResult.Ok(Entry()));

        await sync.RetryAsync(stuck.Id);

        Assert.Empty(await outbox.ReadAsync(Queues.UserId));
    }

    /// <summary>Discarding is the only way a confirmed meal ever leaves unsent.</summary>
    [Fact]
    public async Task Discarding_removes_it_and_nothing_else_does()
    {
        var api = Substitute.For<ITrackrApiClient>();
        var network = new FakeNetwork();
        var outbox = LocalStore.Outbox();
        var sync = Queues.Sync(api, network, outbox, new FixedClock(Noon));

        Queues.Unreachable(api);

        network.GoOffline();
        await sync.SubmitAsync(Meal("Porridge"), "Porridge");

        var queued = Assert.Single(await outbox.ReadAsync(Queues.UserId));

        await sync.DiscardAsync(queued.Id);

        Assert.Empty(await outbox.ReadAsync(Queues.UserId));
    }

    /// <summary>
    /// A failure that is not about the row stops the flush rather than working through the queue.
    /// </summary>
    /// <remarks>
    /// Carrying on would reorder the log for no gain - whatever stopped the first will stop the
    /// second - and would spend a phone's radio on a queue that is going nowhere.
    /// </remarks>
    [Fact]
    public async Task One_unreachable_row_stops_the_flush()
    {
        var api = Substitute.For<ITrackrApiClient>();
        var network = new FakeNetwork();
        var outbox = LocalStore.Outbox();
        var sync = Queues.Sync(api, network, outbox, new FixedClock(Noon));

        Queues.Unreachable(api);

        network.GoOffline();
        await sync.SubmitAsync(Meal("Porridge"), "Porridge");
        await sync.SubmitAsync(Meal("Toast"), "Toast");
        network.ComeBackUnnoticed();

        // The attempt SubmitAsync makes before queuing is not what this test is about.
        api.ClearReceivedCalls();

        api.SaveLogEntryAsync(Arg.Any<SaveLogEntryRequest>(), Arg.Any<CancellationToken>())
            .Returns(SaveLogResult.Deferred("Could not reach the server."));

        await sync.FlushAsync();

        await api.Received(1).SaveLogEntryAsync(
            Arg.Any<SaveLogEntryRequest>(),
            Arg.Any<CancellationToken>());

        Assert.Equal(2, (await outbox.ReadAsync(Queues.UserId)).Count);
    }

    /// <summary>
    /// A meal that has waited a month stops trying, visibly.
    /// </summary>
    /// <remarks>
    /// Giving up quietly would be the failure this whole milestone is against; giving up never
    /// would leave a row for a deleted account retrying until the phone is replaced. So it stops,
    /// says why, and waits for a person.
    /// </remarks>
    [Fact]
    public async Task A_meal_that_has_waited_a_month_stops_trying_and_says_so()
    {
        var api = Substitute.For<ITrackrApiClient>();
        var network = new FakeNetwork();
        var outbox = LocalStore.Outbox();

        Queues.Unreachable(api);

        network.GoOffline();

        await Queues.Sync(api, network, outbox, new FixedClock(Noon))
            .SubmitAsync(Meal("Porridge"), "Porridge");

        // The attempt SubmitAsync makes before queuing is not what this test is about.
        api.ClearReceivedCalls();

        network.ComeBackUnnoticed();

        var later = Queues.Sync(
            api,
            network,
            outbox,
            new FixedClock(Noon + OutboxSync.MaxAge + TimeSpan.FromDays(1)));

        await later.FlushAsync();

        var stuck = Assert.Single(await outbox.ReadAsync(Queues.UserId));

        Assert.True(stuck.Rejected);
        Assert.Contains("30 days", stuck.LastError);

        // Never sent, rather than sent and then abandoned.
        await api.DidNotReceive().SaveLogEntryAsync(
            Arg.Any<SaveLogEntryRequest>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// One account's unsent meals are never another's.
    /// </summary>
    /// <remarks>
    /// Rows survive a sign-out - losing them would make signing out a way to lose logged food - so
    /// the phone can hold rows for an account that is not the one holding it. What somebody ate is
    /// not something to show the next person.
    /// </remarks>
    [Fact]
    public async Task Queued_meals_are_never_read_back_for_another_account()
    {
        var outbox = LocalStore.Outbox();
        var somebodyElse = Guid.Parse("0198c0de-0000-7000-8000-0000000000ff");

        Assert.True(await outbox.EnqueueAsync(Queues.UserId, Meal("Porridge"), "Porridge", Noon));

        Assert.Empty(await outbox.ReadAsync(somebodyElse));
        Assert.Single(await outbox.ReadAsync(Queues.UserId));
    }

    /// <summary>Queuing the same meal twice is one row, and the attempt count survives.</summary>
    [Fact]
    public async Task Queuing_the_same_meal_twice_stores_it_once()
    {
        var outbox = LocalStore.Outbox();
        var meal = Meal("Porridge");

        await outbox.EnqueueAsync(Queues.UserId, meal, "Porridge", Noon);
        await outbox.RecordAttemptAsync(meal.ClientRequestId!.Value, "Nope", rejected: false, Noon);
        await outbox.EnqueueAsync(Queues.UserId, meal, "Porridge", Noon);

        var queued = Assert.Single(await outbox.ReadAsync(Queues.UserId));

        Assert.Equal(1, queued.Attempts);
    }

    /// <summary>What was queued is what is sent, down to the nutrient map.</summary>
    [Fact]
    public async Task A_queued_meal_is_sent_exactly_as_it_was_confirmed()
    {
        var api = Substitute.For<ITrackrApiClient>();
        var network = new FakeNetwork();
        var outbox = LocalStore.Outbox();
        var sync = Queues.Sync(api, network, outbox, new FixedClock(Noon));

        var meal = Meal("Porridge");
        meal.LoggedUtc = Noon - TimeSpan.FromHours(5);
        meal.Note = "before the train";
        meal.Items[0].Nutrients["sodium"] = 62m;

        Queues.Unreachable(api);

        network.GoOffline();
        await sync.SubmitAsync(meal, "Porridge");
        network.ComeBackUnnoticed();

        // The attempt SubmitAsync makes before queuing is not what this test is about.
        api.ClearReceivedCalls();

        api.SaveLogEntryAsync(Arg.Any<SaveLogEntryRequest>(), Arg.Any<CancellationToken>())
            .Returns(SaveLogResult.Ok(Entry()));

        await sync.FlushAsync();

        await api.Received(1).SaveLogEntryAsync(
            Arg.Is<SaveLogEntryRequest>(request =>
                request.Note == "before the train"
                && request.LoggedUtc == Noon - TimeSpan.FromHours(5)
                && request.Items.Single().Nutrients["sodium"] == 62m
                && request.Items.Single().EnergyKcal == 150m),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Home says what is waiting, and says the total does not include it.
    /// </summary>
    /// <remarks>
    /// The banner is on Home rather than in the chat because Home is where the number it
    /// contradicts is, and because the card that queued the meal lives in a transcript that does
    /// not survive a restart.
    /// </remarks>
    [Fact]
    public async Task Home_says_what_is_waiting_and_that_the_total_excludes_it()
    {
        var api = Substitute.For<ITrackrApiClient>();
        var network = new FakeNetwork();
        var outbox = LocalStore.Outbox();
        var sync = Queues.Sync(api, network, outbox, new FixedClock(Noon));

        Queues.Unreachable(api);

        network.GoOffline();

        await sync.SubmitAsync(Meal("Porridge"), "Porridge");
        await sync.SubmitAsync(Meal("Toast"), "Toast and 1 more");

        var home = new HomeViewModel(
            api,
            new NutrientCatalogCache(api),
            Substitute.For<INavigationService>(),
            new DisplayUnits(Queues.SignedIn()),
            sync);

        await home.RefreshCommand.ExecuteAsync(null);

        Assert.True(home.HasQueued);
        Assert.Equal(2, home.Queued.Count);
        Assert.Contains("not in today's total", home.QueuedSummary);
        Assert.False(home.HasStuck);
    }

    private static SaveLogEntryRequest Meal(string name) =>
        new()
        {
            ClientRequestId = Guid.CreateVersion7(),
            LoggedUtc = Noon,
            Items =
            [
                new SaveLogItemRequest
                {
                    Name = name,
                    Quantity = 1m,
                    EnergyKcal = 150m,
                    FatG = 3m,
                    CarbohydrateG = 27m,
                    ProteinG = 5m
                }
            ]
        };

    private static LogEntryResponse Entry() =>
        new(Guid.CreateVersion7(), Noon, null, [], [], Noon, Noon);
}
