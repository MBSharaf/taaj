using LabStage.Api.Security;
using LabStage.Application.Security;
using LabStage.Domain.Entities;
using LabStage.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LabStage.Api.Controllers;

/// <summary>Lab finance password: set by the owner, entered by lab users to see money for a short time.</summary>
[ApiController]
[Route("api/v1/labs/my/finance-lock")]
[Authorize(Roles = $"{AppRoles.LaboratoryOwner},{AppRoles.LaboratoryStaff}")]
public sealed class FinanceLockController(LabStageDbContext db, FinanceGate gate, Throttle throttle) : ControllerBase
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(15);

    public sealed record SetupRequest(string AccountPassword, string NewPin);
    public sealed record UnlockRequest(string Pin);

    [HttpGet]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var lab = await gate.LabForUserAsync(userId, ct);
        if (lab is null) return NotFound();
        var until = gate.UnlockedUntil(HttpContext, userId, lab.Id);
        return Ok(new { hasPin = lab.FinancePinHash is not null, unlocked = until is not null, unlockedUntil = until, isOwner = lab.OwnerUserId == userId });
    }

    /// <summary>Set or change the finance password (owner only; always confirmed with the login password).</summary>
    [EnableRateLimiting("auth")]
    [HttpPost("setup")]
    public async Task<IActionResult> Setup([FromBody] SetupRequest req, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var lab = await gate.LabForUserAsync(userId, ct);
        if (lab is null) return NotFound();
        if (lab.OwnerUserId != userId)
            return Problem("كلمة مرور المالية يحددها صاحب المعمل فقط.", statusCode: StatusCodes.Status403Forbidden);
        var pin = req.NewPin?.Trim() ?? string.Empty;
        if (pin.Length is < 4 or > 64)
            return Problem("كلمة مرور المالية يجب أن تكون من 4 إلى 64 حرفًا أو رقمًا.", statusCode: StatusCodes.Status400BadRequest);
        var owner = await db.Users.FirstAsync(u => u.Id == userId, ct);
        if (!PasswordHasher.Verify(req.AccountPassword ?? string.Empty, owner.PasswordHash))
            return Problem("كلمة مرور الحساب غير صحيحة.", statusCode: StatusCodes.Status401Unauthorized);
        if (PasswordHasher.Verify(pin, owner.PasswordHash))
            return Problem("اختر كلمة مرور للمالية مختلفة عن كلمة مرور الدخول.", statusCode: StatusCodes.Status400BadRequest);

        lab.FinancePinHash = PasswordHasher.Hash(pin);
        lab.FinancePinSetAt = DateTimeOffset.UtcNow;
        db.AuditLogs.Add(new AuditLog { ActorUserId = userId, ActorRole = AppRoles.LaboratoryOwner, Action = "FinancePinSet", EntityType = nameof(Laboratory), EntityId = lab.Id.ToString(), Details = "" });
        await db.SaveChangesAsync(ct);
        await throttle.ResetAsync(FailKey(lab.Id), ct);
        var until = gate.Unlock(HttpContext, userId, lab.Id);
        return Ok(new { hasPin = true, unlocked = true, unlockedUntil = until });
    }

    [EnableRateLimiting("auth")]
    [HttpPost("unlock")]
    public async Task<IActionResult> Unlock([FromBody] UnlockRequest req, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var lab = await gate.LabForUserAsync(userId, ct);
        if (lab is null) return NotFound();
        if (lab.FinancePinHash is null)
            return Problem("لم يحدد صاحب المعمل كلمة مرور المالية بعد.", statusCode: StatusCodes.Status409Conflict);
        var key = FailKey(lab.Id);
        if (await throttle.FailuresAsync(key, ct) >= MaxFailedAttempts)
            return Problem("تم إيقاف فتح المالية مؤقتًا بعد محاولات خاطئة متكررة. حاول بعد 15 دقيقة.", statusCode: StatusCodes.Status429TooManyRequests);
        if (!PasswordHasher.Verify(req.Pin?.Trim() ?? string.Empty, lab.FinancePinHash))
        {
            await throttle.FailAsync(key, LockoutWindow, ct);
            return Problem("كلمة مرور المالية غير صحيحة.", statusCode: StatusCodes.Status401Unauthorized);
        }
        await throttle.ResetAsync(key, ct);
        var until = gate.Unlock(HttpContext, userId, lab.Id);
        return Ok(new { unlocked = true, unlockedUntil = until });
    }

    [HttpPost("lock")]
    public IActionResult Lock()
    {
        FinanceGate.Lock(HttpContext);
        return Ok(new { unlocked = false });
    }

    private static string FailKey(Guid labId) => $"fin-fail:{labId}";
}

