using LabStage.Api.Security;
using LabStage.Application.Security;
using LabStage.Domain.Entities;
using LabStage.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace LabStage.Api.Controllers;

/// <summary>
/// Two-step sign-in: the code step after the password, and turning it on/off from the account page.
/// Any user can turn it on; platform admins must (see <see cref="AdminTwoFactorGate"/>).
/// </summary>
[ApiController]
public sealed class TwoFactorController(LabStageDbContext db, TwoFactor twoFactor, Throttle throttle) : ControllerBase
{
    public sealed record CodeRequest(string Ticket, string Code);
    public sealed record EnableRequest(string Code);
    public sealed record DisableRequest(string Password, string Code);

    private const string BadCode = "الكود غير صحيح أو انتهت مدته. اكتب الكود الحالي من تطبيق المصادقة.";

    /// <summary>Second step of sign-in: the ticket from /auth/login plus a 6-digit code (or a recovery code).</summary>
    [EnableRateLimiting("auth")]
    [HttpPost("api/v1/auth/login/two-factor")]
    public async Task<IActionResult> Login([FromBody] CodeRequest req, CancellationToken ct)
    {
        if (twoFactor.ReadTicket(req.Ticket) is not { } t)
            return Problem("انتهت مهلة تسجيل الدخول. سجّل الدخول من جديد.", statusCode: StatusCodes.Status401Unauthorized);
        var failKey = $"2fa-fail:{t.UserId:N}";
        if (await throttle.FailuresAsync(failKey, ct) >= TwoFactor.MaxFailures)
            return Problem("تم إيقاف تسجيل الدخول مؤقتًا بعد محاولات خاطئة متكررة. حاول بعد 15 دقيقة.", statusCode: StatusCodes.Status429TooManyRequests);

        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == t.UserId, ct);
        if (u is null || u.IsSuspended || u.SecurityStamp != t.Stamp || u.TwoFactorEnabledAt is null)
            return Problem("انتهت مهلة تسجيل الدخول. سجّل الدخول من جديد.", statusCode: StatusCodes.Status401Unauthorized);

