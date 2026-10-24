using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Auth;
using Trackr.Mobile.Core.Nutrition;
using Trackr.Mobile.Core.Platform;
using Trackr.Mobile.Core.Storage;
using Trackr.Mobile.Core.Sync;
using Trackr.Mobile.Core.ViewModels.Chat;
using Trackr.Shared.Nutrition;

namespace Trackr.Mobile.Core.ViewModels;

/// <summary>
/// The input surface: say what you ate, attach a photo, approve the numbers.
/// </summary>
/// <remarks>
/// CLAUDE.md section 1 is the whole shape of this class. The user types plain language and
/// optionally attaches pictures; everything after that - the barcode, Open Food Facts, the model -
/// happens in one <c>POST /api/analyze</c> on the server, because a client that ran the stages
/// itself would be a second copy of the cascade that only changes when a new APK ships (section 10).
/// <para>
/// So this view model orchestrates exactly three calls and no logic about food: upload each photo,
/// analyse, and - only once the user has approved a card - save. It never writes to the log on its
/// own, which is the confirm-before-save rule of section 2 expressed as the absence of a code path.
/// </para>
/// </remarks>
public sealed partial class ChatViewModel : ObservableObject
{
    /// <summary>
    /// The longest edge a meal photo is uploaded at.
    /// </summary>
    /// <remarks>
    /// <c>MealImageRules</c> has no such limit on purpose - a meal photo is stored at full
    /// resolution so re-running a better model over it later is never foreclosed - and this does not
    /// contradict it so much as bound what "full" can mean here. MAUI's picker hands over a stream
    /// with no content type, so the phone re-encodes every image regardless, both to produce
    /// something the server's allow-list definitely accepts and to drop the EXIF block, which on a
    /// camera photo carries the coordinates the meal was eaten at.
    /// <para>
    /// Given it is re-encoding anyway, 2560 is chosen to be generous where it matters: milestone 7
    /// found real barcodes needing a second decode pass at 2x, and a label's small print is what the
    /// vision model reads the nutrition panel from. It is also several times what the server itself
    /// downsizes to before inference (<c>Trackr:Ollama:MaxImageEdgePixels</c>, 1280), so the stored
    /// copy stays better than anything the current model sees.
    /// </para>
    /// </remarks>
    private const int MaxPhotoEdgePixels = 2560;

    private readonly ITrackrApiClient _api;

    private readonly IPhotoPicker _photoPicker;

    private readonly IImageDownsizer _downsizer;

    private readonly NutrientCatalogCache _nutrients;

    private readonly AuthSession _session;

    private readonly TimeProvider _time;
    private readonly DisplayUnits _units;

    private readonly OutboxSync _outbox;

    private readonly RecentItemsCache _recentsCache;

    private CancellationTokenSource? inFlight;

    /// <summary>
    /// Which conversation is on screen. Incremented whenever the transcript is dropped.
    /// </summary>
    /// <remarks>
    /// An analysis takes minutes, and a sign-out during one would otherwise finish by appending the
    /// previous account's meal to the next account's empty chat. Cancelling is not enough on its
    /// own: the request may already have succeeded, and the reply arrives on a continuation that
    /// knows nothing about what happened while it waited.
    /// </remarks>
    private int conversation;

    public ChatViewModel(
        ITrackrApiClient api,
        IPhotoPicker photoPicker,
        IImageDownsizer downsizer,
        NutrientCatalogCache nutrients,
        AuthSession session,
        TimeProvider time,
        DisplayUnits units,
        OutboxSync outbox,
        RecentItemsCache recentsCache)
    {
        _api = api;
        _photoPicker = photoPicker;
        _downsizer = downsizer;
        _nutrients = nutrients;
        _session = session;
        _time = time;
        _units = units;
        _outbox = outbox;
        _recentsCache = recentsCache;

        // This view model outlives a visit to the tab, so it also outlives an account. A previous
        // user's meals, photographs and half-typed message sitting in memory is exactly the sort of
        // thing CLAUDE.md section 8 asks care about - AvatarStore drops its bytes here for the same
        // reason.
        _session.Changed += OnSessionChanged;
    }

    /// <summary>Everything said so far, oldest first. Nothing is ever removed.</summary>
    public ObservableCollection<ChatMessage> Messages { get; } = [];

