using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace SecureLab.Api.Application.Authorization;

public static class IncidentOperations
{
    public const string ReadName = "read";
    public const string ReadInternalCommentsName = "read-internal-comments";
    public const string UpdateName = "update";
    public const string DeleteName = "delete";
    public const string TriageName = "triage";

    public static readonly OperationAuthorizationRequirement Read = new() { Name = ReadName };
    public static readonly OperationAuthorizationRequirement ReadInternalComments = new() { Name = ReadInternalCommentsName };
    public static readonly OperationAuthorizationRequirement Update = new() { Name = UpdateName };
    public static readonly OperationAuthorizationRequirement Delete = new() { Name = DeleteName };
    public static readonly OperationAuthorizationRequirement Triage = new() { Name = TriageName };
}
