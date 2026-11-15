using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LabStage.Domain.Entities;
using LabStage.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace LabStage.Api.Security;

/// <summary>
/// Lab finance lock. Lab users (owner and staff) see no money until the owner's separate finance password is entered;
/// a successful entry sets a short-lived, encrypted, HttpOnly cookie bound to that user and lab.
/// </summary>
public sealed class FinanceGate(IDataProtectionProvider dataProtection, LabStageDbContext db)
{
    public const string CookieName = "Taaj.Fin";
    public static readonly TimeSpan UnlockTtl = TimeSpan.FromMinutes(15);
    private IDataProtector Protector => dataProtection.CreateProtector("Taaj.FinanceUnlock.v1");

    public static bool IsLabRole(ClaimsPrincipal user) =>
        user.IsInRole(AppRoles.LaboratoryOwner) || user.IsInRole(AppRoles.LaboratoryStaff);

    public async Task<Laboratory?> LabForUserAsync(Guid userId, CancellationToken ct = default) =>
        await db.Laboratories.FirstOrDefaultAsync(l => l.OwnerUserId == userId || l.Staff.Any(s => s.UserId == userId), ct);

    /// <summary>Expiry of the current unlock for this user and lab, or null when locked.</summary>
    public DateTimeOffset? UnlockedUntil(HttpContext ctx, Guid userId, Guid labId)
    {
        if (!ctx.Request.Cookies.TryGetValue(CookieName, out var raw) || string.IsNullOrEmpty(raw)) return null;
        try
        {
            var parts = Protector.Unprotect(raw).Split('|');
            if (parts.Length != 3 || parts[0] != userId.ToString() || parts[1] != labId.ToString()) return null;
            var until = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(parts[2]));
            return until > DateTimeOffset.UtcNow ? until : null;
        }
        catch
        {
            return null; // tampered, old key, or malformed
        }
    }

    public DateTimeOffset Unlock(HttpContext ctx, Guid userId, Guid labId)
    {
        var until = DateTimeOffset.UtcNow.Add(UnlockTtl);
        ctx.Response.Cookies.Append(CookieName, Protector.Protect($"{userId}|{labId}|{until.ToUnixTimeMilliseconds()}"), new CookieOptions
        {
            HttpOnly = true,
            Secure = ctx.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Expires = until,
            Path = "/",
        });
        return until;
    }

    public static void Lock(HttpContext ctx) => ctx.Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/" });

    /// <summary>True when the caller is a lab user whose money view is locked (non-lab users are never locked).</summary>
    public async Task<bool> IsLockedAsync(HttpContext ctx, CancellationToken ct = default)
    {
        var user = ctx.User;
        if (!IsLabRole(user) || !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return false;
        var lab = await LabForUserAsync(userId, ct);
        if (lab is null) return false;
        return UnlockedUntil(ctx, userId, lab.Id) is null;
    }

    public static ObjectResult LockedResult() => new(new ProblemDetails
    {
        Status = StatusCodes.Status423Locked,
        Title = "FinanceLocked",
        Detail = "المبالغ المالية مقفولة. أدخل كلمة مرور المالية الخاصة بصاحب المعمل.",
    })
    { StatusCode = StatusCodes.Status423Locked };
}

/// <summary>
/// For lab users whose finance view is locked: removes every money amount from API responses
/// (fields ending in "Egp" become null, amounts written in text become "••••"), and flags the response.
/// </summary>
public sealed partial class LabMoneyMaskFilter(FinanceGate gate) : IAsyncResultFilter
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [GeneratedRegex(@"\d[\d,٠-٩٬٫.]*\s*(?:\.\d+)?\s*ج\.م|[٠-٩][٠-٩٬٫,.]*\s*ج\.م")]
    private static partial Regex MoneyText();

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult { Value: not null } result &&
            context.HttpContext.Request.Path.StartsWithSegments("/api") &&
            !context.HttpContext.Request.Path.Value!.Contains("/finance-lock", StringComparison.OrdinalIgnoreCase) &&
            (result.StatusCode is null or < 400) &&
            await gate.IsLockedAsync(context.HttpContext, context.HttpContext.RequestAborted))
        {
            var node = JsonSerializer.SerializeToNode(result.Value, result.Value.GetType(), Web);
            Mask(node);
            if (node is JsonObject obj) obj["financeLocked"] = true;
            result.Value = node;
            result.DeclaredType = typeof(JsonNode);
        }
        await next();
    }

    /// <summary>Masks in place: money fields to null, money text to "••••".</summary>
    private static void Mask(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var key in o.Select(kv => kv.Key).ToList())
                {
                    if (key.EndsWith("Egp", StringComparison.OrdinalIgnoreCase) || key.EndsWith("Piastres", StringComparison.OrdinalIgnoreCase))
                        o[key] = null;
                    else if (Replacement(o[key]) is { } text)
                        o[key] = text;
                    else
                        Mask(o[key]);
                }
                break;
            case JsonArray a:
                for (var i = 0; i < a.Count; i++)
                {
                    if (Replacement(a[i]) is { } text) a[i] = text;
                    else Mask(a[i]);
                }
                break;
        }
    }

    private static JsonNode? Replacement(JsonNode? n) =>
        n is JsonValue v && v.TryGetValue<string>(out var s) && MoneyText().IsMatch(s)
            ? JsonValue.Create(MoneyText().Replace(s, "•••• ج.م"))
            : null;
}
