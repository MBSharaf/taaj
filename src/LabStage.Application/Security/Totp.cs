using System.Security.Cryptography;
using System.Text;

namespace LabStage.Application.Security;

/// <summary>
/// Time-based one-time codes (RFC 6238: HMAC-SHA1, 6 digits, 30-second steps), the codes shown by Google Authenticator,
/// Microsoft Authenticator, 1Password and similar apps.
/// </summary>
public static class Totp
{
    public const int Digits = 6;
    public const int StepSeconds = 30;
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>A new random 160-bit secret, Base32 encoded (what the authenticator app stores).</summary>
    public static string NewSecret() => ToBase32(RandomNumberGenerator.GetBytes(20));

    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / StepSeconds;

    public static string Code(string base32Secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        for (var i = 7; i >= 0; i--) { counter[i] = (byte)(step & 0xff); step >>= 8; }
        var hash = HMACSHA1.HashData(FromBase32(base32Secret), counter);
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    /// <summary>
    /// Checks a code against the current step and one step either side (clock drift). A step at or before
    /// <paramref name="lastUsedStep"/> is refused, so a code works only once. Returns the matched step, or null.
    /// </summary>
    public static long? Verify(string base32Secret, string? code, DateTimeOffset now, long lastUsedStep)
    {
        code = new string((code ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (code.Length != Digits) return null;
        var current = StepAt(now);
        for (var s = current - 1; s <= current + 1; s++)
        {
            if (s <= lastUsedStep) continue;
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(base32Secret, s)), Encoding.ASCII.GetBytes(code)))
                return s;
        }
        return null;
    }

    /// <summary>The otpauth:// link an authenticator app reads from the QR code.</summary>
    public static string SetupUri(string issuer, string account, string base32Secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={base32Secret}&issuer={Uri.EscapeDataString(issuer)}&digits={Digits}&period={StepSeconds}";

    /// <summary>Ten one-time recovery codes such as "7KQ4-M2XP", for when the phone is lost.</summary>
    public static string[] NewRecoveryCodes(int count = 10) =>
        Enumerable.Range(0, count).Select(_ =>
        {
            var b = RandomNumberGenerator.GetBytes(5);
            var s = ToBase32(b); // 8 characters
            return $"{s[..4]}-{s[4..8]}";
        }).ToArray();

    public static string HashRecoveryCode(string code) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeRecoveryCode(code))));

    public static string NormalizeRecoveryCode(string code) =>
        new string(code.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static string ToBase32(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b; bits += 8;
            while (bits >= 5) { sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[] FromBase32(string s)
    {
        s = s.TrimEnd('=').Replace(" ", "").ToUpperInvariant();
        var output = new List<byte>(s.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in s)
        {
            var v = Alphabet.IndexOf(c);
            if (v < 0) throw new FormatException("Not Base32.");
            buffer = (buffer << 5) | v; bits += 5;
            if (bits >= 8) { output.Add((byte)((buffer >> (bits - 8)) & 0xff)); bits -= 8; }
        }
        return [.. output];
    }
}
