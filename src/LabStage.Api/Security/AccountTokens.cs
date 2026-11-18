using System.Net;
using System.Security.Cryptography;
using System.Text;
using LabStage.Application.Email;
using LabStage.Domain.Entities;
using LabStage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabStage.Api.Security;

/// <summary>One-time link tokens: random 32 bytes in the link, only their SHA-256 in the database.</summary>
public static class AccountTokens
{
    public static readonly TimeSpan ResetLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan ConfirmLifetime = TimeSpan.FromDays(3);
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(3);

    public static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    /// <summary>Creates a new token (older unused ones for the same purpose stop working) and returns the raw value.</summary>
    public static async Task<string> IssueAsync(LabStageDbContext db, Guid userId, string purpose, TimeSpan lifetime, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await db.UserTokens.Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.UsedAt, now), ct);
        var raw = Base64Url(RandomNumberGenerator.GetBytes(32));
        db.UserTokens.Add(new UserToken { UserId = userId, Purpose = purpose, TokenHash = Hash(raw), ExpiresAt = now.Add(lifetime) });
        return raw;
    }

    /// <summary>The valid (unused, unexpired) token for one of the purposes, with its user; null otherwise.</summary>
    public static async Task<UserToken?> FindValidAsync(LabStageDbContext db, string? raw, string[] purposes, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 100) return null;
        var hash = Hash(raw.Trim());
        var t = await db.UserTokens.Include(x => x.User).FirstOrDefaultAsync(x => x.TokenHash == hash, ct);
        return t is null || t.UsedAt is not null || t.ExpiresAt < DateTimeOffset.UtcNow || !purposes.Contains(t.Purpose) || t.User is null ? null : t;
    }

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Account emails (Arabic first, English below), built around a single action link.</summary>
public static class AccountEmails
{
    /// <summary>Public site address for links: App:PublicBaseUrl when set (always set it in production), else this request's host.</summary>
    public static string BaseUrl(HttpContext ctx, IConfiguration config)
    {
        var fixedUrl = config["App:PublicBaseUrl"];
        return !string.IsNullOrWhiteSpace(fixedUrl) ? fixedUrl.TrimEnd('/') : $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    }

    public static EmailMessage ResetPassword(AppUser u, string link) => Build(u,
        "إعادة تعيين كلمة المرور — تاج", "Reset your password — Taaj",
        "طلبت إعادة تعيين كلمة المرور لحسابك على تاج. اضغط الزر لاختيار كلمة مرور جديدة. الرابط صالح لمدة 30 دقيقة ولمرة واحدة.",
        "You asked to reset your Taaj password. Use the button to choose a new one. The link works once, for 30 minutes.",
        "اختيار كلمة مرور جديدة", "Choose a new password", link,
        "إذا لم تطلب ذلك، تجاهل هذه الرسالة وستبقى كلمة مرورك كما هي.");

    public static EmailMessage ConfirmEmail(AppUser u, string link) => Build(u,
        "تأكيد البريد الإلكتروني — تاج", "Confirm your email — Taaj",
        "مرحبًا بك في تاج. أكّد بريدك الإلكتروني ليصلك كل جديد عن حالاتك ومدفوعاتك. الرابط صالح لمدة 3 أيام.",
        "Welcome to Taaj. Confirm your email so you receive updates about your cases and payments. The link works for 3 days.",
        "تأكيد البريد الإلكتروني", "Confirm email", link,
        "إذا لم تنشئ حسابًا على تاج، تجاهل هذه الرسالة.");

    public static EmailMessage StaffInvite(AppUser u, string labName, string link) => Build(u,
        $"دعوة للانضمام إلى {labName} على تاج", $"You're invited to {labName} on Taaj",
        $"أضافك صاحب {labName} كفني في المعمل على منصة تاج. اختر كلمة مرور لتفعيل حسابك. الرابط صالح لمدة 3 أيام.",
        $"The owner of {labName} added you as a lab technician on Taaj. Choose a password to activate your account. The link works for 3 days.",
        "تفعيل الحساب واختيار كلمة المرور", "Activate & set password", link,
        "إذا لم تكن تتوقع هذه الدعوة، تجاهل هذه الرسالة.");

    private static EmailMessage Build(AppUser u, string subjectAr, string subjectEn, string bodyAr, string bodyEn, string btnAr, string btnEn, string link, string footAr)
    {
        static string e(string s) => WebUtility.HtmlEncode(s);
        var html = $"""
            <div dir="rtl" style="font-family:Tahoma,Arial,sans-serif;max-width:560px;margin:auto;color:#0B1A45">
              <h2 style="color:#0D2A78">{e(subjectAr)}</h2>
              <p>مرحبًا {e(u.FullName)}،</p>
              <p>{e(bodyAr)}</p>
              <p><a href="{e(link)}" style="display:inline-block;background:#0D2A78;color:#fff;padding:12px 22px;border-radius:999px;text-decoration:none">{e(btnAr)}</a></p>
              <p style="color:#5A6785;font-size:13px">{e(footAr)}</p>
              <hr style="border:0;border-top:1px solid #E6E9F1">
              <div dir="ltr" style="text-align:left;color:#5A6785;font-size:13px">
                <p><b>{e(subjectEn)}</b><br>{e(bodyEn)}</p>
                <p><a href="{e(link)}">{e(btnEn)}</a></p>
              </div>
            </div>
            """;
        var text = $"{subjectAr}\n\n{bodyAr}\n{link}\n\n{subjectEn}\n{bodyEn}\n{link}\n";
        return new EmailMessage(u.Email, subjectAr, html, text);
    }
}