        var used = twoFactor.Check(u, req.Code);
        if (used is null)
        {
            await throttle.FailAsync(failKey, TwoFactor.FailureWindow, ct);
            return Problem(BadCode, statusCode: StatusCodes.Status401Unauthorized);
        }
        db.AddUserAudit(u, used == "recovery" ? "TwoFactorRecoveryCodeUsed" : "TwoFactorSignIn");
        await db.SaveChangesAsync(ct);
        await throttle.ResetAsync(failKey, ct);
        await AuthController.SignInAsync(HttpContext, u, t.RememberMe, passedTwoFactor: true);
        return Ok(new { u.Id, u.Email, u.FullName, u.Role, RecoveryCodesLeft = RecoveryLeft(u) });
    }

    [Authorize]
    [HttpGet("api/v1/account/two-factor")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking().FirstAsync(x => x.Id == User.GetUserId(), ct);
        return Ok(new
        {
            Enabled = u.TwoFactorEnabledAt != null,
            u.TwoFactorEnabledAt,
            RecoveryCodesLeft = RecoveryLeft(u),
            Required = u.Role == AppRoles.Admin && twoFactor.RequiredForAdmins,
        });
    }

    /// <summary>Starts setup: a new secret (not active until a code from it is confirmed), as a QR code and as text.</summary>
    [Authorize]
    [EnableRateLimiting("auth")]
    [HttpPost("api/v1/account/two-factor/setup")]
    public async Task<IActionResult> Setup(CancellationToken ct)
    {
        var u = await db.Users.FirstAsync(x => x.Id == User.GetUserId(), ct);
        if (u.TwoFactorEnabledAt is not null)
            return Problem("التحقق بخطوتين مفعّل بالفعل. أوقفه أولًا لو عايز تربط تطبيقًا جديدًا.", statusCode: StatusCodes.Status409Conflict);
        var secret = Totp.NewSecret();
        u.TwoFactorSecret = twoFactor.Protect(secret);
        u.TwoFactorLastStep = 0;
        await db.SaveChangesAsync(ct);

        var uri = Totp.SetupUri(TwoFactor.Issuer, u.Email, secret);
        using var data = new QRCodeGenerator().CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
        var svg = new SvgQRCode(data).GetGraphic(5);
        return Ok(new
        {
            Secret = string.Join(' ', secret.Chunk(4).Select(c => new string(c))),
            OtpAuthUri = uri,
            QrSvgDataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg)),
        });
    }

    /// <summary>Confirms setup with a code from the app; turns it on and returns the one-time recovery codes (shown once).</summary>
    [Authorize]
    [EnableRateLimiting("auth")]
    [HttpPost("api/v1/account/two-factor/enable")]
    public async Task<IActionResult> Enable([FromBody] EnableRequest req, CancellationToken ct)
    {
        var u = await db.Users.FirstAsync(x => x.Id == User.GetUserId(), ct);
        if (u.TwoFactorEnabledAt is not null) return Problem("التحقق بخطوتين مفعّل بالفعل.", statusCode: StatusCodes.Status409Conflict);
        if (u.TwoFactorSecret is null) return Problem("ابدأ الإعداد أولًا.", statusCode: StatusCodes.Status400BadRequest);
        var step = Totp.Verify(twoFactor.Unprotect(u.TwoFactorSecret), req.Code, DateTimeOffset.UtcNow, u.TwoFactorLastStep);
        if (step is null) return Problem(BadCode, statusCode: StatusCodes.Status400BadRequest);

        var codes = Totp.NewRecoveryCodes();
        u.TwoFactorLastStep = step.Value;
        u.TwoFactorEnabledAt = DateTimeOffset.UtcNow;
        u.TwoFactorRecoveryCodes = string.Join(';', codes.Select(Totp.HashRecoveryCode));
        u.SecurityStamp = Guid.NewGuid().ToString("N"); // other sessions (signed in without the code) are signed out
        db.AddUserAudit(u, "TwoFactorEnabled");
        await db.SaveChangesAsync(ct);
        await AuthController.SignInAsync(HttpContext, u, isPersistent: true, passedTwoFactor: true);
        return Ok(new { Enabled = true, RecoveryCodes = codes });
    }

    /// <summary>Turns it off: needs the password and a current code (or a recovery code).</summary>
    [Authorize]
    [EnableRateLimiting("auth")]
    [HttpPost("api/v1/account/two-factor/disable")]
    public async Task<IActionResult> Disable([FromBody] DisableRequest req, CancellationToken ct)
    {
        var u = await db.Users.FirstAsync(x => x.Id == User.GetUserId(), ct);
        if (u.TwoFactorEnabledAt is null) return Ok(new { Enabled = false });
        if (!PasswordHasher.Verify(req.Password ?? string.Empty, u.PasswordHash))
            return Problem("كلمة المرور غير صحيحة.", statusCode: StatusCodes.Status400BadRequest);
        if (twoFactor.Check(u, req.Code) is null) return Problem(BadCode, statusCode: StatusCodes.Status400BadRequest);
        Clear(u);
        u.SecurityStamp = Guid.NewGuid().ToString("N");
        db.AddUserAudit(u, "TwoFactorDisabled");
        await db.SaveChangesAsync(ct);
        await AuthController.SignInAsync(HttpContext, u, isPersistent: true);
        return Ok(new { Enabled = false });
    }

    /// <summary>Admin help for a user who lost the phone and the recovery codes: turns it off so they can set it up again.</summary>
    [Authorize(Roles = AppRoles.Admin)]
    [EnableRateLimiting("auth")]
    [HttpPost("api/v1/admin/users/{id:guid}/two-factor/reset")]
    public async Task<IActionResult> AdminReset(Guid id, CancellationToken ct)
    {
        if (id == User.GetUserId()) return Problem("لا يمكنك إلغاء التحقق بخطوتين لحسابك من هنا.", statusCode: StatusCodes.Status400BadRequest);
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null) return NotFound();
        Clear(u);
        u.SecurityStamp = Guid.NewGuid().ToString("N");
        db.AddUserAudit(u, "TwoFactorResetByAdmin", User.GetUserId(), AppRoles.Admin);
        await db.SaveChangesAsync(ct);
        return Ok(new { Enabled = false });
    }

    private static void Clear(AppUser u)
    {
        u.TwoFactorEnabledAt = null;
        u.TwoFactorSecret = null;
        u.TwoFactorRecoveryCodes = null;
    }

    private static int RecoveryLeft(AppUser u) =>
        string.IsNullOrEmpty(u.TwoFactorRecoveryCodes) ? 0 : u.TwoFactorRecoveryCodes.Split(';', StringSplitOptions.RemoveEmptyEntries).Length;

}
