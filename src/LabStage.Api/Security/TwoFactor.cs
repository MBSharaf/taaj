using System.Security.Claims;
using LabStage.Application.Security;
using LabStage.Domain.Entities;
using Microsoft.AspNetCore.DataProtection;

namespace LabStage.Api.Security;

/// <summary>
/// Two-step sign-in with an authenticator app. The secret is kept encrypted (Data Protection); between the password
/// step and the code step the browser holds only a short-lived signed ticket, never a session.
/// </summary>
public sealed class TwoFactor(IDataProtectionProvider dp, IConfiguration config, IHostEnvironment env)
{
    /// <summary>Claim on the session: "1" when this sign-in passed the code step.</summary>
    public const string Claim = "taaj:2fa";
    public const string Issuer = "Taaj";
    public static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(5);
    public const int MaxFailures = 5;
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);

    private IDataProtector Secrets => dp.CreateProtector("Taaj.TwoFactor.Secret.v1");
    private ITimeLimitedDataProtector Tickets => dp.CreateProtector("Taaj.TwoFactor.LoginTicket.v1").ToTimeLimitedDataProtector();

    /// <summary>Platform admins must use two-step sign-in (on by default everywhere except local development).</summary>
    public bool RequiredForAdmins => config.GetValue("Security:RequireAdminTwoFactor", !env.IsDevelopment());

    public string Protect(string secret) => Secrets.Protect(secret);
    public string Unprotect(string stored) => Secrets.Unprotect(stored);

    public string IssueTicket(AppUser u, bool rememberMe) =>
        Tickets.Protect($"{u.Id:N}|{u.SecurityStamp}|{(rememberMe ? 1 : 0)}", TicketLifetime);

    public (Guid UserId, string Stamp, bool RememberMe)? ReadTicket(string? ticket)
    {
        try
        {
            var parts = Tickets.Unprotect(ticket ?? string.Empty).Split('|');
            return parts.Length == 3 && Guid.TryParse(parts[0], out var id) ? (id, parts[1], parts[2] == "1") : null;
        }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }

    /// <summary>
    /// Checks an authenticator code or an unused recovery code. On success updates the user (last step / used recovery
    /// code); the caller saves. Returns "code", "recovery" or null.
    /// </summary>
    public string? Check(AppUser u, string? input)
    {
        if (u.TwoFactorSecret is null) return null;
        var step = Totp.Verify(Unprotect(u.TwoFactorSecret), input, DateTimeOffset.UtcNow, u.TwoFactorLastStep);
        if (step is { } s) { u.TwoFactorLastStep = s; return "code"; }
        var normalized = Totp.NormalizeRecoveryCode(input ?? string.Empty);
        if (normalized.Length != 8 || string.IsNullOrEmpty(u.TwoFactorRecoveryCodes)) return null;
        var hash = Totp.HashRecoveryCode(normalized);
        var codes = u.TwoFactorRecoveryCodes.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!codes.Remove(hash)) return null;
        u.TwoFactorRecoveryCodes = string.Join(';', codes);
        return "recovery";
    }

    public static bool PassedCodeStep(ClaimsPrincipal user) => user.FindFirst(Claim)?.Value == "1";
}

/// <summary>
/// When admins must use two-step sign-in: an admin session without it can only reach what is needed to set it up
/// (who am I, sign out, the two-step settings); everything else answers 403 two_factor_setup_required.
/// </summary>
public sealed class AdminTwoFactorGate(RequestDelegate next)
{
    private static readonly string[] Allowed = ["/api/v1/auth/me", "/api/v1/auth/logout", "/api/v1/account/two-factor", "/api/v1/account/profile"];

    public async Task InvokeAsync(HttpContext ctx, TwoFactor twoFactor)
    {
        var path = ctx.Request.Path;
        if (path.StartsWithSegments("/api") && ctx.User.IsInRole(AppRoles.Admin) && twoFactor.RequiredForAdmins &&
            !TwoFactor.PassedCodeStep(ctx.User) && !Allowed.Any(a => path.StartsWithSegments(a, StringComparison.OrdinalIgnoreCase)))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(new
            {
                type = "two_factor_setup_required",
                title = "فعّل التحقق بخطوتين لحساب الإدارة أولًا من صفحة الحساب.",
                status = 403,
            });
            return;
        }
        await next(ctx);
    }
}
