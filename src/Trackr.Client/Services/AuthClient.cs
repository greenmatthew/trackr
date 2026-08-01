using System.Net.Http.Json;
using Trackr.Shared.Auth;

namespace Trackr.Client.Services;

/// <summary>
/// Every call the auth and account pages make, in one place, so the components stay
/// markup plus a little state rather than markup plus HTTP plumbing.
/// </summary>
public sealed class AuthClient(HttpClient http, CookieAuthenticationStateProvider stateProvider)
{
    // --- sign-in and sign-out -------------------------------------------------------

    public async Task<ApiResult<RegistrationStatusResponse>> GetRegistrationStatusAsync()
    {
        using var response = await http.GetAsync("api/auth/registration-status");
        return await ApiResponse.ReadAsync<RegistrationStatusResponse>(response);
    }

    public async Task<ApiResult<MeResponse>> RegisterAsync(RegisterRequest request)
    {
        using var response = await http.PostAsJsonAsync("api/auth/register", request);
        var result = await ApiResponse.ReadAsync<MeResponse>(response);

        if (result.Succeeded)
        {
            // Registering signs you in, so the app's auth state has just changed.
            stateProvider.Invalidate();
        }

        return result;
    }

    public Task<ApiResult<LoginResponse>> LoginAsync(LoginRequest request) =>
        PostLoginAsync("api/auth/login", request);

    public Task<ApiResult<LoginResponse>> LoginTwoFactorAsync(TwoFactorLoginRequest request) =>
        PostLoginAsync("api/auth/login/2fa", request);

    public Task<ApiResult<LoginResponse>> LoginRecoveryCodeAsync(RecoveryCodeLoginRequest request) =>
        PostLoginAsync("api/auth/login/recovery-code", request);

    /// <remarks>
    /// The login endpoints answer 401 with a <see cref="LoginResponse"/> body rather than a
    /// problem document, because "wrong password" and "you owe a 2FA code" are normal
    /// outcomes the page needs to branch on, not errors to display verbatim.
    /// </remarks>
    private async Task<ApiResult<LoginResponse>> PostLoginAsync<TRequest>(string uri, TRequest request)
    {
        using var response = await http.PostAsJsonAsync(uri, request);

        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized)
        {
            var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
            if (body is not null)
            {
                return new ApiResult<LoginResponse>(body, null);
            }
        }

        var result = await ApiResponse.ReadAsync<LoginResponse>(response);

        if (result is { Succeeded: true, Value.Status: LoginStatus.Succeeded })
        {
            stateProvider.Invalidate();
        }

        return result;
    }

    public async Task LogoutAsync()
    {
        // A 401 here just means the session had already gone; either way we end up
        // signed out, so there is nothing to report.
        using var response = await http.PostAsync("api/auth/logout", content: null);
        stateProvider.Invalidate();
    }

    // --- password recovery ----------------------------------------------------------

    public async Task<ApiResult<object>> ForgotPasswordAsync(ForgotPasswordRequest request)
    {
        using var response = await http.PostAsJsonAsync("api/auth/forgot-password", request);
        return await ApiResponse.ReadAsync<object>(response);
    }

    public async Task<ApiResult<object>> ResetPasswordAsync(ResetPasswordRequest request)
    {
        using var response = await http.PostAsJsonAsync("api/auth/reset-password", request);
        return await ApiResponse.ReadAsync<object>(response);
    }

    // --- account settings -----------------------------------------------------------

    public async Task<ApiResult<object>> ChangePasswordAsync(ChangePasswordRequest request)
    {
        using var response = await http.PostAsJsonAsync("api/account/password", request);
        return await ApiResponse.ReadAsync<object>(response);
    }

    public async Task<ApiResult<TwoFactorStatusResponse>> GetTwoFactorStatusAsync()
    {
        using var response = await http.GetAsync("api/account/2fa");
        return await ApiResponse.ReadAsync<TwoFactorStatusResponse>(response);
    }

    public async Task<ApiResult<TwoFactorEnrollmentResponse>> EnrollTwoFactorAsync()
    {
        using var response = await http.PostAsync("api/account/2fa/enroll", content: null);
        return await ApiResponse.ReadAsync<TwoFactorEnrollmentResponse>(response);
    }

    public async Task<ApiResult<RecoveryCodesResponse>> EnableTwoFactorAsync(TwoFactorCodeRequest request)
    {
        using var response = await http.PostAsJsonAsync("api/account/2fa/enable", request);
        return await ApiResponse.ReadAsync<RecoveryCodesResponse>(response);
    }

    public async Task<ApiResult<object>> DisableTwoFactorAsync(DisableTwoFactorRequest request)
    {
        using var response = await http.PostAsJsonAsync("api/account/2fa/disable", request);
        return await ApiResponse.ReadAsync<object>(response);
    }

    public async Task<ApiResult<RecoveryCodesResponse>> RegenerateRecoveryCodesAsync()
    {
        using var response = await http.PostAsync("api/account/2fa/recovery-codes", content: null);
        return await ApiResponse.ReadAsync<RecoveryCodesResponse>(response);
    }

    // --- invites --------------------------------------------------------------------

    public async Task<ApiResult<InviteCreatedResponse>> CreateInviteAsync(CreateInviteRequest request)
    {
        using var response = await http.PostAsJsonAsync("api/invites", request);
        return await ApiResponse.ReadAsync<InviteCreatedResponse>(response);
    }

    public async Task<ApiResult<InviteResponse[]>> ListInvitesAsync()
    {
        using var response = await http.GetAsync("api/invites");
        return await ApiResponse.ReadAsync<InviteResponse[]>(response);
    }

    public async Task<ApiResult<object>> RevokeInviteAsync(Guid id)
    {
        using var response = await http.DeleteAsync($"api/invites/{id}");
        return await ApiResponse.ReadAsync<object>(response);
    }
}
