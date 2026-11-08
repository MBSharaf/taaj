using System.Security.Cryptography;

namespace LabStage.Application.Security;

/// <summary>
/// PBKDF2-SHA256 password hasher with per-user random salt and constant-time comparison.
/// </summary>
public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    /// <summary>PBKDF2-SHA256 work factor (OWASP 2023 recommendation: 600,000).</summary>
    public const int Iterations = 600_000;

    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"v1.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    /// <summary>True when the stored hash uses fewer iterations than today's setting (upgrade it at next sign-in).</summary>
    public static bool NeedsRehash(string storedHash)
    {
        var parts = storedHash?.Split('.') ?? [];
        return parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var iterations) || iterations < Iterations;
    }

    public static bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(storedHash))
            return false;

        var parts = storedHash.Split('.');
        if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var iterations))
            return false;

        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
