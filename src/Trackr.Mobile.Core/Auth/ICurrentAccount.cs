using Trackr.Shared.Auth;

namespace Trackr.Mobile.Core.Auth;

/// <summary>
/// Whose account the app is currently showing.
/// </summary>
/// <remarks>
/// The read-only sliver of <see cref="AuthSession"/> that things which only need to <em>render</em>
/// the account depend on - unit preferences, a display name. Separate from the session itself so
/// that a view model needing the account's units does not acquire the ability to sign somebody out,
/// and so a test can supply one without standing up a token store and a cache.
/// </remarks>
public interface ICurrentAccount
{
    MeResponse? CurrentUser { get; }
}