/// <summary>Admin: clear a lab's finance password so the owner can set a new one.</summary>
[ApiController]
[Authorize(Roles = AppRoles.Admin)]
public sealed class FinanceLockAdminController(LabStageDbContext db) : ControllerBase
{
    [HttpPost("api/v1/admin/labs/{id:guid}/finance-lock/reset")]
    public async Task<IActionResult> Reset([FromRoute] Guid id, CancellationToken ct)
    {
        var lab = await db.Laboratories.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lab is null) return NotFound();
        lab.FinancePinHash = null;
        lab.FinancePinSetAt = null;
        db.AuditLogs.Add(new AuditLog { ActorUserId = User.GetUserId(), ActorRole = AppRoles.Admin, Action = "FinancePinReset", EntityType = nameof(Laboratory), EntityId = lab.Id.ToString(), Details = "" });
        await db.SaveChangesAsync(ct);
        return Ok(new { reset = true });
    }
}

/// <summary>A dentist's clinics (one is the default for new cases).</summary>
[ApiController]
[Route("api/v1/clinics")]
[Authorize(Roles = AppRoles.Dentist)]
public sealed class ClinicsController(LabStageDbContext db) : ControllerBase
{
    public sealed record ClinicRequest(string Name, string? Address, string? City, string? Governorate, string? Phone, bool IsDefault = false);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var items = await db.Clinics.Where(c => c.DoctorUserId == userId)
            .OrderByDescending(c => c.IsDefault).ThenBy(c => c.CreatedAt)
            .Select(c => new { c.Id, c.Name, c.Address, c.City, c.Governorate, c.Phone, c.IsDefault })
            .ToListAsync(ct);
        return Ok(new { items });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ClinicRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return Problem("اسم العيادة مطلوب.", statusCode: StatusCodes.Status400BadRequest);
        var userId = User.GetUserId();
        var mine = await db.Clinics.Where(c => c.DoctorUserId == userId).ToListAsync(ct);
        if (mine.Count >= 20)
            return Problem("الحد الأقصى 20 عيادة.", statusCode: StatusCodes.Status400BadRequest);
        var clinic = new Clinic
        {
            DoctorUserId = userId,
            Name = req.Name.Trim(),
            Address = req.Address?.Trim() ?? string.Empty,
            City = req.City?.Trim() ?? string.Empty,
            Governorate = req.Governorate?.Trim() ?? string.Empty,
            Phone = req.Phone?.Trim() ?? string.Empty,
            IsDefault = req.IsDefault || mine.Count == 0,
        };
        if (clinic.IsDefault) mine.ForEach(c => c.IsDefault = false);
        db.Clinics.Add(clinic);
        await db.SaveChangesAsync(ct);
        return Ok(new { clinic.Id, clinic.Name, clinic.Address, clinic.City, clinic.Governorate, clinic.Phone, clinic.IsDefault });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] ClinicRequest req, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var mine = await db.Clinics.Where(c => c.DoctorUserId == userId).ToListAsync(ct);
        var clinic = mine.FirstOrDefault(c => c.Id == id);
        if (clinic is null) return NotFound();
        if (!string.IsNullOrWhiteSpace(req.Name)) clinic.Name = req.Name.Trim();
        if (req.Address is not null) clinic.Address = req.Address.Trim();
        if (req.City is not null) clinic.City = req.City.Trim();
        if (req.Governorate is not null) clinic.Governorate = req.Governorate.Trim();
        if (req.Phone is not null) clinic.Phone = req.Phone.Trim();
        if (req.IsDefault) { mine.ForEach(c => c.IsDefault = false); clinic.IsDefault = true; }
        await db.SaveChangesAsync(ct);
        return Ok(new { clinic.Id, clinic.Name, clinic.Address, clinic.City, clinic.Governorate, clinic.Phone, clinic.IsDefault });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var mine = await db.Clinics.Where(c => c.DoctorUserId == userId).ToListAsync(ct);
        var clinic = mine.FirstOrDefault(c => c.Id == id);
        if (clinic is null) return NotFound();
        if (mine.Count == 1)
            return Problem("لا يمكن حذف العيادة الوحيدة. أضف عيادة أخرى أولًا.", statusCode: StatusCodes.Status400BadRequest);
        db.Clinics.Remove(clinic); // existing orders keep their address; their clinic link is cleared
        if (clinic.IsDefault) mine.First(c => c.Id != id).IsDefault = true;
        await db.SaveChangesAsync(ct);
        return Ok(new { deleted = true });
    }
}
