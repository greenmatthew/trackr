using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Auth;
using Trackr.Mobile.Core.Nutrition;
using Trackr.Mobile.Core.Platform;
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
        AuthSession session)
    {
        _api = api;
        _photoPicker = photoPicker;
        _downsizer = downsizer;
        _nutrients = nutrients;
        _session = session;

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
            .Select(item => new ConfirmableItem(item, catalog))
            .ToList();

        Messages.Add(new ConfirmationCard(
            items,
            (card, cancellationToken) => SaveAsync(card, meal, cancellationToken)));

        // Whatever the user does with the card, this attempt is answered. Leaving it retryable
        // would offer a second identical analysis next to a card already waiting on a decision.
        LastAttempt = null;
    }

    private async Task SaveAsync(
        ConfirmationCard card,
        SentMeal meal,
        CancellationToken cancellationToken)
    {
        var request = new SaveLogEntryRequest
        {
            Note = meal.Text,
            ImageIds = [.. meal.ImageIds],

            // A copy of what the user approved and nothing else - see ToSaveLogItemRequest. Any
            // arithmetic here would be a second place for a number to differ from the one on screen.
            Items = [.. card.Items.Select(item => item.Corrected.ToSaveLogItemRequest())]
        };

        var result = await _api.SaveLogEntryAsync(request, cancellationToken);

        if (!result.Succeeded)
        {
            card.Problem = result.Problem;

            return;
        }

        card.Problem = null;
        card.State = ConfirmationState.Saved;
        card.Detach();
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
