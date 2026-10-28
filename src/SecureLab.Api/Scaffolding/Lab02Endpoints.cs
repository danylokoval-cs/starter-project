using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Application.Authorization;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Scaffolding;

public static class Lab02Endpoints
{
    public static void MapLab02Endpoints(this WebApplication app)
    {
        app.MapGet("/api/incidents/search", async (
            string? q, string? sortBy, HttpContext httpContext, SecureLabDbContext db, CancellationToken ct) =>
        {
            if (!IncidentAccess.TryGetViewer(httpContext.User, out var viewer))
                return Results.Challenge();

            var pattern = "%" + EscapeLike(q ?? "") + "%";
            var found = db.Incidents.AsNoTracking()
                .Where(IncidentAccess.VisibleTo(viewer))
                .Where(item => EF.Functions.ILike(item.Title, pattern, "\\")
                    || EF.Functions.ILike(item.Description, pattern, "\\"));

            IQueryable<Incident>? ordered = sortBy switch
            {
                null or "" or "createdAtUtc" => found
                    .OrderByDescending(item => item.CreatedAtUtc)
                    .ThenBy(item => item.Id),
                "severity" => found
                    .OrderBy(item => item.Severity == IncidentSeverity.Critical ? 0
                        : item.Severity == IncidentSeverity.High ? 1
                        : item.Severity == IncidentSeverity.Medium ? 2 : 3)
                    .ThenBy(item => item.Id),
                "status" => found
                    .OrderBy(item => item.Status == IncidentStatus.New ? 0
                        : item.Status == IncidentStatus.Triaged ? 1
                        : item.Status == IncidentStatus.InProgress ? 2
                        : item.Status == IncidentStatus.Resolved ? 3 : 4)
                    .ThenBy(item => item.Id),
                _ => null
            };
            if (ordered is null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["sortBy"] = ["Допустимі значення: createdAtUtc, severity, status."]
                });

            var rows = await ordered
                .Take(50)
                .Select(row => new SearchIncidentResponse(
                    row.Id, row.Title, row.Description,
                    row.Severity.ToString(), row.Status.ToString(), row.CreatedAtUtc))
                .ToListAsync(ct);
            return Results.Ok(rows);
        }).RequireAuthorization();
        app.MapPost("/api/incidents", async (
            CreateIncidentRequest request, HttpContext httpContext, SecureLabDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var ownerUserId))
                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Не вдалося визначити поточного користувача.");

            var now = DateTimeOffset.UtcNow;
            var errors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(request.Title))
                errors["title"] = ["Заголовок обов'язковий."];
            else if (request.Title.Length > 160)
                errors["title"] = ["Заголовок не довший за 160 символів."];

            if (string.IsNullOrWhiteSpace(request.Description))
                errors["description"] = ["Опис обов'язковий."];
            else if (request.Description.Length > 4000)
                errors["description"] = ["Опис не довший за 4000 символів."];

            var severityIsValid = Enum.TryParse<IncidentSeverity>(request.Severity, ignoreCase: true, out var severity)
                && Enum.IsDefined(severity);
            if (!severityIsValid)
                errors["severity"] = ["Допустимі значення: Low, Medium, High, Critical."];

            if (request.OccurredAtUtc is null)
                errors["occurredAtUtc"] = ["Час події обов'язковий."];
            else if (request.OccurredAtUtc > now.AddMinutes(5))
                errors["occurredAtUtc"] = ["Час події не може бути більш ніж на 5 хвилин у майбутньому."];

            var title = request.Title?.Trim() ?? "";
            var description = request.Description?.Trim() ?? "";

            if (severityIsValid
                && (severity == IncidentSeverity.High || severity == IncidentSeverity.Critical)
                && !errors.ContainsKey("description") && description.Length < 40)
                errors["description"] = ["Для High або Critical опис має містити щонайменше 40 символів."];

            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            if (await db.Incidents.AnyAsync(
                    item => item.Title == title && item.Status != IncidentStatus.Closed, ct))
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Інцидент уже існує",
                    detail: "Інцидент з таким заголовком уже створено і ще не закрито.");

            var incident = new Incident
            {
                Id = Guid.NewGuid(), OwnerUserId = ownerUserId,
                Title = title, Description = description,
                Severity = severity,
                Status = IncidentStatus.New, OccurredAtUtc = request.OccurredAtUtc!.Value.ToUniversalTime(),
                CreatedAtUtc = now, UpdatedAtUtc = now
            };
            db.Incidents.Add(incident);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/incidents/{incident.Id}", new CreatedIncidentResponse(
                incident.Id, incident.Title, incident.Description,
                incident.Severity.ToString(), incident.Status.ToString(),
                incident.OccurredAtUtc, incident.CreatedAtUtc));
        }).RequireAuthorization();
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}

public sealed record CreateIncidentRequest(
    string? Title, string? Description, string? Severity, DateTimeOffset? OccurredAtUtc);

public sealed record CreatedIncidentResponse(
    Guid Id, string Title, string Description, string Severity, string Status,
    DateTimeOffset OccurredAtUtc, DateTimeOffset CreatedAtUtc);

public sealed record SearchIncidentResponse(
    Guid Id, string Title, string Description, string Severity, string Status,
    DateTimeOffset CreatedAtUtc);
