using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Trackr.Api.Identity;

/// <summary>
/// Generating and hashing registration invite tokens.
/// </summary>
/// <remarks>
/// Shared by the endpoint that mints an invite and the one that redeems it, so the two
/// can never disagree about the hash format.
/// </remarks>
public static class InviteTokens
{
    /// <summary>How many characters of the raw token are kept in the clear for display.</summary>
    public const int PrefixLength = 8;

    /// <summary>
    /// A fresh 256-bit token, base64url encoded (43 characters, safe in a URL).
    /// </summary>
    public static string Create() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Uppercase hex SHA-256 of a token.
    /// </summary>
    /// <remarks>
    /// A plain fast hash rather than a KDF, unlike a password. The token is 256 bits of
    /// CSPRNG output, so there is no low-entropy guess to grind against and stretching
    /// would buy nothing.
    /// </remarks>
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static string Prefix(string token) =>
        token.Length <= PrefixLength ? token : token[..PrefixLength];
}
