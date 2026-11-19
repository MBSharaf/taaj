using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;
using LabStage.Application.Security;
using LabStage.Domain.Entities;
using LabStage.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LabStage.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(LabStageDbContext db, LabStage.Application.Email.IEmailSender email, IConfiguration config) : ControllerBase
{
    public sealed record RegisterRequest(
        string Role,
        string FullName,
        string Phone,
        string EntityName,
        string Governorate,
        string Email,
        string Password);

    public sealed record LoginRequest(string Email, string Password, bool RememberMe = true);

    [EnableRateLimiting("auth")]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@'))
            return Problem("أدخل بريدًا إلكترونيًا صحيحًا.", statusCode: StatusCodes.Status400BadRequest);
        if (PasswordPolicy.Problem(req.Password, req.Email) is { } weak)
            return Problem(weak, statusCode: StatusCodes.Status400BadRequest);
        if (string.IsNullOrWhiteSpace(req.FullName) || string.IsNullOrWhiteSpace(req.EntityName))
            return Problem("الاسم واسم العيادة/المعمل مطلوبان.", statusCode: StatusCodes.Status400BadRequest);

        var normEmail = req.Email.Trim().ToUpperInvariant();
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normEmail, ct))
            return Problem("هذا البريد الإلكتروني مسجّل بالفعل.", statusCode: StatusCodes.Status409Conflict);

        var role = req.Role == AppRoles.LaboratoryOwner ? AppRoles.LaboratoryOwner : AppRoles.Dentist;
        var user = new AppUser
        {
            Email = req.Email.Trim(),
            NormalizedEmail = normEmail,
            PasswordHash = PasswordHasher.Hash(req.Password),
            FullName = req.FullName.Trim(),
            Phone = req.Phone.Trim(),
            Role = role,
        };

        db.Users.Add(user);

        if (role == AppRoles.Dentist)
        {
            db.DoctorProfiles.Add(new DoctorProfile
            {
                UserId = user.Id,
                ClinicName = req.EntityName.Trim(),
                Governorate = req.Governorate.Trim(),
                City = req.Governorate.Trim(),
            });
            // The registration clinic becomes the dentist's first (default) clinic.
            db.Clinics.Add(new Clinic { DoctorUserId = user.Id, Name = req.EntityName.Trim(), Governorate = req.Governorate.Trim(), City = req.Governorate.Trim(), Phone = user.Phone, IsDefault = true });
        }
        else
        {
            var settings = await db.PlatformSettings.FirstAsync(ct);
            var slugBase = $"lab-{Guid.NewGuid().ToString("N")[..8]}";
            var lab = new Laboratory
            {
                OwnerUserId = user.Id,
                Slug = slugBase,
                Name = req.EntityName.Trim(),
                Description = $"معمل أسنان في {req.Governorate.Trim()}",
                Governorate = req.Governorate.Trim(),
                City = req.Governorate.Trim(),
                ServiceAreasCsv = req.Governorate.Trim(),
                VerificationStatus = VerificationStatus.Pending,
                Services =
                [
                    new LaboratoryService
                    {
                        Category = "تركيبات ثابتة",
                        Name = "تاج زركونيا",
                        Description = "تاج زركونيا رقمي",
                        PricePiastres = 160_000,
                        EstimatedDays = 3,
                        SupportedStagesCsv = "Design,Milling,Layering,Glazing",
                    },
                ],
                Subscriptions =
                [
                    new LaboratorySubscription
                    {
                        Status = SubscriptionStatus.TrialActive,
                        IsTrial = true,
                        MonthlyPricePiastres = settings.MonthlySubscriptionPiastres,
                        StartsAt = DateTimeOffset.UtcNow,
                        EndsAt = DateTimeOffset.UtcNow.AddDays(settings.FreeTrialDays),
                    },
                ],
            };
            db.Laboratories.Add(lab);
        }

        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = user.Id,
            ActorRole = user.Role,
            Action = "UserRegistered",
            EntityType = nameof(AppUser),
            EntityId = user.Id.ToString(),
            Details = $"Registered {user.Email} as {user.Role}",
        });

        await db.SaveChangesAsync(ct);
        await AccountController.SendConfirmationAsync(db, email, config, HttpContext, user, ct);
        await SignInCookieAsync(user, true);

        return Ok(new { user.Id, user.Email, user.FullName, user.Role, EmailConfirmed = false });
    }

    /// <summary>Failed sign-ins allowed per account before a temporary lockout.</summary>
    public const int MaxFailedLogins = 5;
    public static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(15);

    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, [FromServices] LabStage.Api.Security.Throttle throttle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return Problem("البريد الإلكتروني وكلمة المرور مطلوبان.", statusCode: StatusCodes.Status400BadRequest);

        var normEmail = req.Email.Trim().ToUpperInvariant();
        var failKey = $"login-fail:{normEmail}";
        if (await throttle.FailuresAsync(failKey, ct) >= MaxFailedLogins)
            return Problem("تم إيقاف تسجيل الدخول لهذا الحساب مؤقتًا بعد محاولات فاشلة متكررة. حاول مرة أخرى بعد 15 دقيقة.", statusCode: StatusCodes.Status429TooManyRequests);

        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normEmail, ct);
        if (user is null || !PasswordHasher.Verify(req.Password, user.PasswordHash))
        {
            await throttle.FailAsync(failKey, LockoutWindow, ct);
            return Problem("البريد الإلكتروني أو كلمة المرور غير صحيحة.", statusCode: StatusCodes.Status401Unauthorized);
        }
        await throttle.ResetAsync(failKey, ct);

        // Older, weaker hashes are upgraded transparently on a successful sign-in.
        if (PasswordHasher.NeedsRehash(user.PasswordHash))
        {
            user.PasswordHash = PasswordHasher.Hash(req.Password);
            await db.SaveChangesAsync(ct);
        }

        if (user.IsSuspended)
            return Problem($"تم تعليق الحساب: {user.SuspensionReason ?? "راجع إدارة المنصة"}", statusCode: StatusCodes.Status403Forbidden);

        // Two-step sign-in: no session yet, only a 5-minute ticket for the code step (POST auth/login/two-factor).
        if (user.TwoFactorEnabledAt is not null)
            return Ok(new { TwoFactorRequired = true, Ticket = HttpContext.RequestServices.GetRequiredService<LabStage.Api.Security.TwoFactor>().IssueTicket(user, req.RememberMe) });

        await SignInCookieAsync(user, req.RememberMe);
        return Ok(new { user.Id, user.Email, user.FullName, user.Role });
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        LabStage.Api.Security.FinanceGate.Lock(HttpContext);
        return Ok(new { loggedOut = true });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var user = await db.Users
            .Include(u => u.DoctorProfile)
            .Include(u => u.OwnedLaboratory)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return Unauthorized();

        var unreadNotifications = await db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);

        return Ok(new
        {
            user.Id,
            user.Email,
            user.FullName,
            user.Phone,
            user.Role,
            ClinicName = user.DoctorProfile?.ClinicName,
            LaboratoryId = user.OwnedLaboratory?.Id,
            LaboratoryName = user.OwnedLaboratory?.Name,
            LaboratorySlug = user.OwnedLaboratory?.Slug,
            UnreadNotifications = unreadNotifications,
            EmailConfirmed = user.EmailConfirmedAt != null,
            TwoFactorEnabled = user.TwoFactorEnabledAt != null,
            TwoFactorSetupRequired = user.Role == AppRoles.Admin && !LabStage.Api.Security.TwoFactor.PassedCodeStep(User)
                && HttpContext.RequestServices.GetRequiredService<LabStage.Api.Security.TwoFactor>().RequiredForAdmins,
        });
    }

    private Task SignInCookieAsync(AppUser user, bool isPersistent) => SignInAsync(HttpContext, user, isPersistent);

    /// <summary>Claim carrying the account's security stamp: a password change or reset signs out older sessions.</summary>
    public const string StampClaim = "taaj:stamp";

    public static async Task SignInAsync(HttpContext ctx, AppUser user, bool isPersistent, bool passedTwoFactor = false)
    {
        // Platform admins: never a long "remember me" session (ends with the browser, 8 idle hours at most).
        if (user.Role == AppRoles.Admin) isPersistent = false;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Role, user.Role),
            new(StampClaim, user.SecurityStamp),
            new(LabStage.Api.Security.TwoFactor.Claim, passedTwoFactor ? "1" : "0"),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await ctx.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            // "Remember me": 14 days; otherwise the cookie options apply (8 idle hours, sliding).
            new AuthenticationProperties { IsPersistent = isPersistent, ExpiresUtc = isPersistent ? DateTimeOffset.UtcNow.AddDays(14) : null });
    }
}
