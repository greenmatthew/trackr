using NSubstitute;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Auth;
using Trackr.Mobile.Core.Platform;
using Trackr.Mobile.Core.ViewModels;
using Trackr.Shared.Auth;

namespace Trackr.Mobile.Tests;

/// <summary>
/// Choosing a profile picture: pick, shrink, upload, and every way that can stop early.
/// </summary>
/// <remarks>
/// The picker and the resizer are the two pieces that need Android, and both sit behind Core
/// interfaces precisely so the decisions between them can be tested here rather than by
/// tapping through the app (CLAUDE.md section 11).
/// </remarks>
public sealed class ProfileViewModelTests
{
    [Fact]
    public async Task Backing_out_of_the_picker_is_not_an_error()
    {
        var (viewModel, api, picker, downsizer) = Build();

        picker.PickAsync(Arg.Any<CancellationToken>()).Returns(PhotoPickResult.Cancelled);

        await viewModel.ChangePictureCommand.ExecuteAsync(null);

        Assert.Null(viewModel.PictureError);
        await downsizer.DidNotReceive().DownsizeAsync(
            Arg.Any<Stream>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
        await api.DidNotReceive().UploadAvatarAsync(
            Arg.Any<byte[]>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_picker_that_could_not_open_the_file_says_so()
    {
        var (viewModel, api, picker, _) = Build();

        picker.PickAsync(Arg.Any<CancellationToken>())
            .Returns(PhotoPickResult.Failed("That picture could not be opened."));

        await viewModel.ChangePictureCommand.ExecuteAsync(null);

        Assert.Equal("That picture could not be opened.", viewModel.PictureError);
        await api.DidNotReceive().UploadAvatarAsync(
            Arg.Any<byte[]>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_picture_is_shrunk_to_the_shared_limit_before_it_is_uploaded()
    {
        var (viewModel, api, picker, downsizer) = Build();

        var resized = new byte[] { 1, 2, 3 };

        picker.PickAsync(Arg.Any<CancellationToken>())
            .Returns(PhotoPickResult.Picked(new MemoryStream([9, 9, 9])));
        downsizer.DownsizeAsync(Arg.Any<Stream>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new DownsizedImage(resized, "image/jpeg"));
        api.UploadAvatarAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AvatarChangeResult.Ok(DateTimeOffset.UtcNow));

        await viewModel.ChangePictureCommand.ExecuteAsync(null);

        // The limit comes from Trackr.Shared, so the phone shrinks to something the server is
        // known to accept instead of finding out by being rejected.
        await downsizer.Received(1).DownsizeAsync(
            Arg.Any<Stream>(),
            AvatarRules.MaxEdgePixels,
            Arg.Any<CancellationToken>());

        // The resized bytes, not the ones that came out of the gallery.
        await api.Received(1).UploadAvatarAsync(resized, "image/jpeg", Arg.Any<CancellationToken>());

        Assert.Null(viewModel.PictureError);
        Assert.True(viewModel.HasPicture);
        Assert.Equal(resized, viewModel.Picture);
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_stops_before_the_upload()
    {
        var (viewModel, api, picker, downsizer) = Build();

        picker.PickAsync(Arg.Any<CancellationToken>())
            .Returns(PhotoPickResult.Picked(new MemoryStream([9, 9, 9])));
        downsizer.DownsizeAsync(Arg.Any<Stream>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((DownsizedImage?)null);

        await viewModel.ChangePictureCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.PictureError);
        await api.DidNotReceive().UploadAvatarAsync(
            Arg.Any<byte[]>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Something_still_over_the_cap_after_resizing_is_refused_here()
    {
        var (viewModel, api, picker, downsizer) = Build();

        picker.PickAsync(Arg.Any<CancellationToken>())
            .Returns(PhotoPickResult.Picked(new MemoryStream([9, 9, 9])));
        downsizer.DownsizeAsync(Arg.Any<Stream>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new DownsizedImage(new byte[AvatarRules.MaxBytes + 1], "image/jpeg"));

        await viewModel.ChangePictureCommand.ExecuteAsync(null);

        // The server would reject it too. Refusing here saves an upload that was always going
        // to fail, over a connection that is probably the reason it is large.
        Assert.NotNull(viewModel.PictureError);
        await api.DidNotReceive().UploadAvatarAsync(
            Arg.Any<byte[]>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_rejected_upload_reaches_the_screen_in_the_server_s_own_words()
    {
        var (viewModel, api, picker, downsizer) = Build();

        picker.PickAsync(Arg.Any<CancellationToken>())
            .Returns(PhotoPickResult.Picked(new MemoryStream([9, 9, 9])));
        downsizer.DownsizeAsync(Arg.Any<Stream>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new DownsizedImage([1, 2, 3], "image/jpeg"));
        api.UploadAvatarAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AvatarChangeResult.Failed("That image format is not supported."));

        await viewModel.ChangePictureCommand.ExecuteAsync(null);

        Assert.Equal("That image format is not supported.", viewModel.PictureError);
        Assert.False(viewModel.HasPicture);
    }

    [Fact]
    public async Task Removing_the_picture_falls_back_to_initials()
    {
        var (viewModel, api, picker, downsizer) = Build();

        picker.PickAsync(Arg.Any<CancellationToken>())
            .Returns(PhotoPickResult.Picked(new MemoryStream([9, 9, 9])));
        downsizer.DownsizeAsync(Arg.Any<Stream>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new DownsizedImage([1, 2, 3], "image/jpeg"));
        api.UploadAvatarAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AvatarChangeResult.Ok(DateTimeOffset.UtcNow));
        api.DeleteAvatarAsync(Arg.Any<CancellationToken>()).Returns(AvatarChangeResult.Ok());

        await viewModel.ChangePictureCommand.ExecuteAsync(null);
        await viewModel.RemovePictureCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasPicture);
        Assert.Equal("O", viewModel.Initials);
    }

    private static IDeviceTimeZone DeviceZone(string? id)
    {
        var zone = Substitute.For<IDeviceTimeZone>();

        zone.IanaId.Returns(id);

        return zone;
    }

    /// <summary>The same fixture, with the phone claiming to be somewhere.</summary>
    private static (ProfileViewModel Profile, ITrackrApiClient Api, AuthSession Session)
        BuildWithZone(string deviceZoneId)
    {
        var api = Substitute.For<ITrackrApiClient>();
        var tokenStore = Substitute.For<ITokenStore>();
        var serverSettings = Substitute.For<IServerSettings>();

        serverSettings.BaseUrl.Returns(new Uri("https://trackr.example.test/"));
        tokenStore.ReadAsync().Returns(new StoredTokens("access", "refresh", DateTimeOffset.MaxValue));
        api.GetMeAsync(Arg.Any<CancellationToken>()).Returns(MeResult.Ok(new MeResponse(
            Guid.NewGuid(),
            "owner@example.test",
            TwoFactorEnabled: false)));

        var cache = LocalStore.InMemory();
        var session = new AuthSession(api, tokenStore, serverSettings, cache);
        var avatars = new AvatarStore(api, session, cache);

        var profile = new ProfileViewModel(
            session,
            serverSettings,
            avatars,
            Substitute.For<IPhotoPicker>(),
            Substitute.For<IImageDownsizer>(),
            api,
            DeviceZone(deviceZoneId));

        return (profile, api, session);
    }

    /// <summary>
    /// The zone shown is the account's, not the phone's, and the difference is the whole point.
    /// </summary>
    /// <remarks>
    /// The server totals a day and every client has to agree with it. The phone's zone is offered
    /// as an answer rather than used as one - section 9.13.
    /// </remarks>
    [Fact]
    public async Task The_phones_zone_is_offered_rather_than_assumed()
    {
        var (profile, api, session) = BuildWithZone("Europe/London");

        await session.RestoreAsync();

        Assert.Equal("UTC", profile.TimeZoneDescription);
        Assert.Equal("Europe/London", profile.DeviceTimeZoneId);
        Assert.True(profile.CanUseDeviceTimeZone);

        api.SaveTimeZoneAsync(Arg.Any<SaveTimeZoneRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MeResponse(Guid.NewGuid(), "a@example.test", false, null, "Europe/London"));

        await profile.UseDeviceTimeZoneCommand.ExecuteAsync(null);

        await api.Received(1).SaveTimeZoneAsync(
            Arg.Is<SaveTimeZoneRequest>(request => request.TimeZoneId == "Europe/London"),
            Arg.Any<CancellationToken>());

        Assert.Equal("Europe/London", profile.TimeZoneDescription);
        Assert.False(profile.CanUseDeviceTimeZone);
    }

    /// <remarks>
    /// A day quietly running on the wrong clock is exactly what storing the zone on the server
    /// prevents, so a failed save must not redraw as though it had worked.
    /// </remarks>
    [Fact]
    public async Task A_zone_that_could_not_be_saved_does_not_look_saved()
    {
        var (profile, api, session) = BuildWithZone("Europe/London");

        await session.RestoreAsync();

        api.SaveTimeZoneAsync(Arg.Any<SaveTimeZoneRequest>(), Arg.Any<CancellationToken>())
            .Returns((MeResponse?)null);

        await profile.UseDeviceTimeZoneCommand.ExecuteAsync(null);

        Assert.Equal("UTC", profile.TimeZoneDescription);
        Assert.Contains("could not be saved", profile.TimeZoneProblem!, StringComparison.Ordinal);
    }

    private static (
        ProfileViewModel ViewModel,
        ITrackrApiClient Api,
        IPhotoPicker Picker,
        IImageDownsizer Downsizer) Build()
    {
        var api = Substitute.For<ITrackrApiClient>();
        var tokenStore = Substitute.For<ITokenStore>();
        var serverSettings = Substitute.For<IServerSettings>();
        var picker = Substitute.For<IPhotoPicker>();
        var downsizer = Substitute.For<IImageDownsizer>();

        serverSettings.BaseUrl.Returns(new Uri("https://trackr.example.test/"));
        tokenStore.ReadAsync().Returns(new StoredTokens("access", "refresh", DateTimeOffset.MaxValue));
        api.GetMeAsync(Arg.Any<CancellationToken>()).Returns(MeResult.Ok(new MeResponse(
            Guid.NewGuid(),
            "owner@example.test",
            TwoFactorEnabled: false)));

        var cache = LocalStore.InMemory();

        var session = new AuthSession(api, tokenStore, serverSettings, cache);
        var avatars = new AvatarStore(api, session, cache);

        session.RestoreAsync().GetAwaiter().GetResult();

        return (
            new ProfileViewModel(
                session, serverSettings, avatars, picker, downsizer, api, DeviceZone(null)),
            api,
            picker,
            downsizer);
    }
}
