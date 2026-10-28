using System.Linq.Expressions;
using System.Security.Claims;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Application.Authorization;

public readonly record struct IncidentViewer(Guid UserId, bool IsAnalyst);

public static class IncidentAccess
{
    public static bool TryGetViewer(ClaimsPrincipal user, out IncidentViewer viewer)
    {
        if (Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            viewer = new IncidentViewer(userId, user.IsInRole(DbSeeder.AnalystRole));
            return true;
        }

        viewer = default;
        return false;
    }

    public static Expression<Func<Incident, bool>> VisibleTo(IncidentViewer viewer)
    {
        var userId = viewer.UserId;
        var isAnalyst = viewer.IsAnalyst;
        return incident => isAnalyst || incident.OwnerUserId == userId;
    }
}
