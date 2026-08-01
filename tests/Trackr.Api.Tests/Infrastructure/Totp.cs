using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Trackr.Api.Tests.Infrastructure;

/// <summary>
/// Computes the code an authenticator app would be showing right now.
/// </summary>
/// <remarks>
/// Needed because Identity's <c>AuthenticatorTokenProvider.GenerateAsync</c> deliberately
/// returns an empty string - the whole point of the provider is that only the user's phone
/// can produce a code, so it can validate but not generate. Standing in for that phone is
/// the test's job.
/// <para>
/// This is RFC 6238 with the parameters Identity's <c>Rfc6238AuthenticationService</c>
/// uses: HMAC-SHA1, a 30-second step, six digits and no modifier. It is test-only code -
/// nothing in the application computes a TOTP by hand.
/// </para>
/// </remarks>
internal static class Totp
{
    private const int TimeStepSeconds = 30;
    private const int Digits = 1_000_000;

    public static string Generate(string base32Key)
    {
        var key = FromBase32(base32Key);
        var counter = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / TimeStepSeconds);

        Span<byte> counterBytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(counterBytes, counter);

        Span<byte> hash = stackalloc byte[HMACSHA1.HashSizeInBytes];
        HMACSHA1.HashData(key, counterBytes, hash);

        // Dynamic truncation, RFC 4226 section 5.3.
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
            | (hash[offset + 1] << 16)
            | (hash[offset + 2] << 8)
            | hash[offset + 3];

        return (binary % Digits).ToString("D6");
    }

    private static byte[] FromBase32(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        var cleaned = input.Replace(" ", "").Replace("=", "").ToUpperInvariant();
        var output = new List<byte>(cleaned.Length * 5 / 8);
        var buffer = 0;
        var bitsInBuffer = 0;

        foreach (var character in cleaned)
        {
            var index = alphabet.IndexOf(character);
            if (index < 0)
            {
                throw new FormatException($"'{character}' is not a base32 character.");
            }

            buffer = (buffer << 5) | index;
            bitsInBuffer += 5;

            if (bitsInBuffer >= 8)
            {
                output.Add((byte)(buffer >> (bitsInBuffer - 8)));
                bitsInBuffer -= 8;
            }
        }

        return [.. output];
    }
}