    /// <summary>Photos chosen but not yet sent. Uploaded when the message is.</summary>
    /// <remarks>
    /// Uploaded on send rather than on pick, so backing out of a half-composed message leaves
    /// nothing on the server. The cost is that the wait starts at send; the analysis behind it is
    /// far longer than the upload, so it is not the part anybody notices.
    /// </remarks>
    public ObservableCollection<PendingPhoto> Attachments { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    public partial string Draft { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; set; }

    /// <summary>What the app is doing right now, while <see cref="IsBusy"/>.</summary>
    [ObservableProperty]
    public partial string? Status { get; set; }

    /// <summary>Something that went wrong before the conversation could take it. Null most of the time.</summary>
    [ObservableProperty]
    public partial string? Error { get; set; }

    public bool IsIdle => !IsBusy;

    public bool CanSend =>
        !IsBusy && (!string.IsNullOrWhiteSpace(Draft) || Attachments.Count > 0);

    /// <summary>
    /// The last thing sent, kept so a failure can be retried without re-uploading its photos.
    /// </summary>
    /// <remarks>
    /// CLAUDE.md section 5 requires a failed analysis to leave the user able to retry. Re-sending
    /// from the transcript rather than re-composing means the photos already on the server are
    /// reused - the same ids, so no second copy, and no cost to trying again.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    public partial SentMeal? LastAttempt { get; set; }

    public bool CanRetry => !IsBusy && LastAttempt is not null;

    /// <summary>
    /// Whether the `+` button has opened its two choices.
    /// </summary>
    /// <remarks>
    /// A flag here rather than an action sheet raised from the page, so that "camera or gallery"
    /// stays a decision the view model exposes and a test can drive. The alternative would put a
    /// platform dialog between the button and the two commands below, which is exactly the kind of
    /// logic CLAUDE.md section 10 keeps out of the MAUI project.
    /// </remarks>
    [ObservableProperty]
    public partial bool IsAttachMenuOpen { get; set; }

    /// <summary>
    /// Foods this account confirmed before, offered so the same meal need not be analysed twice.
    /// </summary>
    /// <remarks>
    /// Read from the log rather than from the catalog: what people eat again is mostly home
    /// cooking, which never carries a barcode and so never becomes a catalog item.
    /// </remarks>
    public ObservableCollection<RecentItem> Recents { get; } = [];

    [ObservableProperty]
    public partial bool IsRecentsOpen { get; set; }

    /// <summary>
    /// Why the list is empty, when it is empty for a reason worth saying.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Error"/> because it belongs beside the list rather than above the
    /// composer, and separate from an empty list because "the server could not be reached" and "you
    /// have not logged anything yet" must never look the same.
    /// </remarks>
    [ObservableProperty]
    public partial string? RecentsProblem { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingRecents { get; set; }

    /// <summary>
    /// Opens the list and fetches it, falling back to the copy on the phone.
    /// </summary>
    /// <remarks>
    /// The offline half of milestone 14, and the reason the queue is worth having at all. Analysing
    /// a meal needs the server - the cascade lives there and stays there (CLAUDE.md section 10) - so
    /// with no network this list is the <em>only</em> way to log anything. Something eaten before
    /// needs no model: the numbers were confirmed once already, and re-logging is a card built from
    /// them.
    /// <para>
    /// The stale list says it is stale. Showing yesterday's answer as though the server had just
    /// given it would be the quiet kind of wrong this app keeps refusing to be.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private async Task ShowRecentsAsync()
    {
        IsAttachMenuOpen = false;
        IsRecentsOpen = true;
        IsLoadingRecents = true;
        RecentsProblem = null;

        try
        {
            var user = _session.CurrentUser;
            var recent = await _api.GetRecentItemsAsync();
            var now = _time.GetUtcNow();

            Recents.Clear();

            if (recent is null)
            {
                await ShowCachedRecentsAsync(user?.UserId, now);

                return;
            }

            foreach (var item in recent)
            {
                Recents.Add(new RecentItem(item, now, _units));
            }

            if (Recents.Count == 0)
            {
                RecentsProblem = "Nothing logged yet. Once you have, it will show up here.";
            }

            if (user is { } signedIn)
            {
                // Written on the way past rather than on a schedule: the list is only ever wanted
                // here, so the moment it is fetched is the moment worth keeping it.
                await _recentsCache.WriteAsync(signedIn.UserId, recent, now);
            }
        }
        finally
        {
            IsLoadingRecents = false;
        }
    }

    private async Task ShowCachedRecentsAsync(Guid? userId, DateTimeOffset now)
    {
        var cached = userId is { } id ? await _recentsCache.ReadAsync(id) : null;

        if (cached is null || cached.Items.Count == 0)
        {
            RecentsProblem = "Could not reach the server, and nothing has been saved on this phone "
                + "to offer instead.";

            return;
        }

        foreach (var item in cached.Items)
        {
            Recents.Add(new RecentItem(item, now, _units));
        }

        RecentsProblem = $"Offline - showing what this phone last saw, {Ago(now - cached.CachedUtc)}. "
            + "Anything you log now is sent when the server can be reached.";
    }

    /// <remarks>
    /// Coarser than <c>RecentItem</c>'s, and about a different thing: this dates the <em>list</em>,
    /// and the question it answers is only "is this worth trusting".
    /// </remarks>
    private static string Ago(TimeSpan elapsed) => elapsed switch
    {
        { TotalMinutes: < 90 } => "a moment ago",
        { TotalHours: < 24 } => $"{(int)elapsed.TotalHours} hours ago",
        { TotalDays: < 2 } => "yesterday",
        _ => $"{(int)elapsed.TotalDays} days ago"
    };

    [RelayCommand]
    private void HideRecents() => IsRecentsOpen = false;

    /// <summary>
    /// Puts a card for something eaten before into the chat. Saves nothing on its own.
    /// </summary>
    /// <remarks>
    /// Deliberately the same card, through the same save path, as an analysis produces. Nothing is
    /// written until the user confirms - confirm-before-save holds here because this reuses the code
    /// path that enforces it rather than because it re-implements it.
    /// <para>
    /// A user message goes in first so the transcript still reads as a conversation rather than as
    /// a card that appeared from nowhere.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private async Task LogAgainAsync(RecentItem? recent)
    {
        if (recent is null)
        {
            return;
        }

        IsRecentsOpen = false;
        Error = null;

        var catalog = await _nutrients.EnsureLoadedAsync();

        Messages.Add(new UserMessage($"{recent.Name} again", []));

        // No text and no photos: nothing here needs analysing, and the note on the entry would
        // otherwise claim the user typed something they did not.
        var meal = new SentMeal(null, []);

        Messages.Add(new ConfirmationCard(
            [new ConfirmableItem(recent.Item, catalog, _units)],
            (card, cancellationToken) => SaveAsync(card, meal, cancellationToken),
            _units));
    }

    /// <summary>The `+` button, bottom-left of the text box.</summary>
    [RelayCommand]
    private void ToggleAttachMenu() => IsAttachMenuOpen = !IsAttachMenuOpen;

    /// <summary>Takes a photo of the meal in front of you - the case the app exists for.</summary>
    [RelayCommand]
    private Task CapturePhotoAsync() => AttachAsync(_photoPicker.CaptureAsync);

    /// <summary>Attaches one already on the phone.</summary>
    [RelayCommand]
    private Task AttachPhotoAsync() => AttachAsync(_photoPicker.PickAsync);

    private async Task AttachAsync(Func<CancellationToken, Task<PhotoPickResult>> source)
    {
        Error = null;
        IsAttachMenuOpen = false;

        var picked = await source(CancellationToken.None);

        if (picked.Problem is { } problem)
        {
            Error = problem;

            return;
        }

        if (picked.Content is null)
        {
            // Backed out of the picker. Not a failure, and nothing to say about it.
            return;
        }

        await using var content = picked.Content;

        var image = await _downsizer.DownsizeAsync(content, MaxPhotoEdgePixels);

        if (image is null)
        {
            Error = "That file could not be read as an image. Try another one.";

            return;
        }

        if (image.Content.Length > MealImageRules.MaxBytes)
        {
            Error = $"That photo is over {MealImageRules.MaxBytes / (1024 * 1024)} MB even after "
                + "resizing. Try a different one.";

            return;
        }

        Attachments.Add(new PendingPhoto(image.Content, image.ContentType));

        AttachmentsChanged();
    }

    [RelayCommand]
    private void RemoveAttachment(PendingPhoto photo)
    {
        Attachments.Remove(photo);

        AttachmentsChanged();
    }

    /// <summary>
    /// A photo on its own is a message worth sending, so the send button has to notice one arriving.
    /// </summary>
    /// <remarks>
    /// <c>NotifyCanExecuteChanged</c> as well as the property change, and the second is the one that
    /// matters: a <c>Button</c> bound to a command takes its enabled state from
    /// <c>ICommand.CanExecute</c>, not from whatever the command's <c>CanExecute</c> method happens
    /// to read. Raising only the property left the button greyed out with a photo attached and no
    /// text - which is precisely the wordless log the camera exists for.
    /// </remarks>
    private void AttachmentsChanged()
    {
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(HasAttachments));

        SendCommand.NotifyCanExecuteChanged();
    }

    public bool HasAttachments => Attachments.Count > 0;

    /// <summary>Uploads the attachments, runs the cascade, and puts the answer in the transcript.</summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = string.IsNullOrWhiteSpace(Draft) ? null : Draft.Trim();
        var photos = Attachments.ToList();

        Error = null;
        Draft = "";
        Attachments.Clear();

        AttachmentsChanged();

        Messages.Add(new UserMessage(text, [.. photos.Select(photo => photo.Content)]));

        IsBusy = true;

        try
        {
            Status = photos.Count switch
            {
                0 => null,
                1 => "Uploading your photo...",
                _ => $"Uploading {photos.Count} photos..."
            };

            var imageIds = new List<Guid>(photos.Count);

            foreach (var photo in photos)
            {
                var uploaded = await _api.UploadMealImageAsync(photo.Content, photo.ContentType);

                if (!uploaded.Succeeded)
                {
                    // Stop at the first failure rather than analysing a partial meal. Whatever did
                    // upload stays on the server and is unreferenced, which costs a few megabytes
                    // and is a great deal better than reading half a plate as the whole of it.
                    AddWarning(uploaded.Problem ?? "A photo could not be uploaded.");

                    return;
                }

                imageIds.Add(uploaded.ImageId);
            }

            await RunAnalysisAsync(new SentMeal(text, imageIds));
        }
        finally
        {
            IsBusy = false;
            Status = null;
        }
    }

