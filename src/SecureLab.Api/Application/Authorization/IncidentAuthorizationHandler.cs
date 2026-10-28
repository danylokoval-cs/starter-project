using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Application.Authorization;

public sealed class IncidentAuthorizationHandler
    : AuthorizationHandler<OperationAuthorizationRequirement, Incident>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OperationAuthorizationRequirement requirement,
        Incident incident)
    {
        if (!IncidentAccess.TryGetViewer(context.User, out var viewer))
        {
            return Task.CompletedTask;
        }

        var isOwner = incident.OwnerUserId == viewer.UserId;
        var isNew = incident.Status == IncidentStatus.New;

        var allowed = requirement.Name switch
        {
            IncidentOperations.ReadName => IncidentAccess.VisibleTo(viewer).Compile()(incident),
            IncidentOperations.ReadInternalCommentsName => viewer.IsAnalyst,
            IncidentOperations.UpdateName => isOwner && isNew,
            IncidentOperations.DeleteName => isOwner && isNew,
            IncidentOperations.TriageName => viewer.IsAnalyst,
            _ => false
        };

        if (allowed)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
