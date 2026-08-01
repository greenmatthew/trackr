using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trackr.Mobile.Core.Auth;
using Trackr.Mobile.Core.Platform;
using Trackr.Shared.Auth;

namespace Trackr.Mobile.Core.ViewModels;

/// <summary>
/// Sign-in, including the second factor.
/// </summary>
/// <remarks>
/// One screen for both steps, which follows from the API shape: <c>/api/auth/token</c> takes
/// an optional code and re-checks the password, so there is no server-side challenge to keep
/// alive between two screens. <see cref="NeedsTwoFactor"/> just reveals the code field.
/// </remarks>
public sealed partial class LoginViewModel(
    AuthSession session,
    IServerSettings serverSettings,
    INavigationService navigation) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial string Email { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial string Password { get; set; } = "";

    /// <summary>The authenticator code, or a recovery code. Only shown once the server asks.</summary>
    [ObservableProperty]
    public partial string TwoFactorCode { get; set; } = "";

    /// <summary>Whether the password step reported that a code is still owed.</summary>
    [ObservableProperty]
    public partial bool NeedsTwoFactor { get; set; }

    /// <summary>Treat <see cref="TwoFactorCode"/> as one of the single-use recovery codes.</summary>
    [ObservableProperty]
    public partial bool UseRecoveryCode { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>Shown so it is obvious which server is about to be signed in to.</summary>
    public string ServerDescription => serverSettings.BaseUrl?.Host ?? "no server configured";

    private bool CanSignIn =>
        !IsBusy && !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync(CancellationToken cancellationToken)
    {
        Error = null;
        IsBusy = true;

        try
        {
            var request = new TokenRequest
            {
                Email = Email.Trim(),
                Password = Password
            };

            // Only attached once the server has actually asked, so a first attempt never
            // sends a stale code left in the box.
            if (NeedsTwoFactor && !string.IsNullOrWhiteSpace(TwoFactorCode))
            {
                if (UseRecoveryCode)
                {
                    request.TwoFactorRecoveryCode = TwoFactorCode;
                }
                else
                {
                    request.TwoFactorCode = TwoFactorCode;
                }
            }

            var result = await session.SignInAsync(request, cancellationToken);

            switch (result.Status)
            {
                case LoginStatus.Succeeded:
                    Password = "";
                    TwoFactorCode = "";
                    NeedsTwoFactor = false;
                    await navigation.GoToHomeAsync();
                    break;

                case LoginStatus.RequiresTwoFactor:
                    // Not an error. The password was right; reveal the code field.
                    NeedsTwoFactor = true;
                    Error = null;
                    break;

                case LoginStatus.LockedOut:
                    Error = result.LockoutEndUtc is { } until
                        ? $"Too many failed attempts. Try again after {until.ToLocalTime():HH:mm}."
                        : "Too many failed attempts. Try again later.";
                    break;

                default:
                    Error = result.Problem
                        ?? (NeedsTwoFactor
                            ? "That code was not accepted."
                            : "That email and password did not match.");
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Abandons this server and returns to first-run setup.</summary>
    [RelayCommand]
    private async Task ChangeServerAsync()
    {
        await serverSettings.ClearAsync();
        await navigation.GoToServerSetupAsync();
    }

    partial void OnUseRecoveryCodeChanged(bool value) => TwoFactorCode = "";
}
