using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Application.Incidents;

public sealed class IncidentCommands(SecureLabDbContext dbContext, ILogger<IncidentCommands> logger)
{
    public Task<Incident?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.Incidents
            .SingleOrDefaultAsync(incident => incident.Id == id, cancellationToken);
    }

    public async Task UpdateContentAsync(
        Incident incident,
        string title,
        string description,
        CancellationToken cancellationToken)
    {
        incident.Title = title;
        incident.Description = description;
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Updated content of incident {IncidentId}", incident.Id);
    }

    public async Task TriageAsync(
        Incident incident,
        Guid analystUserId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        dbContext.IncidentStatusHistory.Add(new IncidentStatusHistory
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            ChangedByUserId = analystUserId,
            OldStatus = incident.Status,
            NewStatus = IncidentStatus.Triaged,
            Note = "Первинну оцінку завершено.",
            CreatedAtUtc = now
        });
        incident.Status = IncidentStatus.Triaged;
        incident.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Triaged incident {IncidentId}", incident.Id);
    }

    public async Task DeleteAsync(Incident incident, CancellationToken cancellationToken)
    {
        dbContext.Incidents.Remove(incident);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Deleted incident {IncidentId}", incident.Id);
    }
}
