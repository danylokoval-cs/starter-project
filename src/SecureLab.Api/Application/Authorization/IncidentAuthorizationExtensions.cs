using Microsoft.AspNetCore.Authorization;
using SecureLab.Api.Application.Incidents;

namespace SecureLab.Api.Application.Authorization;

public static class IncidentAuthorizationExtensions
{
    public static IServiceCollection AddIncidentAuthorization(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, IncidentAuthorizationHandler>();
        services.AddScoped<IncidentAuthorizer>();
        services.AddScoped<IncidentCommands>();
        return services;
    }
}
