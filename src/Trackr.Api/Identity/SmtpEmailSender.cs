using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Trackr.Api.Identity;

/// <summary>
/// Sends mail over SMTP. Selected by setting <c>Trackr:Email:Provider=Smtp</c>.
/// </summary>
/// <remarks>
/// Uses System.Net.Mail, which is in the BCL, so this costs no extra package. It is the
/// optional half of the email seam: the default <see cref="LoggingEmailSender"/> needs no
/// configuration at all, and this exists so that turning on real email later is a matter
/// of environment variables rather than code.
/// <para>
/// Not exercised by the test suite or by any verification run - there is no SMTP server in
/// this stack. If it misbehaves the failure is a logged exception on a background path,
/// not a broken login.
/// </para>
/// </remarks>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    : IEmailSender<TrackrUser>
{
    private readonly EmailOptions _options = options.Value;

    public Task SendConfirmationLinkAsync(TrackrUser user, string email, string confirmationLink) =>
        SendAsync(email, "Confirm your Trackr email address", Body("confirm your email address", confirmationLink));

    public Task SendPasswordResetLinkAsync(TrackrUser user, string email, string resetLink) =>
        SendAsync(email, "Reset your Trackr password", Body("reset your password", resetLink));

    public Task SendPasswordResetCodeAsync(TrackrUser user, string email, string resetCode) =>
        SendAsync(email, "Reset your Trackr password", $"Your password reset code is {resetCode}.");

    private static string Body(string purpose, string link) =>
        $"Use the link below to {purpose}. If you did not request this, ignore this message.\n\n{link}\n";

    private async Task SendAsync(string to, string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(_options.Host) || string.IsNullOrWhiteSpace(_options.From))
        {
            // Misconfiguration must be loud rather than a silent no-op, or a user would
            // sit waiting for a reset mail that was never going to arrive.
            logger.LogError(
                "Trackr:Email:Provider is Smtp but Host or From is not set, so no message was sent to {Email}.",
                to);
            return;
        }

        using var client = new SmtpClient(_options.Host, _options.Port) { EnableSsl = _options.UseSsl };

        if (!string.IsNullOrWhiteSpace(_options.UserName))
        {
            client.Credentials = new NetworkCredential(_options.UserName, _options.Password);
        }

        using var message = new MailMessage(_options.From, to, subject, body);

        try
        {
            await client.SendMailAsync(message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send a {Subject} message to {Email}.", subject, to);
        }
    }
}