    /// <summary>Sends the last attempt again, reusing the photos already uploaded.</summary>
    [RelayCommand(CanExecute = nameof(CanRetry))]
    private async Task RetryAsync()
    {
        if (LastAttempt is not { } attempt)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await RunAnalysisAsync(attempt);
        }
        finally
        {
            IsBusy = false;
            Status = null;
        }
    }

    /// <summary>Abandons the analysis in flight. The server may still finish it; nothing is saved either way.</summary>
    [RelayCommand]
    private void Cancel() => inFlight?.Cancel();

    private async Task RunAnalysisAsync(SentMeal meal)
    {
        var started = conversation;

        LastAttempt = meal;

        Status = "Working it out - this can take a minute or two...";

        // Fetched alongside rather than before: a card whose micronutrient names could not be looked
        // up still shows its calories and macros, so this must never be able to stop an analysis.
        var catalog = await _nutrients.EnsureLoadedAsync();

        using var cancellation = new CancellationTokenSource();
        inFlight = cancellation;

        MealAnalysisResult result;

        try
        {
            result = await _api.AnalyzeMealAsync(
                new AnalyzeMealRequest { Text = meal.Text, ImageIds = [.. meal.ImageIds] },
                cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            if (started == conversation)
            {
                AddWarning("Cancelled. Nothing was saved, and you can send it again.");
            }

            return;
        }
        finally
        {
            inFlight = null;
        }

        // Signed out while the server was thinking. The reply belongs to a conversation that no
        // longer exists, and appending it here would show one account another account's meal.
        if (started != conversation)
        {
            return;
        }

        // The model's own sentence first, when it has one - it is the part written for a person, and
        // on a failure it is usually more use than anything the server can say about the failure.
        if (!string.IsNullOrWhiteSpace(result.Note))
        {
            Messages.Add(new NoteMessage(result.Note));
        }

        // Independently of what the model said. Section 5: a rate limit, a timeout or an assumed
        // serving size reaches the user even when the reply never mentions it.
        foreach (var warning in result.Warnings)
        {
            Messages.Add(new WarningMessage(warning));
        }

        if (result.Outcome is not MealAnalysisOutcome.Analyzed || result.Items.Count == 0)
        {
            // No card, so nothing can be saved. The warnings above already said why.
            return;
        }

        var items = result.Items
            .Select(item => new ConfirmableItem(item, catalog, _units))
            .ToList();

        Messages.Add(new ConfirmationCard(
            items,
            (card, cancellationToken) => SaveAsync(card, meal, cancellationToken),
            _units));

        // Whatever the user does with the card, this attempt is answered. Leaving it retryable
        // would offer a second identical analysis next to a card already waiting on a decision.
        LastAttempt = null;
    }

    /// <summary>
    /// Turns an approved card into a stored meal - on the server, or on the phone until it can be.
    /// </summary>
    /// <remarks>
    /// Two fields are stamped here and nowhere else, and both exist because the save may not happen
    /// for hours.
    /// <para>
    /// <b><c>LoggedUtc</c> is the moment the card was approved</b>, on every save rather than only
    /// on a queued one. The server would otherwise stamp its own "now" when the queue finally
    /// reached it, which turns a breakfast confirmed in a tunnel into an evening meal - and, past
    /// midnight, into one on the wrong day. One rule for both paths, so a meal's time never depends
    /// on how long it waited. It is an <em>instant</em> and never a date: which local day it falls
    /// in is still the server's decision, as milestone 11 and milestone 12 both insisted.
    /// </para>
    /// <para>
    /// <b><c>ClientRequestId</c> makes the save repeatable.</b> A queued row is retried with nobody
    /// watching, and a reply lost on the way back is indistinguishable from a request that never
    /// arrived; the id is what makes the second attempt find the first one's entry instead of
    /// writing a second dinner.
    /// </para>
    /// </remarks>
    private async Task SaveAsync(
        ConfirmationCard card,
        SentMeal meal,
        CancellationToken cancellationToken)
    {
        var request = new SaveLogEntryRequest
        {
            ClientRequestId = Guid.CreateVersion7(),
            LoggedUtc = _time.GetUtcNow(),
            Note = meal.Text,
            ImageIds = [.. meal.ImageIds],

            // A copy of what the user approved and nothing else - see ToSaveLogItemRequest. Any
            // arithmetic here would be a second place for a number to differ from the one on screen.
            Items = [.. card.Items.Select(item => item.Corrected.ToSaveLogItemRequest())]
        };

        var result = await _outbox.SubmitAsync(request, Describe(card), cancellationToken);

        switch (result.Outcome)
        {
            case SubmitOutcome.Saved:
                card.Problem = null;
                card.State = ConfirmationState.Saved;
                card.Detach();

                break;

            case SubmitOutcome.Queued:
                // Settled as far as the user is concerned, and deliberately not called "saved":
                // today's total will not move until the queue empties, and a card claiming
                // otherwise would be the app telling a small lie about the server.
                card.Problem = null;
                card.State = ConfirmationState.Queued;
                card.Detach();

                Messages.Add(new WarningMessage(
                    result.Problem ?? "Saved on this phone. It will be sent when the server can be "
                    + "reached."));

                break;

            default:
                // Rejected or unstorable. The card stays pending with its numbers intact, because
                // the only thing that can fix either is the person looking at it.
                card.Problem = result.Problem;

                break;
        }
    }

    /// <summary>What to call a queued meal on the Home banner, where there is no card to look at.</summary>
    private static string Describe(ConfirmationCard card)
    {
        var first = card.Items[0].Corrected.Name;

        return card.Items.Count > 1 ? $"{first} and {card.Items.Count - 1} more" : first;
    }

    /// <summary>
    /// Empties the conversation when the account behind it changes.
    /// </summary>
    /// <remarks>
    /// The transcript is the one piece of state here that is worth keeping across a visit to
    /// another tab and must not be kept across a sign-out: it holds what somebody ate, what their
    /// kitchen looks like and what they typed about it. Clearing beats leaving it for the next
    /// account to find.
    /// <para>
    /// An analysis in flight is abandoned rather than awaited. The server may still finish it, which
    /// costs nothing - <c>POST /api/analyze</c> writes nothing - and the reply would arrive for a
    /// session that no longer exists.
    /// </para>
    /// </remarks>
    private void OnSessionChanged()
    {
        conversation++;
        inFlight?.Cancel();

        Messages.Clear();
        Attachments.Clear();
        AttachmentsChanged();

        Recents.Clear();
        IsRecentsOpen = false;
        RecentsProblem = null;

        Draft = string.Empty;
        Error = null;
        Status = null;
        LastAttempt = null;
        IsAttachMenuOpen = false;
    }

    private void AddWarning(string text) => Messages.Add(new WarningMessage(text));

    partial void OnIsBusyChanged(bool value)
    {
        SendCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
    }

    partial void OnDraftChanged(string value) => SendCommand.NotifyCanExecuteChanged();

    partial void OnLastAttemptChanged(SentMeal? value) => RetryCommand.NotifyCanExecuteChanged();
}

/// <summary>A photo chosen but not yet uploaded.</summary>
/// <param name="Content">Already re-encoded and within the server's size limit.</param>
public sealed record PendingPhoto(byte[] Content, string ContentType);

/// <summary>
/// One thing sent for analysis: the words, and the photos as the server now knows them.
/// </summary>
/// <remarks>
/// Ids rather than bytes, which is what makes a retry free - the photos are already stored, and
/// re-analysing them costs no second upload and produces no second copy.
/// </remarks>
public sealed record SentMeal(string? Text, IReadOnlyList<Guid> ImageIds);
