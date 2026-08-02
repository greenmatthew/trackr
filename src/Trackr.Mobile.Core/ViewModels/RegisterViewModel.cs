using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Auth;
using Trackr.Shared.Auth;

namespace Trackr.Mobile.Core.ViewModels;

/// <summary>
/// Creating an account from the phone.
/// </summary>
/// <remarks>
/// <para>
/// The app deliberately owns onboarding even though every other account task - password
/// change, 2FA enrolment, minting invites - stays on the website. The case that justifies it
/// is the invited household member, who may own nothing but a phone; sending them to find a
/// desktop browser to redeem an invite is unreasonable. See CLAUDE.md section 3.
/// </para>
/// <para>
/// Two modes behind one form, decided by the server rather than the user:
/// <see cref="RegistrationMode.Bootstrap"/> on an empty database, where this account claims
/// the server, and <see cref="RegistrationMode.InviteRequired"/> afterwards, where the invite
/// field appears.
/// </para>
/// </remarks>
public sealed partial class RegisterViewModel(
    ITrackrApiClient api,
    AuthSession session,
    INavigationService navigation) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
    public partial string Email { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
    public partial string Password { get; set; } = "";

    /// <summary>The invite token, or the whole invite link - see <see cref="TryExtractInviteToken"/>.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
    public partial string InviteToken { get; set; } = "";

    /// <summary>Null until <see cref="LoadAsync"/> has asked the server.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsInvite))]
    [NotifyPropertyChangedFor(nameof(IsBootstrap))]
    [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
    public partial RegistrationMode? Mode { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>Whether to show the invite field.</summary>
    public bool NeedsInvite => Mode is RegistrationMode.InviteRequired;

    /// <summary>Whether to explain that this account will claim the server.</summary>
    public bool IsBootstrap => Mode is RegistrationMode.Bootstrap;

    private bool CanRegister =>
        !IsBusy
        && Mode is not null
        && !string.IsNullOrWhiteSpace(Email)
        && !string.IsNullOrWhiteSpace(Password)
        && (!NeedsInvite || !string.IsNullOrWhiteSpace(InviteToken));

    /// <summary>
    /// Asks the server which of the two registration paths is open.
    /// </summary>
    /// <remarks>
    /// A failed lookup assumes <see cref="RegistrationMode.InviteRequired"/> - the stricter of
    /// the two - rather than showing a form that could not possibly succeed. The web app's
    /// Register page makes the same call and the same assumption.
    /// </remarks>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            Mode = await api.GetRegistrationModeAsync(cancellationToken)
                ?? RegistrationMode.InviteRequired;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRegister))]
    private async Task RegisterAsync(CancellationToken cancellationToken)
    {
        Error = null;
        IsBusy = true;

        try
        {
            var request = new RegisterRequest
            {
                Email = Email.Trim(),
                Password = Password
            };

            if (NeedsInvite)
            {
                if (!TryExtractInviteToken(InviteToken, out var token))
                {
                    Error = "That does not look like an invite code or link.";
                    return;
                }

                request.InviteToken = token;
            }

            var created = await api.RegisterAsync(request, cancellationToken);
            if (!created.Succeeded)
            {
                Error = created.Problem ?? "Could not create the account.";
                return;
            }

            // The account exists from here on, and an invite has been spent. Everything below
            // is about getting a token for it - a failure must never read as "that did not
            // work", or the obvious response is to go and burn a second invite.
            var signedIn = await session.SignInAsync(
                new TokenRequest { Email = request.Email, Password = request.Password },
                cancellationToken);

            if (signedIn.Status is LoginStatus.Succeeded)
            {
                Password = "";
                InviteToken = "";
                // No navigation: SignInAsync raised AuthSession.Changed, and App swaps the
                // whole shell in response.
                return;
            }

            Error = "Your account was created, but signing in afterwards did not work. "
                + "Try signing in with it now - do not register again.";

            await navigation.GoToLoginAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task BackToLoginAsync() => navigation.GoToLoginAsync();

    /// <summary>
    /// Pulls the token out of whatever was pasted.
    /// </summary>
    /// <remarks>
    /// An invite is delivered as a link, so a link is what people have on their clipboard;
    /// asking them to select the token out of a URL by hand on a phone keyboard is a good way
    /// to get a truncated one. Accepts either the bare token or the whole
    /// <c>https://host/register?token=...</c> address.
    /// <para>
    /// Internal and static so it can be tested directly, like
    /// <see cref="ServerSetupViewModel.TryNormalise"/>.
    /// </para>
    /// </remarks>
    internal static bool TryExtractInviteToken(string input, out string token)
    {
        token = "";

        var text = input.Trim();
        if (text.Length == 0)
        {
            return false;
        }

        if (Uri.TryCreate(text, UriKind.Absolute, out var url)
            && url.Scheme is "http" or "https")
        {
            // Deliberately hand-parsed rather than using HttpUtility/QueryHelpers: neither is
            // available here without dragging a web dependency into a library that is linked
            // into the APK, and the shape being read is one known query string.
            foreach (var pair in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = pair.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                if (pair[..separator].Equals("token", StringComparison.OrdinalIgnoreCase))
                {
                    token = Uri.UnescapeDataString(pair[(separator + 1)..]);
                    return token.Length > 0;
                }
            }

            // A URL with no token in it is a mis-paste, not a token that happens to look
            // like a URL.
            return false;
        }

        token = text;
        return true;
    }
}
