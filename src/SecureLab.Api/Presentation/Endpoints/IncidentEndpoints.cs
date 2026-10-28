using SecureLab.Api.Application.Authorization;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Presentation.Endpoints;

public static class IncidentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/incidents")
            .WithTags("Incidents")
            .RequireAuthorization();

        group.MapGet("/", GetListAsync)
            .WithName("GetIncidents")
            .Produces<IReadOnlyList<IncidentListItemResponse>>()
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetDetailsAsync)
            .WithName("GetIncidentDetails")
            .Produces<IncidentDetailsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/severity-summary", GetSeveritySummaryAsync)
            .WithName("GetIncidentSeveritySummary")
            .Produces<IReadOnlyList<IncidentSeveritySummaryResponse>>()
            .ProducesValidationProblem();

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateIncident")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteIncident")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/triage", TriageAsync)
            .WithName("TriageIncident")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapGroup("/api/incident-comments")
            .WithTags("Incident comments")
            .RequireAuthorization()
            .MapGet("/{id:guid}", GetCommentAsync)
            .WithName("GetIncidentComment")
            .Produces<IncidentCommentResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetListAsync(
        string? status,
        HttpContext httpContext,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        if (!IncidentAccess.TryGetViewer(httpContext.User, out var viewer))
        {
            return Results.Challenge();
        }

        IncidentStatus? parsedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<IncidentStatus>(status, ignoreCase: true, out var candidate)
                || !Enum.IsDefined(candidate))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = ["Допустимі значення: New, Triaged, InProgress, Resolved, Closed."]
                });
            }

            parsedStatus = candidate;
        }

        return Results.Ok(await queries.GetListAsync(viewer, parsedStatus, cancellationToken));
    }

    private static async Task<IResult> GetDetailsAsync(
        Guid id,
        HttpContext httpContext,
        IncidentAuthorizer authorizer,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        var incident = await queries.FindAsync(id, cancellationToken);
        if (incident is null)
        {
            return IncidentResults.NotFound();
        }

        var decision = await authorizer.DecideAsync(httpContext, incident, IncidentOperations.Read);
        if (decision != IncidentAccessDecision.Allowed)
        {
            return IncidentResults.Denied(decision);
        }

        var includeInternal = await authorizer.IsAllowedAsync(
            httpContext.User, incident, IncidentOperations.ReadInternalComments);
        var details = await queries.GetDetailsAsync(id, includeInternal, cancellationToken);
        return details is null ? IncidentResults.NotFound() : Results.Ok(details);
    }

    private static async Task<IResult> GetSeveritySummaryAsync(
        string? status,
        HttpContext httpContext,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        if (!IncidentAccess.TryGetViewer(httpContext.User, out var viewer))
        {
            return Results.Challenge();
        }

        IncidentStatus? parsedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<IncidentStatus>(status, ignoreCase: true, out var candidate)
                || !Enum.IsDefined(candidate))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = ["Допустимі значення: New, Triaged, InProgress, Resolved, Closed."]
                });
            }

            parsedStatus = candidate;
        }

        return Results.Ok(await queries.GetSeveritySummaryAsync(viewer, parsedStatus, cancellationToken));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateIncidentRequest request,
        HttpContext httpContext,
        IncidentAuthorizer authorizer,
        IncidentCommands commands,
        CancellationToken cancellationToken)
    {
        var incident = await commands.FindForUpdateAsync(id, cancellationToken);
        if (incident is null)
        {
            return IncidentResults.NotFound();
        }

        var decision = await authorizer.DecideAsync(httpContext, incident, IncidentOperations.Update);
        if (decision != IncidentAccessDecision.Allowed)
        {
            return IncidentResults.Denied(decision);
        }

        var title = request.Title?.Trim() ?? "";
        var description = request.Description?.Trim() ?? "";
        var errors = new Dictionary<string, string[]>();

        if (title.Length == 0)
        {
            errors["title"] = ["Заголовок обов'язковий."];
        }
        else if (title.Length > 160)
        {
            errors["title"] = ["Заголовок не довший за 160 символів."];
        }

        if (description.Length == 0)
        {
            errors["description"] = ["Опис обов'язковий."];
        }
        else if (description.Length > 4000)
        {
            errors["description"] = ["Опис не довший за 4000 символів."];
        }
        else if (incident.Severity is IncidentSeverity.High or IncidentSeverity.Critical
            && description.Length < 40)
        {
            errors["description"] = ["Для High або Critical опис має містити щонайменше 40 символів."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        await commands.UpdateContentAsync(incident, title, description, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        HttpContext httpContext,
        IncidentAuthorizer authorizer,
        IncidentCommands commands,
        CancellationToken cancellationToken)
    {
        var incident = await commands.FindForUpdateAsync(id, cancellationToken);
        if (incident is null)
        {
            return IncidentResults.NotFound();
        }

        var decision = await authorizer.DecideAsync(httpContext, incident, IncidentOperations.Delete);
        if (decision != IncidentAccessDecision.Allowed)
        {
            return IncidentResults.Denied(decision);
        }

        await commands.DeleteAsync(incident, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> TriageAsync(
        Guid id,
        HttpContext httpContext,
        IncidentAuthorizer authorizer,
        IncidentCommands commands,
        CancellationToken cancellationToken)
    {
        if (!IncidentAccess.TryGetViewer(httpContext.User, out var viewer))
        {
            return Results.Challenge();
        }

        var incident = await commands.FindForUpdateAsync(id, cancellationToken);
        if (incident is null)
        {
            return IncidentResults.NotFound();
        }

        var decision = await authorizer.DecideAsync(httpContext, incident, IncidentOperations.Triage);
        if (decision != IncidentAccessDecision.Allowed)
        {
            return IncidentResults.Denied(decision);
        }

        if (incident.Status != IncidentStatus.New)
        {
            return Results.Problem(
                title: "Перехід недопустимий",
                detail: "Первинну оцінку дозволено лише для інциденту зі статусом New.",
                statusCode: StatusCodes.Status409Conflict);
        }

        await commands.TriageAsync(incident, viewer.UserId, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetCommentAsync(
        Guid id,
        HttpContext httpContext,
        IncidentAuthorizer authorizer,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        var comment = await queries.FindCommentAsync(id, cancellationToken);
        if (comment is null)
        {
            return IncidentResults.CommentNotFound();
        }

        var operation = comment.IsInternal
            ? IncidentOperations.ReadInternalComments
            : IncidentOperations.Read;
        var decision = await authorizer.DecideAsync(httpContext, comment.Incident, operation);
        if (decision != IncidentAccessDecision.Allowed)
        {
            return IncidentResults.CommentNotFound();
        }

        return Results.Ok(new IncidentCommentResponse(
            comment.Id,
            comment.Author.DisplayName,
            comment.Text,
            comment.CreatedAtUtc));
    }
}
