using Microsoft.AspNetCore.Identity;

namespace Trackr.Api.Identity;

/// <summary>
/// Writes what would have been emailed to the application log.
/// </summary>
/// <remarks>
/// This is the default, and for this deployment it is the actual password-recovery
/// mechanism: forget your password, hit "forgot password", then read the link out of
/// `docker compose logs backend`. It keeps SMTP credentials and an external dependency
/// out of a private single-user tool entirely.
/// <para>
/// The trade-off is real and is called out in the README: anyone who can read the backend
/// logs can take over an account. On a private server whose logs only the owner can read,
/// that is the same trust boundary as the database itself. Configure
/// <c>Trackr:Email:Provider=Smtp</c> to send real mail instead.
/// </para>
/// Logged at Warning so it stands out in a stream of Information-level request logs.
/// </remarks>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender<TrackrUser>
{
    public Task SendConfirmationLinkAsync(TrackrUser user, string email, string confirmationLink)
    {
        Log(email, "confirm your email address", confirmationLink);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetLinkAsync(TrackrUser user, string email, string resetLink)
    {
        Log(email, "reset your password", resetLink);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetCodeAsync(TrackrUser user, string email, string resetCode)
    {
        Log(email, "reset your password (code)", resetCode);
        return Task.CompletedTask;
    }

    private void Log(string email, string purpose, string link) =>
        logger.LogWarning(
            "No email provider is configured, so the link below was not sent. " +
            "Give it to {Email} to {Purpose}:\n\n    {Link}\n",
            email,
            purpose,
            link);
}
