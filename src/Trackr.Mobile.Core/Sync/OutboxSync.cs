using Microsoft.Extensions.Logging;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Auth;
using Trackr.Mobile.Core.Platform;
using Trackr.Mobile.Core.Storage;
using Trackr.Shared.Nutrition;

namespace Trackr.Mobile.Core.Sync;

/// <summary>What became of a meal the user confirmed.</summary>
public enum SubmitOutcome
{
    /// <summary>The server has it.</summary>
    Saved,

    /// <summary>Stored on the phone, and it will be sent when the server can be reached.</summary>
    Queued,

    /// <summary>The server refused it. Nothing is queued and the user has to change something.</summary>
    Rejected,

    /// <summary>
    /// Neither sent nor queued: the local database would not take it. The meal is still on screen
    /// and still unsaved, which is the only honest thing to report.
    /// </summary>
    Lost,
}

/// <param name="Problem">What to tell the user. Null when <see cref="SubmitOutcome.Saved"/>.</param>
public sealed record SubmitResult(SubmitOutcome Outcome, string? Problem = null);

/// <summary>
/// The offline queue: where a confirmed meal goes when the server cannot be reached, and what
/// empties it again.
/// </summary>
/// <remarks>
/// Milestone 14. Everything upstream of this is unchanged - the cascade still runs on the server
/// and a card is still the only thing that turns an analysis into a saved entry - so this deals
/// only with the last step, the <c>POST /api/log</c> that a phone in a lift cannot make.
/// <para>
/// <b>Only a confirmed meal is ever queued.</b> There is no queue of things to analyse later, and
/// that is deliberate: the numbers in a queued row are ones a person read and approved, so sending
/// them an hour later stores exactly what they agreed to. Queuing an <em>un</em>analysed meal would
/// mean either saving figures nobody had seen or raising a card hours after the food was eaten,
/// and confirm-before-save (CLAUDE.md section 2) rules out the first.
/// </para>
/// <para>
/// <b>Once anything is queued, everything queues.</b> A save that jumped the queue would store
/// tonight's dinner while this morning's breakfast was still waiting, which is a strange thing for
/// a log to do and makes any later "did that go through?" impossible to answer by looking.
/// </para>
/// <para>
/// Retries are safe because each row carries a <see cref="SaveLogEntryRequest.ClientRequestId"/>
/// the server de-duplicates on. The dangerous failure is not the one where nothing arrives - it is
/// the one where the write succeeded and the reply was lost, which from here looks identical.
/// </para>
/// </remarks>
public sealed class OutboxSync
{
    /// <summary>
    /// How long a meal may sit unsent before the queue gives up on sending it by itself.
    /// </summary>
    /// <remarks>
    /// Long enough that a phone away from its server for a holiday still delivers, and short enough
    /// that a row for an account that no longer exists does not retry forever. Giving up is
    /// <em>visible</em>: the row stays, marked as needing attention, because the one thing the queue
    /// may never do is quietly drop food somebody logged.
    /// </remarks>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    private readonly ITrackrApiClient api;
    private readonly LogOutbox outbox;
    private readonly ICurrentAccount account;
    private readonly INetworkStatus network;
    private readonly TimeProvider time;
    private readonly ILogger<OutboxSync> logger;

    private readonly Lock gate = new();

    /// <summary>The flush currently running, if one is. Joined rather than duplicated.</summary>
    private Task<FlushSummary>? inFlight;

    public OutboxSync(
        ITrackrApiClient api,
        LogOutbox outbox,
        ICurrentAccount account,
        INetworkStatus network,
        TimeProvider time,
        ILogger<OutboxSync> logger)
    {
        this.api = api;
        this.outbox = outbox;
        this.account = account;
        this.network = network;
        this.time = time;
        this.logger = logger;

        // The whole reason the platform hint is worth having: a queue nobody has to remember.
        network.Changed += OnNetworkChanged;
    }

    /// <summary>Meals waiting or stuck, oldest first. Empty when the account is not signed in.</summary>
    public async Task<IReadOnlyList<QueuedMeal>> ReadAsync(CancellationToken cancellationToken = default) =>
        account.CurrentUser is { } user
            ? await outbox.ReadAsync(user.UserId, cancellationToken)
            : [];

    /// <summary>
    /// Stores a confirmed meal: on the server if it can, on the phone if it cannot.
    /// </summary>
    /// <remarks>
    /// The request must already carry its client request id and the time it was approved. Both are
    /// set where the card is confirmed rather than here, so that a meal keeps the moment it was
    /// eaten however long it then waits.
    /// </remarks>
    public async Task<SubmitResult> SubmitAsync(
        SaveLogEntryRequest request,
        string summary,
        CancellationToken cancellationToken = default)
    {
        if (account.CurrentUser is not { } user)
        {
            // Nothing to file it under. Reachable only by signing out with a card open.
            return new SubmitResult(SubmitOutcome.Rejected, "You are no longer signed in.");
        }

        // Behind the queue rather than in front of it, whenever the queue is not empty - see the
        // class remarks.
        //
        // Deliberately not "and the platform says there is a network". INetworkStatus is a hint and
        // is wrong in both directions: a captive-portal wifi reports a connection that answers
        // nothing, and - as the emulator demonstrated - airplane mode reports none while the server
        // is still perfectly reachable. Skipping the attempt on its word queues meals that would
        // have gone straight through. The attempt costs one fast failure when it is right.
        var queued = await outbox.ReadAsync(user.UserId, cancellationToken);

        if (!queued.Any(meal => !meal.Rejected))
        {
            var result = await api.SaveLogEntryAsync(request, cancellationToken);

            switch (result.Status)
            {
                case SaveLogStatus.Saved:
                    return new SubmitResult(SubmitOutcome.Saved);

                case SaveLogStatus.Rejected:
                    return new SubmitResult(SubmitOutcome.Rejected, result.Problem);
            }
        }

        var stored = await outbox.EnqueueAsync(
            user.UserId,
            request,
            summary,
            time.GetUtcNow(),
            cancellationToken);

        if (!stored)
        {
            return new SubmitResult(
                SubmitOutcome.Lost,
                "Could not reach the server, and this phone could not store the meal either. "
                + "Nothing was saved - try again in a moment.");
        }

        // Deliberately no retry from here. The card has its answer, and a second attempt made
        // immediately after the first one failed is the least likely of all of them to work; what
        // does work is a change in circumstances, which is exactly what the triggers on FlushAsync
        // are - coming back to the app, the network returning, or opening Home.
        return new SubmitResult(
            SubmitOutcome.Queued,
            "Saved on this phone. It will be sent when the server can be reached.");
    }

