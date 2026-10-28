namespace SecureLab.Api.Presentation.Contracts;

public sealed record UpdateIncidentRequest(string? Title, string? Description);

public sealed record TriageIncidentRequest(string? Status);
