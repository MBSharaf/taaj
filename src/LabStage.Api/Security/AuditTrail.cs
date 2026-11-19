using LabStage.Domain.Entities;
using LabStage.Infrastructure.Persistence;

namespace LabStage.Api.Security;

public static class AuditTrail
{
    /// <summary>Records an account event (password, two-step, profile...) for <paramref name="u"/>; saved with the caller's SaveChanges.</summary>
    public static void AddUserAudit(this LabStageDbContext db, AppUser u, string action, Guid? actor = null, string? actorRole = null) =>
        db.AuditLogs.Add(new AuditLog { ActorUserId = actor ?? u.Id, ActorRole = actorRole ?? u.Role, Action = action, EntityType = nameof(AppUser), EntityId = u.Id.ToString(), Details = "" });
}
