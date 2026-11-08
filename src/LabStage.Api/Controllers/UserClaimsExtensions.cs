using System.Security.Claims;

namespace LabStage.Api.Controllers;

public static class UserClaimsExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }

    public static string GetRole(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

    public static string GetFullName(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
}
