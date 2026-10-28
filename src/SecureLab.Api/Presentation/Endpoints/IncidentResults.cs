using SecureLab.Api.Application.Authorization;

namespace SecureLab.Api.Presentation.Endpoints;

public static class IncidentResults
{
    public static IResult NotFound() => Results.Problem(
        title: "Інцидент не знайдено",
        statusCode: StatusCodes.Status404NotFound);

    public static IResult CommentNotFound() => Results.Problem(
        title: "Коментар не знайдено",
        statusCode: StatusCodes.Status404NotFound);

    public static IResult Denied(IncidentAccessDecision decision) =>
        decision == IncidentAccessDecision.Hidden ? NotFound() : Results.Forbid();
}
