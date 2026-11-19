using System.Security.Claims;
using LabStage.Api.Security;
using LabStage.Application.Email;
using LabStage.Application.Security;
using LabStage.Domain.Entities;
using LabStage.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace LabStage.Api.Controllers;

/// <summary>Password reset, email confirmation, change password, profile and lab staff accounts.</summary>
[ApiController]
public sealed class AccountController(LabStageDbContext db, IEmailSender email, IConfiguration config, ILogger<AccountController> log) : ControllerBase
{
    public const int MinPasswordLength = PasswordPolicy.MinLength;
    private const string LinkInvalid = "الرابط غير صالح أو انتهت صلاحيته. اطلب رابطًا جديدًا.";

    public sealed record ForgotRequest(string Email);
    public sealed record ResetRequest(string Token, string NewPassword);
    public sealed record TokenRequest(string Token);
    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
    public sealed record ProfileRequest(string FullName, string? Phone);
    public sealed record StaffRequest(string FullName, string Email, string? Phone);

    // ---------------- forgot / reset ----------------

    /// <summary>Always the same answer, whether or not the email has an account (no user enumeration).</summary>
    [EnableRateLimiting("auth")]
    [HttpPost("api/v1/auth/forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotRequest req, CancellationToken ct)
    {
        var norm = (req.Email ?? string.Empty).Trim().ToUpperInvariant();
        var user = norm.Length > 0 ? await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == norm && !u.IsSuspended, ct) : null;
        if (user is not null)
        {
            var raw = await AccountTokens.IssueAsync(db, user.Id, UserToken.ResetPassword, AccountTokens.ResetLifetime, ct);
            await db.SaveChangesAsync(ct);
            await SendSafelyAsync(AccountEmails.ResetPassword(user, $"{AccountEmails.BaseUrl(HttpContext, config)}/reset-password.html?token={Uri.EscapeDataString(raw)}"), ct);
        }
        return Ok(new { sent = true });
    }

    /// <summary>Sets a new password from a reset link or a staff invitation link; every other session is signed out.</summary>
    [EnableRateLimiting("auth")]
    [HttpPost("api/v1/auth/reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetRequest req, CancellationToken ct)
    {
        if (PasswordProblem(req.NewPassword) is { } bad) return bad;
        var t = await AccountTokens.FindValidAsync(db, req.Token, [UserToken.ResetPassword, UserToken.StaffInvite], ct);
        if (t is null || t.User!.IsSuspended) return Problem(LinkInvalid, statusCode: StatusCodes.Status400BadRequest);

        var u = t.User;
        u.PasswordHash = PasswordHasher.Hash(req.NewPassword);
        u.SecurityStamp = Guid.NewGuid().ToString("N");
        u.EmailConfirmedAt ??= DateTimeOffset.UtcNow; // the link reached this inbox
        t.UsedAt = DateTimeOffset.UtcNow;
        db.AddUserAudit(u, t.Purpose == UserToken.StaffInvite ? "StaffInviteAccepted" : "PasswordReset");
        await db.SaveChangesAsync(ct);
        return Ok(new { reset = true, email = u.Email });
    }

    // ---------------- email confirmation ----------------

    [EnableRateLimiting("auth")]
    [HttpPost("api/v1/auth/confirm-email")]
    public async Task<IActionResult> ConfirmEmail([FromBody] TokenRequest req, CancellationToken ct)
    {
        var t = await AccountTokens.FindValidAsync(db, req.Token, [UserToken.ConfirmEmail], ct);
        if (t is null) return Problem(LinkInvalid, statusCode: StatusCodes.Status400BadRequest);
        t.UsedAt = DateTimeOffset.UtcNow;
        t.User!.EmailConfirmedAt ??= DateTimeOffset.UtcNow;
        db.AddUserAudit(t.User, "EmailConfirmed");
        await db.SaveChangesAsync(ct);
        return Ok(new { confirmed = true });
    }

    [Authorize]
    [EnableRateLimiting("auth")]
    [HttpPost("api/v1/account/resend-confirmation")]
    public async Task<IActionResult> ResendConfirmation(CancellationToken ct)
    {
        var u = await db.Users.FirstAsync(x => x.Id == User.GetUserId(), ct);
        if (u.EmailConfirmedAt is not null) return Ok(new { confirmed = true });
        await SendConfirmationAsync(db, email, config, HttpContext, u, ct);
        return Ok(new { sent = true });
    }

    /// <summary>Used by registration too: issues a confirmation token and emails the link.</summary>
    public static async Task SendConfirmationAsync(LabStageDbContext db, IEmailSender email, IConfiguration config, HttpContext ctx, AppUser u, CancellationToken ct)
    {
        var raw = await AccountTokens.IssueAsync(db, u.Id, UserToken.ConfirmEmail, AccountTokens.ConfirmLifetime, ct);
        await db.SaveChangesAsync(ct);
        try { await email.SendAsync(AccountEmails.ConfirmEmail(u, $"{AccountEmails.BaseUrl(ctx, config)}/confirm-email.html?token={Uri.EscapeDataString(raw)}"), ct); }
        catch (Exception ex) { ctx.RequestServices.GetRequiredService<ILogger<AccountController>>().LogError(ex, "Confirmation email to {User} failed", u.Id); }
    }

    // ---------------- signed-in account ----------------

    [Authorize]
    [HttpGet("api/v1/account/profile")]
    public async Task<IActionResult> Profile(CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking().FirstAsync(x => x.Id == User.GetUserId(), ct);
        return Ok(new { u.FullName, u.Email, u.Phone, u.Role, EmailConfirmed = u.EmailConfirmedAt != null, PhoneConfirmed = u.PhoneConfirmedAt != null, u.CreatedAt });
    }

    [Authorize]
    [HttpPut("api/v1/account/profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] ProfileRequest req, CancellationToken ct)
    {
        var name = (req.FullName ?? string.Empty).Trim();
        var phone = (req.Phone ?? string.Empty).Trim();
        if (name.Length is < 2 or > 200) return Problem("اكتب الاسم الكامل.", statusCode: StatusCodes.Status400BadRequest);
        if (phone.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(phone, @"^\+?[0-9 ]{8,20}$"))
            return Problem("رقم الهاتف غير صحيح.", statusCode: StatusCodes.Status400BadRequest);
        var u = await db.Users.FirstAsync(x => x.Id == User.GetUserId(), ct);
        if (u.Phone != phone) u.PhoneConfirmedAt = null; // a new number needs confirming again
        u.FullName = name;
        u.Phone = phone;
        db.AddUserAudit(u, "ProfileUpdated");
        await db.SaveChangesAsync(ct);
        return Ok(new { u.FullName, u.Phone });
    }

    [Authorize]
    [EnableRateLimiting("auth")]
    [HttpPost("api/v1/account/change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req, CancellationToken ct)
    {
        if (PasswordProblem(req.NewPassword, User.FindFirstValue(ClaimTypes.Email)) is { } bad) return bad;
        var u = await db.Users.FirstAsync(x => x.Id == User.GetUserId(), ct);
        if (!PasswordHasher.Verify(req.CurrentPassword ?? string.Empty, u.PasswordHash))
            return Problem("كلمة المرور الحالية غير صحيحة.", statusCode: StatusCodes.Status400BadRequest);
        if (PasswordHasher.Verify(req.NewPassword, u.PasswordHash))
            return Problem("اختر كلمة مرور مختلفة عن الحالية.", statusCode: StatusCodes.Status400BadRequest);
        u.PasswordHash = PasswordHasher.Hash(req.NewPassword);
        u.SecurityStamp = Guid.NewGuid().ToString("N");
        db.AddUserAudit(u, "PasswordChanged");
        await db.SaveChangesAsync(ct);
        // Other devices are signed out by the new stamp; this one gets a fresh cookie.
        await AuthController.SignInAsync(HttpContext, u, isPersistent: true);
        return Ok(new { changed = true });
    }

    // ---------------- lab staff (owner only) ----------------

    [Authorize(Roles = AppRoles.LaboratoryOwner)]
    [HttpGet("api/v1/labs/my/staff")]
    public async Task<IActionResult> Staff(CancellationToken ct)
    {
        var lab = await OwnLabAsync(ct);
        if (lab is null) return NotFound();
        var items = await (from s in db.LaboratoryStaff
                           join u in db.Users on s.UserId equals u.Id
                           where s.LaboratoryId == lab.Id
                           orderby u.FullName
                           select new { s.UserId, u.FullName, u.Email, u.Phone, Active = u.EmailConfirmedAt != null && !u.IsSuspended, u.IsSuspended, u.CreatedAt })
                          .ToListAsync(ct);
        return Ok(new { items });
    }

    [Authorize(Roles = AppRoles.LaboratoryOwner)]
    [EnableRateLimiting("auth")]
    [HttpPost("api/v1/labs/my/staff")]
    public async Task<IActionResult> AddStaff([FromBody] StaffRequest req, CancellationToken ct)
    {
        var lab = await OwnLabAsync(ct);
        if (lab is null) return NotFound();
        var name = (req.FullName ?? string.Empty).Trim();
        var mail = (req.Email ?? string.Empty).Trim();
        if (name.Length < 2) return Problem("اكتب اسم الفني.", statusCode: StatusCodes.Status400BadRequest);
        if (!System.Text.RegularExpressions.Regex.IsMatch(mail, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            return Problem("أدخل بريدًا إلكترونيًا صحيحًا.", statusCode: StatusCodes.Status400BadRequest);
        var norm = mail.ToUpperInvariant();
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == norm, ct))
            return Problem("هذا البريد مسجّل بالفعل بحساب آخر على تاج.", statusCode: StatusCodes.Status409Conflict);

        // No usable password until the technician opens the invitation link.
        var u = new AppUser
        {
            Email = mail, NormalizedEmail = norm, FullName = name, Phone = (req.Phone ?? string.Empty).Trim(),
            Role = AppRoles.LaboratoryStaff, PasswordHash = PasswordHasher.Hash(Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))),
        };
        db.Users.Add(u);
        db.LaboratoryStaff.Add(new LaboratoryStaff { LaboratoryId = lab.Id, UserId = u.Id });
        db.AddUserAudit(u, "StaffInvited", User.GetUserId(), AppRoles.LaboratoryOwner);
        var raw = await AccountTokens.IssueAsync(db, u.Id, UserToken.StaffInvite, AccountTokens.InviteLifetime, ct);
        await db.SaveChangesAsync(ct);
        await SendSafelyAsync(AccountEmails.StaffInvite(u, lab.Name, $"{AccountEmails.BaseUrl(HttpContext, config)}/reset-password.html?invite=1&token={Uri.EscapeDataString(raw)}"), ct);
        return Ok(new { u.Id, u.FullName, u.Email, invited = true });
    }

    [Authorize(Roles = AppRoles.LaboratoryOwner)]
    [EnableRateLimiting("auth")]
    [HttpPost("api/v1/labs/my/staff/{userId:guid}/resend")]
    public async Task<IActionResult> ResendInvite([FromRoute] Guid userId, CancellationToken ct)
    {
        var lab = await OwnLabAsync(ct);
        var link = lab is null ? null : await db.LaboratoryStaff.FirstOrDefaultAsync(s => s.LaboratoryId == lab.Id && s.UserId == userId, ct);
        var staffUser = link is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (staffUser is null) return NotFound();
        if (staffUser.EmailConfirmedAt is not null) return Problem("هذا الفني فعّل حسابه بالفعل.", statusCode: StatusCodes.Status400BadRequest);
        var raw = await AccountTokens.IssueAsync(db, userId, UserToken.StaffInvite, AccountTokens.InviteLifetime, ct);
        await db.SaveChangesAsync(ct);
        await SendSafelyAsync(AccountEmails.StaffInvite(staffUser, lab!.Name, $"{AccountEmails.BaseUrl(HttpContext, config)}/reset-password.html?invite=1&token={Uri.EscapeDataString(raw)}"), ct);
        return Ok(new { sent = true });
    }

    /// <summary>Removes a technician from the lab: the account is closed and signed out everywhere.</summary>
    [Authorize(Roles = AppRoles.LaboratoryOwner)]
    [HttpDelete("api/v1/labs/my/staff/{userId:guid}")]
    public async Task<IActionResult> RemoveStaff([FromRoute] Guid userId, CancellationToken ct)
    {
        var lab = await OwnLabAsync(ct);
        var link = lab is null ? null : await db.LaboratoryStaff.FirstOrDefaultAsync(s => s.LaboratoryId == lab.Id && s.UserId == userId, ct);
        var staffUser = link is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (staffUser is null) return NotFound();
        var u = staffUser;
        db.LaboratoryStaff.Remove(link!);
        u.IsSuspended = true;
        u.SuspensionReason = "تمت إزالة الحساب من المعمل.";
        u.SecurityStamp = Guid.NewGuid().ToString("N");
        await db.UserTokens.Where(t => t.UserId == u.Id && t.UsedAt == null).ExecuteUpdateAsync(x => x.SetProperty(t => t.UsedAt, DateTimeOffset.UtcNow), ct);
        db.AddUserAudit(u, "StaffRemoved", User.GetUserId(), AppRoles.LaboratoryOwner);
        await db.SaveChangesAsync(ct);
        return Ok(new { removed = true });
    }

    // ---------------- helpers ----------------

    private Task<Laboratory?> OwnLabAsync(CancellationToken ct)
    {
        var id = User.GetUserId();
        return db.Laboratories.FirstOrDefaultAsync(l => l.OwnerUserId == id, ct);
    }

    private ObjectResult? PasswordProblem(string? pw, string? email = null) =>
        PasswordPolicy.Problem(pw, email) is { } weak ? Problem(weak, statusCode: StatusCodes.Status400BadRequest) : null;


    /// <summary>A failing provider must not reveal anything or break the request; it is logged instead.</summary>
    private async Task SendSafelyAsync(EmailMessage m, CancellationToken ct)
    {
        try { await email.SendAsync(m, ct); }
        catch (Exception ex) { log.LogError(ex, "Email \"{Subject}\" failed", m.Subject); }
    }
}
