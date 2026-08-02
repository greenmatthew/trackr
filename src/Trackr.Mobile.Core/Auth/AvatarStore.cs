using Trackr.Mobile.Core.Api;

namespace Trackr.Mobile.Core.Auth;

/// <summary>
/// The account's profile picture, held once for everything that draws it.
/// </summary>
/// <remarks>
/// Two places show the avatar - the title bar on every signed-in tab, and the profile screen -
/// and they must agree the instant it changes. A singleton holding the bytes is what makes
/// that true without either view model knowing the other exists.
/// <para>
/// It is also where the picture is fetched and replaced, rather than in a view model, so the
/// rule that the marker and the bytes move together lives in one class. Milestone 5's SQLite
/// store slots in behind this same surface: <see cref="EnsureLoadedAsync"/> gains a read from
/// disk before it considers a request, and nothing that draws an avatar changes.
/// </para>
/// </remarks>
public sealed class AvatarStore
{
    private readonly ITrackrApiClient _api;
    private readonly AuthSession _session;

    /// <summary>The marker the held bytes correspond to, so a stale copy can be spotted.</summary>
    private DateTimeOffset? _heldMarker;

    public AvatarStore(ITrackrApiClient api, AuthSession session)
    {
        _api = api;
        _session = session;

        // Signing out has to drop the bytes, not merely stop drawing them. The next account
        // on this device gets a fresh shell either way, but a previous user's photograph
        // sitting in memory is exactly the sort of thing CLAUDE.md section 8 asks care about.
        _session.Changed += OnSessionChanged;
    }

    /// <summary>The picture, or null when the account has none and initials are shown instead.</summary>
    public byte[]? Content { get; private set; }

    /// <summary>The server's tag for <see cref="Content"/>, for conditional re-fetching.</summary>
    public string? ETag { get; private set; }

    public bool HasPicture => Content is not null;

    /// <summary>Raised when <see cref="Content"/> changes, so both avatars redraw together.</summary>
    public event Action? Changed;

    /// <summary>
    /// Makes sure the held picture matches the signed-in account, fetching it if not.
    /// </summary>
    /// <remarks>
    /// Cheap to call repeatedly: the marker on the session says whether the server's copy has
    /// moved, so the common case costs a comparison rather than a request.
    /// <para>
    /// A failure here is deliberately silent. The fallback is the initials circle, which is
    /// visibly not-a-photograph, so the user is not being shown something wrong - and the rule
    /// against swallowing errors (CLAUDE.md section 5) is about numbers going into the log, not
    /// about decoration failing to load.
    /// </para>
    /// </remarks>
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        var marker = _session.CurrentUser?.AvatarUpdatedUtc;

        if (marker is null)
        {
            Forget();

            return;
        }

        if (_heldMarker == marker && Content is not null)
        {
            return;
        }

        var result = await _api.GetAvatarAsync(ETag, cancellationToken);

        switch (result.Status)
        {
            case AvatarFetchStatus.Fetched:
                Content = result.Content;
                ETag = result.ETag;
                _heldMarker = marker;
                Changed?.Invoke();
                break;

            case AvatarFetchStatus.Unchanged:
                // Nothing to redraw; only the record of what the held bytes are current
                // against needs moving forward, so the next check does not ask again.
                _heldMarker = marker;
                break;

            case AvatarFetchStatus.None:
                // The picture was removed from another device.
                Forget();
                break;

            case AvatarFetchStatus.Failed:
                // Keep whatever is held. Retried on the next call.
                break;
        }
    }

    /// <summary>Uploads a new picture and, on success, shows it immediately.</summary>
    public async Task<AvatarChangeResult> ReplaceAsync(
        byte[] content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var result = await _api.UploadAvatarAsync(content, contentType, cancellationToken);

        if (!result.Succeeded)
        {
            return result;
        }

        Content = content;

        // No tag: the upload's response carries the marker but not the ETag the GET would
        // have sent, and deriving one from the marker would mean encoding the server's tag
        // format here. Null simply means the next conditional request is unconditional.
        ETag = null;
        _heldMarker = result.UpdatedUtc;

        // The session's copy of the marker is now stale, and EnsureLoadedAsync compares
        // against it - so without this the very next check would decide the freshly uploaded
        // picture was the wrong one.
        _session.NoteAvatarChanged(result.UpdatedUtc);

        Changed?.Invoke();

        return result;
    }

    /// <summary>Removes the picture, falling back to initials.</summary>
    public async Task<AvatarChangeResult> RemoveAsync(CancellationToken cancellationToken = default)
    {
        var result = await _api.DeleteAvatarAsync(cancellationToken);

        if (!result.Succeeded)
        {
            return result;
        }

        _session.NoteAvatarChanged(null);
        Forget();

        return result;
    }

    private void OnSessionChanged()
    {
        if (!_session.IsSignedIn)
        {
            Forget();
        }
    }

    private void Forget()
    {
        _heldMarker = null;

        if (Content is null && ETag is null)
        {
            return;
        }

        Content = null;
        ETag = null;
        Changed?.Invoke();
    }
}
