using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Platform;

namespace Trackr.Mobile.Core.ViewModels;

/// <summary>
/// First run: which server does this install talk to.
/// </summary>
public sealed partial class ServerSetupViewModel(
    ITrackrApiClient api,
    IServerSettings serverSettings,
    INavigationService navigation) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial string Address { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    private bool CanConnect => !IsBusy && !string.IsNullOrWhiteSpace(Address);

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        Error = null;

        if (!TryNormalise(Address, out var baseUrl))
        {
            Error = "That does not look like a web address.";
            return;
        }

        IsBusy = true;

        try
        {
            var check = await api.CheckServerAsync(baseUrl, cancellationToken);

            if (!check.IsReachable)
            {
                Error = check.Problem;
                return;
            }

            await serverSettings.SetBaseUrlAsync(baseUrl);
            await navigation.GoToLoginAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Turns what a person types into a URL that can be used as a base address.
    /// </summary>
    /// <remarks>
    /// People type "trackr.example.com", not "https://trackr.example.com/". Two fixups:
    /// assume https when no scheme is given, and guarantee a trailing slash, because
    /// <c>new Uri(baseUrl, "api/...")</c> silently discards the last path segment without
    /// one - so "https://host/trackr" would resolve to "https://host/api/...".
    /// <para>
    /// The awkward case is a bare "scheme:" prefix, which is ambiguous: "localhost:8000" is
    /// a host and a port, while "mailto:someone@example.com" is a scheme this app cannot
    /// use. They are told apart by what follows the colon - digits mean a port. Getting this
    /// wrong is not harmless: prefixing "https://" onto the mailto form produces
    /// "https://mailto:someone@example.com", a URL that parses perfectly well with
    /// "mailto:someone" as its userinfo, and would be saved as a server address.
    /// </para>
    /// </remarks>
    internal static bool TryNormalise(string input, out Uri baseUrl)
    {
        baseUrl = null!;

        var text = input.Trim();
        if (text.Length == 0)
        {
            return false;
        }

        var scheme = SchemePrefix(text);

        if (scheme is not null)
        {
            var isHttp = scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

            if (!isHttp)
            {
                return false;
            }
        }
        else
        {
            // No scheme, so default to https. Never http: downgrading silently is the kind
            // of default that ships someone's password over plaintext.
            text = "https://" + text;
        }

        if (!text.EndsWith('/'))
        {
            text += "/";
        }

        return Uri.TryCreate(text, UriKind.Absolute, out baseUrl!)
            && (baseUrl.Scheme == Uri.UriSchemeHttps || baseUrl.Scheme == Uri.UriSchemeHttp);
    }

    /// <summary>
    /// The URI scheme the input declares, or null when it declares none.
    /// </summary>
    /// <remarks>
    /// "host:8000" is deliberately treated as declaring no scheme: a colon followed only by
    /// digits is a port, not a scheme, whatever <see cref="Uri.TryCreate"/> makes of it.
    /// </remarks>
    private static string? SchemePrefix(string text)
    {
        var colon = text.IndexOf(':');
        if (colon <= 0)
        {
            return null;
        }

        var candidate = text[..colon];

        // RFC 3986: a scheme starts with a letter, then letters, digits, '+', '-' or '.'.
        if (!char.IsAsciiLetter(candidate[0])
            || !candidate.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.'))
        {
            return null;
        }

        var rest = text[(colon + 1)..];

        // A port: "localhost:8000", possibly with a path after it.
        var afterColon = rest.Split('/')[0];
        if (afterColon.Length > 0 && afterColon.All(char.IsAsciiDigit))
        {
            return null;
        }

        return candidate;
    }
}