    /// <summary>
    /// Tries to deliver everything queued, oldest first.
    /// </summary>
    /// <remarks>
    /// Stops at the first row that could not be delivered. Carrying on would reorder the log for no
    /// gain - a failure that is not about the row itself will be about the next one too - and would
    /// spend a phone's radio on a queue that is going nowhere.
    /// <para>
    /// One flush at a time, and a second caller <em>joins</em> the first rather than being turned
    /// away. A resume and a connectivity change routinely land together, and two flushes racing
    /// would send the same row twice; the server would de-duplicate it, but relying on that for
    /// something this easy to prevent would be putting the seatbelt to work as a chair. Joining
    /// rather than returning early also means Home's refresh reads its totals <em>after</em>
    /// whatever was already being delivered, instead of just before.
    /// </para>
    /// </remarks>
    public Task<FlushSummary> FlushAsync(CancellationToken cancellationToken = default)
    {
        if (account.CurrentUser is null)
        {
            return Task.FromResult(FlushSummary.Empty);
        }

        lock (gate)
        {
            if (inFlight is { IsCompleted: false })
            {
                return inFlight;
            }

            return inFlight = RunFlushAsync(cancellationToken);
        }
    }

    private async Task<FlushSummary> RunFlushAsync(CancellationToken cancellationToken)
    {
        if (account.CurrentUser is not { } user)
        {
            return FlushSummary.Empty;
        }

        var queued = await outbox.ReadAsync(user.UserId, cancellationToken);

        var sent = 0;
        var failed = 0;

        foreach (var meal in queued.Where(meal => !meal.Rejected))
        {
            var now = time.GetUtcNow();

            if (now - meal.QueuedUtc > MaxAge)
            {
                await outbox.RecordAttemptAsync(
                    meal.Id,
                    $"This has been waiting {MaxAge.Days} days and has not been sent. "
                    + "Check the server address, then send it again or discard it.",
                    rejected: true,
                    now,
                    cancellationToken);

                failed++;

                continue;
            }

            var result = await api.SaveLogEntryAsync(meal.Request, cancellationToken);

            if (result.Status is SaveLogStatus.Saved)
            {
                await outbox.RemoveAsync(meal.Id, cancellationToken);

                sent++;

                continue;
            }

            await outbox.RecordAttemptAsync(
                meal.Id,
                result.Problem,
                rejected: result.Status is SaveLogStatus.Rejected,
                now,
                cancellationToken);

            failed++;

            if (result.Status is SaveLogStatus.Deferred)
            {
                // Not this row's fault, so nothing behind it will fare better.
                break;
            }
        }

        return new FlushSummary(sent, failed);
    }

    /// <summary>
    /// Sends one stuck meal again, at the user's request.
    /// </summary>
    /// <remarks>
    /// A rejected row is never retried on its own - that is what stops a queue becoming a loop -
    /// but the reason may well have been fixed by now: a server that was full, an account that was
    /// signed out. Clearing the flag and flushing is the whole of it.
    /// </remarks>
    public async Task RetryAsync(Guid clientRequestId, CancellationToken cancellationToken = default)
    {
        await outbox.RecordAttemptAsync(
            clientRequestId,
            problem: null,
            rejected: false,
            time.GetUtcNow(),
            cancellationToken);

        await FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Throws a queued meal away, at the user's request. The only way a row leaves unsent.
    /// </summary>
    public async Task DiscardAsync(Guid clientRequestId, CancellationToken cancellationToken = default)
    {
        if (await outbox.RemoveAsync(clientRequestId, cancellationToken))
        {
            logger.LogInformation("A queued meal was discarded by the user");
        }
    }

    private void OnNetworkChanged()
    {
        if (network.IsConnected)
        {
            _ = FlushInBackgroundAsync();
        }
    }

    /// <summary>
    /// A flush nobody is awaiting, so a failure has to end here rather than on a thread pool
    /// thread with nothing above it.
    /// </summary>
    private async Task FlushInBackgroundAsync()
    {
        try
        {
            await FlushAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Emptying the offline queue failed");
        }
    }
}

/// <param name="Sent">Meals the server took this time round.</param>
/// <param name="Failed">Rows still queued, including any newly marked as needing attention.</param>
public sealed record FlushSummary(int Sent, int Failed)
{
    public static FlushSummary Empty { get; } = new(0, 0);
}
