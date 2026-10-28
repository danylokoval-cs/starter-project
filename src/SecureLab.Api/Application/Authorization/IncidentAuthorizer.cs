using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Application.Authorization;

public enum IncidentAccessDecision
{
    Allowed,
    Hidden,
    Forbidden
}

public sealed class IncidentAuthorizer(
    IAuthorizationService authorization,
    ILogger<IncidentAuthorizer> logger)
{
    public async Task<bool> IsAllowedAsync(
        ClaimsPrincipal user,
        Incident incident,
        OperationAuthorizationRequirement operation)
    {
        var result = await authorization.AuthorizeAsync(user, incident, operation);
        return result.Succeeded;
    }

    public async Task<IncidentAccessDecision> DecideAsync(
        HttpContext httpContext,
        Incident incident,
        OperationAuthorizationRequirement operation)
    {
        if (!await IsAllowedAsync(httpContext.User, incident, IncidentOperations.Read))
        {
            LogDenied(httpContext, operation, incident.Id);
            return IncidentAccessDecision.Hidden;
        }

        if (operation == IncidentOperations.Read
            || await IsAllowedAsync(httpContext.User, incident, operation))
        {
            return IncidentAccessDecision.Allowed;
        }

        LogDenied(httpContext, operation, incident.Id);
        return IncidentAccessDecision.Forbidden;
    }

    private void LogDenied(
        HttpContext httpContext,
        OperationAuthorizationRequirement operation,
        Guid incidentId)
    {
        logger.LogWarning(
            "authorization.denied subject={SubjectId} action={Action} resource={ResourceId} "
            + "result={Result} occurredAtUtc={OccurredAtUtc:O} traceId={TraceId}",
            httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            "incident." + operation.Name,
            incidentId,
            "denied",
            DateTimeOffset.UtcNow,
            Activity.Current?.Id ?? httpContext.TraceIdentifier);
    }
}
