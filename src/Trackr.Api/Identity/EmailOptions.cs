namespace Trackr.Api.Identity;

/// <summary>How outbound mail (currently only password-reset links) is delivered.</summary>
public enum EmailProvider
{
    /// <summary>
    /// Write the link to the application log. The default, and a workable recovery path
    /// for a private self-hosted tool: `docker compose logs backend`. Requires no
    /// credentials and no external dependency.
    /// </summary>
    Log,

    /// <summary>Send over SMTP using the settings below.</summary>
    Smtp
}

/// <summary>Bound from the <c>Trackr:Email</c> configuration section.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Trackr:Email";

    public EmailProvider Provider { get; set; } = EmailProvider.Log;

    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? From { get; set; }

    /// <summary>
    /// Base URL to build links against, e.g. <c>https://trackr.example.com</c>. Leave
    /// unset to derive it from the incoming request, which is correct as long as the
    /// reverse proxy forwards Host and X-Forwarded-Proto (it does - see nginx.conf).
    /// Set it explicitly if links ever come out with the wrong host.
    /// </summary>
    public string? PublicBaseUrl { get; set; }
}
