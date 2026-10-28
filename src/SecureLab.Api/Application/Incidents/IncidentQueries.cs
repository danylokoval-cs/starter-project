using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Application.Authorization;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Application.Incidents;

public sealed class IncidentQueries(SecureLabDbContext dbContext, ILogger<IncidentQueries> logger)
{
    public async Task<IReadOnlyList<IncidentListItemResponse>> GetListAsync(
        IncidentViewer viewer,
        IncidentStatus? status,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading incidents with status filter {Status}", status);

        var query = dbContext.Incidents
            .AsNoTracking()
            .Where(IncidentAccess.VisibleTo(viewer));
        if (status is not null)
        {
            query = query.Where(incident => incident.Status == status);
        }

        return await query
            .OrderByDescending(incident => incident.CreatedAtUtc)
            .Select(incident => new IncidentListItemResponse(
                incident.Id,
                incident.Title,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public Task<Incident?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.Incidents
            .AsNoTracking()
            .SingleOrDefaultAsync(incident => incident.Id == id, cancellationToken);
    }

    public Task<IncidentDetailsResponse?> GetDetailsAsync(
        Guid id,
        bool includeInternalComments,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading incident {IncidentId}", id);

        return dbContext.Incidents
            .AsNoTracking()
            .Where(incident => incident.Id == id)
            .Select(incident => new IncidentDetailsResponse(
                incident.Id,
                incident.Title,
                incident.Description,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc,
                incident.Owner.DisplayName,
                incident.Comments
                    .Where(comment => includeInternalComments || !comment.IsInternal)
                    .OrderBy(comment => comment.CreatedAtUtc)
                    .Select(comment => new IncidentCommentResponse(
                        comment.Id,
                        comment.Author.DisplayName,
                        comment.Text,
                        comment.CreatedAtUtc))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<IncidentComment?> FindCommentAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.IncidentComments
            .AsNoTracking()
            .Include(comment => comment.Incident)
            .Include(comment => comment.Author)
            .SingleOrDefaultAsync(comment => comment.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<IncidentSeveritySummaryResponse>> GetSeveritySummaryAsync(
        IncidentViewer viewer,
        IncidentStatus? status,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Incidents
            .AsNoTracking()
            .Where(IncidentAccess.VisibleTo(viewer));
        if (status is not null)
        {
            query = query.Where(incident => incident.Status == status);
        }

        var presentGroups = await query
            .GroupBy(incident => incident.Severity)
            .Select(group => new IncidentSeveritySummaryResponse(group.Key.ToString(), group.Count()))
            .ToListAsync(cancellationToken);

        var severityRank = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [nameof(IncidentSeverity.Critical)] = 0,
            [nameof(IncidentSeverity.High)] = 1,
            [nameof(IncidentSeverity.Medium)] = 2,
            [nameof(IncidentSeverity.Low)] = 3
        };

        var summary = presentGroups
            .OrderBy(item => severityRank[item.Severity])
            .ToList();

        logger.LogInformation(
            "Computed severity summary with {GroupCount} groups for status filter {Status}",
            summary.Count,
            status);

        return summary;
    }
}
