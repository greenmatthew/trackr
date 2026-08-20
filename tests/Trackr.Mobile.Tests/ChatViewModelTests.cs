using NSubstitute;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Auth;
using Trackr.Mobile.Core.Nutrition;
using Trackr.Mobile.Core.Platform;
using Trackr.Mobile.Core.ViewModels;
using Trackr.Mobile.Core.ViewModels.Chat;
using Trackr.Shared.Nutrition;

namespace Trackr.Mobile.Tests;

/// <summary>
/// The core loop: say what you ate, approve the numbers, and only then have anything written.
/// </summary>
/// <remarks>
/// Every one of these is about a rule from CLAUDE.md rather than about a screen - nothing is saved
/// before a confirm, no failure disappears silently, and what gets stored is what the user
/// approved. They run with plain <c>dotnet test</c> because all three of the pieces that need
/// Android (the picker, the resizer, the network) sit behind Core interfaces.
/// </remarks>
public sealed class ChatViewModelTests
{
    [Fact]
    public void An_empty_message_cannot_be_sent()
    {
        var (chat, _, _, _) = Build();

        Assert.False(chat.CanSend);

        chat.Draft = "   ";

        Assert.False(chat.CanSend);
    }

    [Fact]
    public async Task Photos_are_uploaded_first_and_analysed_by_id()
    {
        var (chat, api, picker, downsizer) = Build();

        var imageId = Guid.NewGuid();

        picker.PickAsync(Arg.Any<CancellationToken>())
            .Returns(PhotoPickResult.Picked(new MemoryStream([1, 2, 3])));
        downsizer.DownsizeAsync(Arg.Any<Stream>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new DownsizedImage([4, 5, 6], "image/jpeg"));
        api.UploadMealImageAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(MealImageUploadResult.Ok(imageId));
        api.AnalyzeMealAsync(Arg.Any<AnalyzeMealRequest>(), Arg.Any<CancellationToken>())
            .Returns(MealAnalysisResult.Analyzed([Analysed()]));

        await chat.AttachPhotoCommand.ExecuteAsync(null);

        chat.Draft = "two of these";

        await chat.SendCommand.ExecuteAsync(null);

        // The bytes that came out of the resizer, not the ones that came out of the gallery.
        await api.Received(1).UploadMealImageAsync(
            Arg.Is<byte[]>(content => content.SequenceEqual(new byte[] { 4, 5, 6 })),
            "image/jpeg",
            Arg.Any<CancellationToken>());

        await api.Received(1).AnalyzeMealAsync(
            Arg.Is<AnalyzeMealRequest>(request =>
                request.Text == "two of these" && request.ImageIds.Single() == imageId),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Half a plate read as the whole of it would be a confidently wrong number, which is the
    /// expensive failure this whole flow is arranged to avoid.
    /// </summary>
    [Fact]
    public async Task A_photo_that_did_not_upload_stops_the_analysis()
    {
        var (chat, api, picker, downsizer) = Build();

        picker.PickAsync(Arg.Any<CancellationToken>())
            .Returns(PhotoPickResult.Picked(new MemoryStream([1])));
        downsizer.DownsizeAsync(Arg.Any<Stream>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new DownsizedImage([1], "image/jpeg"));
        api.UploadMealImageAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(MealImageUploadResult.Failed("The server ran out of room."));

        await chat.AttachPhotoCommand.ExecuteAsync(null);
        await chat.SendCommand.ExecuteAsync(null);

        await api.DidNotReceive().AnalyzeMealAsync(
            Arg.Any<AnalyzeMealRequest>(),
            Arg.Any<CancellationToken>());

        Assert.Contains(
            chat.Messages.OfType<WarningMessage>(),
            warning => warning.Text.Contains("ran out of room"));
    }

    /// <summary>
    /// CLAUDE.md section 5: rate limits, timeouts and parse failures reach the user in plain
    /// language. None of them may produce a card, because a card is a thing that can be saved.
    /// </summary>
    [Fact]
    public async Task A_failed_analysis_shows_every_warning_and_offers_no_card()
    {
        var (chat, api, _, _) = Build();

        api.AnalyzeMealAsync(Arg.Any<AnalyzeMealRequest>(), Arg.Any<CancellationToken>())
            .Returns(MealAnalysisResult.Failed(
                "The local model did not answer within 240 seconds.",
                note: "I could not read the label.",
                warnings: ["Open Food Facts rate-limited the lookup."]));

        chat.Draft = "a biscuit";

        await chat.SendCommand.ExecuteAsync(null);

        Assert.Empty(chat.Messages.OfType<ConfirmationCard>());

        var warnings = chat.Messages.OfType<WarningMessage>().Select(message => message.Text).ToList();

        Assert.Contains(warnings, text => text.Contains("rate-limited"));
        Assert.Contains(warnings, text => text.Contains("240 seconds"));

        // The model's own sentence is the part written for a person, and on a failure it is usually
        // more use than anything the server can say about the failure.
        Assert.Contains(chat.Messages.OfType<NoteMessage>(), note => note.Text.Contains("label"));
    }

    [Fact]
    public async Task A_failed_analysis_can_be_retried_without_uploading_the_photos_again()
    {
        var (chat, api, picker, downsizer) = Build();

        var imageId = Guid.NewGuid();

        picker.PickAsync(Arg.Any<CancellationToken>())
            .Returns(PhotoPickResult.Picked(new MemoryStream([1])));
        downsizer.DownsizeAsync(Arg.Any<Stream>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new DownsizedImage([1], "image/jpeg"));
        api.UploadMealImageAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(MealImageUploadResult.Ok(imageId));
        api.AnalyzeMealAsync(Arg.Any<AnalyzeMealRequest>(), Arg.Any<CancellationToken>())
            .Returns(MealAnalysisResult.Failed("Could not reach the server."));

        await chat.AttachPhotoCommand.ExecuteAsync(null);
        await chat.SendCommand.ExecuteAsync(null);

        Assert.True(chat.CanRetry);

        await chat.RetryCommand.ExecuteAsync(null);

        await api.Received(1).UploadMealImageAsync(
            Arg.Any<byte[]>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        await api.Received(2).AnalyzeMealAsync(
            Arg.Is<AnalyzeMealRequest>(request => request.ImageIds.Single() == imageId),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The rule the endpoint and this screen both exist to enforce.</summary>
    [Fact]
    public async Task An_analysis_saves_nothing_until_the_card_is_confirmed()
    {
        var (chat, api, _, _) = Build();

        api.AnalyzeMealAsync(Arg.Any<AnalyzeMealRequest>(), Arg.Any<CancellationToken>())
            .Returns(MealAnalysisResult.Analyzed([Analysed()]));

        chat.Draft = "an egg";

        await chat.SendCommand.ExecuteAsync(null);

        Assert.Single(chat.Messages.OfType<ConfirmationCard>());

        await api.DidNotReceive().SaveLogEntryAsync(
            Arg.Any<SaveLogEntryRequest>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The corrected numbers, copied field for field. A mapping layer between the card and the log
    /// would be a second place for a number to change between what was approved and what was stored.
    /// </summary>
    [Fact]
    public async Task Confirming_saves_exactly_what_the_card_holds_including_corrections()
    {
        var (chat, api, _, _) = Build();

        api.AnalyzeMealAsync(Arg.Any<AnalyzeMealRequest>(), Arg.Any<CancellationToken>())
            .Returns(MealAnalysisResult.Analyzed([Analysed(quantity: 1m, energyKcal: 78m)]));
        api.SaveLogEntryAsync(Arg.Any<SaveLogEntryRequest>(), Arg.Any<CancellationToken>())
            .Returns(SaveLogResult.Ok(Saved()));

        chat.Draft = "two eggs";

        await chat.SendCommand.ExecuteAsync(null);

        var card = chat.Messages.OfType<ConfirmationCard>().Single();

        card.Items.Single().QuantityText = "2";
        card.Items.Single().ProteinText = "6.5";

        await card.ConfirmCommand.ExecuteAsync(null);

        await api.Received(1).SaveLogEntryAsync(
            Arg.Is<SaveLogEntryRequest>(request =>
                request.Note == "two eggs"
                && request.Items.Single().Quantity == 2m
                && request.Items.Single().EnergyKcal == 78m
                && request.Items.Single().ProteinG == 6.5m
                && request.Items.Single().Name == "Egg"
                && request.Items.Single().Nutrients["sodium"] == 62m),
            Arg.Any<CancellationToken>());

        Assert.Equal(ConfirmationState.Saved, card.State);
    }

    /// <summary>A number that could not be read must stop the save, never become a zero.</summary>
    [Fact]
    public async Task A_card_with_an_unreadable_number_cannot_be_confirmed()
    {
        var (chat, api, _, _) = Build();

        api.AnalyzeMealAsync(Arg.Any<AnalyzeMealRequest>(), Arg.Any<CancellationToken>())
            .Returns(MealAnalysisResult.Analyzed([Analysed()]));

        chat.Draft = "an egg";

        await chat.SendCommand.ExecuteAsync(null);

        var card = chat.Messages.OfType<ConfirmationCard>().Single();

        card.Items.Single().EnergyText = "about eighty";

        Assert.False(card.CanConfirm);
        Assert.False(card.ConfirmCommand.CanExecute(null));

        // Zero servings is not a thing that was eaten, and the server refuses it.
        card.Items.Single().EnergyText = "80";
        card.Items.Single().QuantityText = "0";

        Assert.False(card.CanConfirm);
    }

    [Fact]
    public async Task Discarding_a_card_writes_nothing()
    {
        var (chat, api, _, _) = Build();

        api.AnalyzeMealAsync(Arg.Any<AnalyzeMealRequest>(), Arg.Any<CancellationToken>())
            .Returns(MealAnalysisResult.Analyzed([Analysed()]));

        chat.Draft = "an egg";

        await chat.SendCommand.ExecuteAsync(null);

        var card = chat.Messages.OfType<ConfirmationCard>().Single();

        card.DiscardCommand.Execute(null);

        Assert.Equal(ConfirmationState.Discarded, card.State);
        Assert.False(card.IsPending);

        await api.DidNotReceive().SaveLogEntryAsync(
            Arg.Any<SaveLogEntryRequest>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A failed save leaves the card usable rather than losing the corrections.</summary>
    [Fact]
    public async Task A_save_that_failed_says_so_and_stays_confirmable()
    {
        var (chat, api, _, _) = Build();

        api.AnalyzeMealAsync(Arg.Any<AnalyzeMealRequest>(), Arg.Any<CancellationToken>())
            .Returns(MealAnalysisResult.Analyzed([Analysed()]));
        api.SaveLogEntryAsync(Arg.Any<SaveLogEntryRequest>(), Arg.Any<CancellationToken>())
            .Returns(SaveLogResult.Failed("Could not reach the server. Nothing was saved."));

        chat.Draft = "an egg";

        await chat.SendCommand.ExecuteAsync(null);

        var card = chat.Messages.OfType<ConfirmationCard>().Single();

        await card.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(ConfirmationState.Pending, card.State);
        Assert.Contains("Nothing was saved", card.Problem);
        Assert.True(card.CanConfirm);
    }

    /// <summary>
    /// Milestone 8's validator earns its place here or nowhere: a low-confidence item has to be
    /// distinguishable on the card, and its warnings readable without a tap.
    /// </summary>
    [Fact]
    public async Task A_low_confidence_item_is_marked_and_its_warnings_are_carried()
    {
        var (chat, api, _, _) = Build();

        api.AnalyzeMealAsync(Arg.Any<AnalyzeMealRequest>(), Arg.Any<CancellationToken>())
            .Returns(MealAnalysisResult.Analyzed(
            [
                Analysed(confidence: AnalysisConfidence.Low, warnings: ["The calories do not match the macros."])
            ]));

        chat.Draft = "a mystery";

        await chat.SendCommand.ExecuteAsync(null);

        var item = chat.Messages.OfType<ConfirmationCard>().Single().Items.Single();

        Assert.True(item.IsLowConfidence);
        Assert.True(item.HasWarnings);
        Assert.Contains("do not match", item.Warnings.Single());
    }

    /// <summary>
    /// Absent means not measured, never zero. A card shows the nutrients the source reported and no
    /// others - see wiki/Nutrient-Reference.md.
    /// </summary>
    [Fact]
    public async Task A_card_shows_only_the_nutrients_that_were_reported()
    {
        var (chat, api, _, _) = Build();

        api.AnalyzeMealAsync(Arg.Any<AnalyzeMealRequest>(), Arg.Any<CancellationToken>())
            .Returns(MealAnalysisResult.Analyzed([Analysed()]));

        chat.Draft = "an egg";

        await chat.SendCommand.ExecuteAsync(null);

        var item = chat.Messages.OfType<ConfirmationCard>().Single().Items.Single();

        // The catalog below knows about fibre too, and the item did not report it.
        var row = Assert.Single(item.Nutrients);

        Assert.Equal("Sodium", row.DisplayName);
        Assert.Equal("62 mg", row.Amount);
    }

    /// <summary>
    /// Without the catalog there are no names and no units, and raw keys would be worse than
    /// nothing. The calories and macros are typed fields and do not need it.
    /// </summary>
    [Fact]
    public async Task A_card_drawn_without_the_nutrient_catalog_still_shows_its_macros()
    {
        var (chat, api, _, _) = Build(withNutrientCatalog: false);

        api.AnalyzeMealAsync(Arg.Any<AnalyzeMealRequest>(), Arg.Any<CancellationToken>())
            .Returns(MealAnalysisResult.Analyzed([Analysed()]));

        chat.Draft = "an egg";

        await chat.SendCommand.ExecuteAsync(null);

        var item = chat.Messages.OfType<ConfirmationCard>().Single().Items.Single();

        Assert.Empty(item.Nutrients);
        Assert.False(item.HasNutrients);
        Assert.Equal("78", item.EnergyText);
    }

    [Fact]
    public async Task Sending_clears_the_draft_and_the_attachments()
    {
        var (chat, api, picker, downsizer) = Build();

        picker.PickAsync(Arg.Any<CancellationToken>())
            .Returns(PhotoPickResult.Picked(new MemoryStream([1])));
        downsizer.DownsizeAsync(Arg.Any<Stream>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new DownsizedImage([1], "image/jpeg"));
        api.UploadMealImageAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(MealImageUploadResult.Ok(Guid.NewGuid()));
        api.AnalyzeMealAsync(Arg.Any<AnalyzeMealRequest>(), Arg.Any<CancellationToken>())
            .Returns(MealAnalysisResult.Analyzed([Analysed()]));

        await chat.AttachPhotoCommand.ExecuteAsync(null);

        chat.Draft = "an egg";

        await chat.SendCommand.ExecuteAsync(null);

        Assert.Equal("", chat.Draft);
        Assert.Empty(chat.Attachments);

        // What was said stays in the transcript, photo and all.
        var sent = chat.Messages.OfType<UserMessage>().Single();

        Assert.Equal("an egg", sent.Text);
        Assert.True(sent.HasPhotos);
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_is_refused_before_anything_is_uploaded()
    {
        var (chat, api, picker, downsizer) = Build();

        picker.PickAsync(Arg.Any<CancellationToken>())
            .Returns(PhotoPickResult.Picked(new MemoryStream([1])));
        downsizer.DownsizeAsync(Arg.Any<Stream>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((DownsizedImage?)null);

        await chat.AttachPhotoCommand.ExecuteAsync(null);

        Assert.Empty(chat.Attachments);
        Assert.Contains("could not be read", chat.Error);

        await api.DidNotReceive().UploadMealImageAsync(
            Arg.Any<byte[]>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The case the phone exists for: photograph what is in front of you, in the app, at the table.
    /// </summary>
    [Fact]
    public async Task A_photo_taken_with_the_camera_is_attached_and_closes_the_menu()
    {
        var (chat, _, picker, downsizer) = Build();

        picker.CaptureAsync(Arg.Any<CancellationToken>())
            .Returns(PhotoPickResult.Picked(new MemoryStream([1])));
        downsizer.DownsizeAsync(Arg.Any<Stream>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new DownsizedImage([7], "image/jpeg"));

        chat.ToggleAttachMenuCommand.Execute(null);

        Assert.True(chat.IsAttachMenuOpen);

        await chat.CapturePhotoCommand.ExecuteAsync(null);

        Assert.Single(chat.Attachments);
        Assert.False(chat.IsAttachMenuOpen);
        Assert.True(chat.CanSend);

        // The regression this line exists for: a Button takes its enabled state from the command,
        // not from the property the command reads, so a photo with no text left Send greyed out -
        // which is exactly the wordless log the camera is for.
        Assert.True(chat.SendCommand.CanExecute(null));
        Assert.True(chat.HasAttachments);

        await picker.DidNotReceive().PickAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>Refusing the camera is an answer, and the gallery still works. Say so.</summary>
    [Fact]
    public async Task A_refused_camera_is_reported_and_attaches_nothing()
    {
        var (chat, _, picker, _) = Build();

        picker.CaptureAsync(Arg.Any<CancellationToken>())
            .Returns(PhotoPickResult.Failed("Trackr was not allowed to use the camera."));

        await chat.CapturePhotoCommand.ExecuteAsync(null);

        Assert.Empty(chat.Attachments);
        Assert.Contains("not allowed", chat.Error);
    }

    [Fact]
    public async Task Recent_items_are_offered_from_the_attach_menu()
    {
        var (chat, api, _, _) = Build();

        api.GetRecentItemsAsync(Arg.Any<CancellationToken>()).Returns([Recent(), Recent("Toast")]);

        chat.ToggleAttachMenuCommand.Execute(null);

        await chat.ShowRecentsCommand.ExecuteAsync(null);

        Assert.True(chat.IsRecentsOpen);
        Assert.False(chat.IsAttachMenuOpen);
        Assert.Equal(2, chat.Recents.Count);
        Assert.Equal("312 kcal \u00b7 2 days ago", chat.Recents[0].Summary);
        Assert.Null(chat.RecentsProblem);
    }

    /// <remarks>
    /// The distinction the whole feature turns on: an unreachable server must never be drawn as an
    /// account that has never logged anything.
    /// </remarks>
    [Fact]
    public async Task A_failed_recents_fetch_says_so_rather_than_showing_an_empty_history()
    {
        var (chat, api, _, _) = Build();

        api.GetRecentItemsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<RecentItemResponse>?)null);

        await chat.ShowRecentsCommand.ExecuteAsync(null);

        Assert.Empty(chat.Recents);
        Assert.Contains("Could not reach the server", chat.RecentsProblem!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_account_that_has_logged_nothing_is_told_so_plainly()
    {
        var (chat, api, _, _) = Build();

        api.GetRecentItemsAsync(Arg.Any<CancellationToken>()).Returns([]);

        await chat.ShowRecentsCommand.ExecuteAsync(null);

        Assert.Contains("Nothing logged yet", chat.RecentsProblem!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Re-logging goes through the same card and the same save path an analysis produces.
    /// </summary>
    [Fact]
    public async Task Logging_something_again_puts_a_card_in_the_chat_and_saves_nothing()
    {
        var (chat, api, _, _) = Build();

        api.GetRecentItemsAsync(Arg.Any<CancellationToken>()).Returns([Recent()]);

        await chat.ShowRecentsCommand.ExecuteAsync(null);
        await chat.LogAgainCommand.ExecuteAsync(chat.Recents[0]);

        Assert.False(chat.IsRecentsOpen);
        Assert.Single(chat.Messages.OfType<UserMessage>());

        var card = Assert.Single(chat.Messages.OfType<ConfirmationCard>());

        Assert.Equal("logged before", Assert.Single(card.Items).SourceDescription);

        await api.DidNotReceive().SaveLogEntryAsync(
            Arg.Any<SaveLogEntryRequest>(),
            Arg.Any<CancellationToken>());

        // And it was never analysed - that is the whole saving.
        await api.DidNotReceive().AnalyzeMealAsync(
            Arg.Any<AnalyzeMealRequest>(),
            Arg.Any<CancellationToken>());
    }

    /// <remarks>
    /// The barcode has to survive the round trip, or a re-logged packaged product stops filing
    /// itself in the catalog the second time.
    /// </remarks>
    [Fact]
    public async Task Confirming_a_re_logged_item_saves_what_the_card_holds()
    {
        var (chat, api, _, _) = Build();

        api.GetRecentItemsAsync(Arg.Any<CancellationToken>())
            .Returns([Recent(energyKcal: 312m, barcode: "0076840100446")]);
        api.SaveLogEntryAsync(Arg.Any<SaveLogEntryRequest>(), Arg.Any<CancellationToken>())
            .Returns(SaveLogResult.Ok(Saved()));

        await chat.ShowRecentsCommand.ExecuteAsync(null);
        await chat.LogAgainCommand.ExecuteAsync(chat.Recents[0]);

        var card = Assert.Single(chat.Messages.OfType<ConfirmationCard>());

        card.Items[0].QuantityText = "2";

        await card.ConfirmCommand.ExecuteAsync(null);

        await api.Received(1).SaveLogEntryAsync(
            Arg.Is<SaveLogEntryRequest>(request =>
                request.Items[0].Quantity == 2m
                && request.Items[0].EnergyKcal == 312m
                && request.Items[0].Barcode == "0076840100446"
                && request.Note == null
                && request.ImageIds.Count == 0),
            Arg.Any<CancellationToken>());
    }

    /// <remarks>
    /// SourceDescription ends in a catch-all arm reading "estimated", so a new source silently
    /// becomes an estimate rather than failing to compile. This is the test that notices.
    /// </remarks>
    [Theory]
    [InlineData(AnalyzedItemSource.Database, "from Open Food Facts")]
    [InlineData(AnalyzedItemSource.DatabaseAndModel, "Open Food Facts, gaps estimated")]
    [InlineData(AnalyzedItemSource.PreviouslyLogged, "logged before")]
    [InlineData(AnalyzedItemSource.Model, "estimated")]
    public void Every_source_says_where_its_numbers_came_from(AnalyzedItemSource source, string expected)
    {
        var item = new ConfirmableItem(Analysed() with { Source = source }, nutrientCatalog: null);

        Assert.Equal(expected, item.SourceDescription);
    }

    /// <remarks>
    /// The barcode is never drawn - section 1 keeps it invisible - but it has to survive the trip
    /// from the analysis to the save, because it is the key the server files the item away under.
    /// </remarks>
    [Fact]
    public async Task A_confirmed_item_carries_the_barcode_it_was_identified_by()
    {
        var (chat, api, _, _) = Build();

        api.AnalyzeMealAsync(Arg.Any<AnalyzeMealRequest>(), Arg.Any<CancellationToken>())
            .Returns(MealAnalysisResult.Analyzed([Analysed(barcode: "0076840100446")]));
        api.SaveLogEntryAsync(Arg.Any<SaveLogEntryRequest>(), Arg.Any<CancellationToken>())
            .Returns(SaveLogResult.Ok(Saved()));

        chat.Draft = "ice cream";

        await chat.SendCommand.ExecuteAsync(null);

        var card = chat.Messages.OfType<ConfirmationCard>().Single();

        await card.ConfirmCommand.ExecuteAsync(null);

        await api.Received(1).SaveLogEntryAsync(
            Arg.Is<SaveLogEntryRequest>(request => request.Items[0].Barcode == "0076840100446"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The view model is a singleton so the transcript survives a tab switch, and that is exactly
    /// why it must not survive an account.
    /// </summary>
    /// <remarks>
    /// What is held here is what somebody ate, what their kitchen looks like and what they typed
    /// about it. Leaving it for the next person to sign in on the device is the same failure
    /// AvatarStore drops its bytes to avoid.
    /// </remarks>
    [Fact]
    public async Task Signing_out_empties_the_conversation()
    {
        var (chat, api, _, _, session) = BuildWithSession();

        api.AnalyzeMealAsync(Arg.Any<AnalyzeMealRequest>(), Arg.Any<CancellationToken>())
            .Returns(MealAnalysisResult.Analyzed([Analysed()]));

        chat.Draft = "an egg";

        await chat.SendCommand.ExecuteAsync(null);

        Assert.NotEmpty(chat.Messages);

        await session.SignOutAsync();

        Assert.Empty(chat.Messages);
        Assert.Empty(chat.Attachments);
        Assert.Equal(string.Empty, chat.Draft);
        Assert.False(chat.CanRetry);
    }

    /// <remarks>
    /// Cancelling the request is not enough on its own. An analysis takes minutes, the reply may
    /// already be on its way back, and it arrives on a continuation that knows nothing about the
    /// sign-out that happened while it waited - so without the generation check it would append one
    /// account's meal to the next account's empty chat.
    /// </remarks>
    [Fact]
    public async Task A_reply_that_arrives_after_a_sign_out_is_never_shown()
    {
        var (chat, api, _, _, session) = BuildWithSession();

        var analysing = new TaskCompletionSource<MealAnalysisResult>();

        api.AnalyzeMealAsync(Arg.Any<AnalyzeMealRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => analysing.Task);

        chat.Draft = "an egg";

        var sending = chat.SendCommand.ExecuteAsync(null);

        await session.SignOutAsync();

        analysing.SetResult(MealAnalysisResult.Analyzed([Analysed()]));

        await sending;

        Assert.Empty(chat.Messages);
    }

    /// <summary>A fixed clock, so "2 days ago" is a thing a test can assert.</summary>
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));

    private static RecentItemResponse Recent(
        string name = "Porridge",
        int timesLogged = 1,
        int daysAgo = 2,
        decimal energyKcal = 312m,
        string? barcode = null) =>
        new(
            Clock.GetUtcNow().AddDays(-daysAgo),
            timesLogged,
            Analysed(energyKcal: energyKcal, barcode: barcode) with
            {
                Name = name,
                Source = AnalyzedItemSource.PreviouslyLogged
            });

    private static MealAnalysisItem Analysed(
        decimal quantity = 1m,
        decimal energyKcal = 78m,
        AnalysisConfidence confidence = AnalysisConfidence.Normal,
        IReadOnlyList<string>? warnings = null,
        string? barcode = null) =>
        new(
            Name: "Egg",
            Brand: null,
            Barcode: barcode,
            MealImageId: null,
            Source: AnalyzedItemSource.Model,
            Confidence: confidence,
            Quantity: quantity,
            ServingSize: 50m,
            ServingUnit: "g",
            EnergyKcal: energyKcal,
            FatG: 5m,
            CarbohydrateG: 0.6m,
            ProteinG: 6.3m,
            Nutrients: new Dictionary<string, decimal>(StringComparer.Ordinal) { ["sodium"] = 62m },
            Warnings: warnings ?? []);

    private static LogEntryResponse Saved() =>
        new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            null,
            [],
            [],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

    private static (ChatViewModel Chat, ITrackrApiClient Api, IPhotoPicker Picker, IImageDownsizer Downsizer)
        Build(bool withNutrientCatalog = true)
    {
        var (chat, api, picker, downsizer, _) = BuildWithSession(withNutrientCatalog);

        return (chat, api, picker, downsizer);
    }

    /// <summary>
    /// The same, with the session handed back so a test can sign out mid-conversation.
    /// </summary>
    /// <remarks>
    /// A real <see cref="AuthSession"/> rather than a substitute, matching LoginViewModelTests:
    /// raising <c>Changed</c> at the right moment is the behaviour under test, and a substitute
    /// would stub out exactly that.
    /// </remarks>
    private static (ChatViewModel Chat, ITrackrApiClient Api, IPhotoPicker Picker, IImageDownsizer Downsizer, AuthSession Session)
        BuildWithSession(bool withNutrientCatalog = true)
    {
        var api = Substitute.For<ITrackrApiClient>();
        var picker = Substitute.For<IPhotoPicker>();
        var downsizer = Substitute.For<IImageDownsizer>();

        api.GetNutrientsAsync(Arg.Any<CancellationToken>()).Returns(withNutrientCatalog
            ?
            [
                new NutrientResponse("sodium", "Sodium", NutrientUnit.Milligram, NutrientGroup.SterolsAndElectrolytes, 90, false),
                new NutrientResponse("fibre", "Fibre", NutrientUnit.Gram, NutrientGroup.CarbohydrateBreakdown, 60, false)
            ]
            : null);

        var settings = Substitute.For<IServerSettings>();
        settings.BaseUrl.Returns(new Uri("https://trackr.example.test/"));

        var session = new AuthSession(
            api, Substitute.For<ITokenStore>(), settings, LocalStore.InMemory());

        return (
            new ChatViewModel(
                api, picker, downsizer, new NutrientCatalogCache(api), session, Clock),
            api,
            picker,
            downsizer,
            session);
    }
}
