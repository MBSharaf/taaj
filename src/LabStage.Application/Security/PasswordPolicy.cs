namespace LabStage.Application.Security;

/// <summary>
/// The one password rule for the whole platform (sign-up, reset, change, staff invitations):
/// at least 10 characters with a letter and a digit, not a well-known password, and not built from the email address.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    // The most used passwords in public breach lists, plus local favourites (checked in lower case).
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "123456789a", "1234567890", "12345678910", "123456789q", "1q2w3e4r5t", "1qaz2wsx3edc", "qwertyuiop", "qwerty1234",
        "qwerty12345", "password1", "password12", "password123", "password1234", "passw0rd123", "p@ssw0rd123", "abc1234567",
        "abcd123456", "a123456789", "iloveyou123", "welcome123", "welcome1234", "admin12345", "admin123456", "administrator1",
        "letmein123", "monkey12345", "dragon12345", "football123", "baseball123", "superman123", "princess123", "sunshine123",
        "zaq12wsx", "zaq1zaq1zaq1", "aa123456789", "aa12345678", "q1w2e3r4t5", "q1w2e3r4t5y6", "asdfghjkl1", "1234qwerasdf",
        "egypt12345", "egypt123456", "cairo12345", "cairo123456", "mohamed123", "mohamed1234", "mohamed12345", "ahmed12345",
        "ahmed123456", "mahmoud123", "mahmoud1234", "doctor1234", "doctor12345", "dentist123", "dentist1234", "taaj123456",
        "taaj12345678", "labstage123", "labstage1234", "dental12345", "dental123456", "allah12345", "bismillah1",
    };

    /// <summary>The Arabic reason the password is refused, or null when it is acceptable.</summary>
    public static string? Problem(string? password, string? email = null)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
            return $"كلمة المرور يجب ألا تقل عن {MinLength} أحرف.";
        if (password.Length > MaxLength) return "كلمة المرور طويلة جدًا.";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            return "يجب أن تحتوي كلمة المرور على حروف وأرقام معًا.";
        if (password.Distinct().Count() < 4 || Common.Contains(password))
            return "كلمة المرور هذه شائعة وسهلة التخمين. اختر كلمة أصعب.";
        var local = (email ?? string.Empty).Split('@')[0].Trim();
        if (local.Length >= 4 && password.Contains(local, StringComparison.OrdinalIgnoreCase))
            return "لا تستخدم بريدك الإلكتروني داخل كلمة المرور.";
        return null;
    }
}
