using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Identity;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Scaffolding;

public static class Lab03Identity
{
    public static void AddLab03Identity(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 10;
        }).AddEntityFrameworkStores<SecureLabDbContext>().AddDefaultTokenProviders();
        builder.Services.AddAuthorization();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("login", httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromSeconds(10),
                    QueueLimit = 0
                }));
        });
    }

    public static void MapLab03Identity(this WebApplication app)
    {
        app.MapPost("/api/auth/register", async (
            RegistrationInput input, UserManager<ApplicationUser> users,
            HttpContext httpContext, ILogger<Program> logger) =>
        {
            var userName = input.UserName?.Trim() ?? "";
            var email = input.Email?.Trim() ?? "";
            var displayName = input.DisplayName?.Trim() ?? "";
            var password = input.Password ?? "";

            var errors = new Dictionary<string, string[]>();
            if (userName.Length == 0 || userName.Length > 256)
                errors["userName"] = ["Ім'я користувача обов'язкове, не довше 256 символів."];
            if (email.Length == 0 || email.Length > 256)
                errors["email"] = ["Email обов'язковий, не довший за 256 символів."];
            if (displayName.Length == 0 || displayName.Length > 120)
                errors["displayName"] = ["Відображуване ім'я обов'язкове, не довше 120 символів."];
            if (password.Length == 0 || password.Length > 128)
                errors["password"] = ["Пароль обов'язковий, не довший за 128 символів."];
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = userName, Email = email, DisplayName = displayName
            };
            var result = await users.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                var duplicate = result.Errors.Any(error =>
                    error.Code is "DuplicateUserName" or "DuplicateEmail");
                logger.LogWarning("{EventType} {Result} {TraceId}",
                    "register", duplicate ? "duplicate" : "rejected", httpContext.TraceIdentifier);
                if (duplicate)
                    return Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Користувач уже існує",
                        detail: "Обліковий запис із такими даними вже зареєстровано.");
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["registration"] = ["Реєстрацію відхилено. Перевірте дані: пароль має містити щонайменше 10 символів, велику й малу літеру, цифру та спеціальний символ."]
                });
            }

            var role = await users.AddToRoleAsync(user, DbSeeder.ReporterRole);
            if (!role.Succeeded) throw new InvalidOperationException("Default role assignment failed.");
            logger.LogInformation("{EventType} {Result} {TraceId}",
                "register", "success", httpContext.TraceIdentifier);
            return Results.Created("/api/me", new RegisterResponse(user.Id, user.UserName!, user.DisplayName));
        });

        app.MapPost("/api/auth/login", async (
            LoginInput input, SignInManager<ApplicationUser> signIn,
            HttpContext httpContext, ILogger<Program> logger) =>
        {
            var succeeded = false;
            if (!string.IsNullOrWhiteSpace(input.UserName) && !string.IsNullOrEmpty(input.Password))
            {
                var result = await signIn.PasswordSignInAsync(input.UserName, input.Password, false, false);
                succeeded = result.Succeeded;
            }
            if (succeeded)
                logger.LogInformation("{EventType} {Result} {TraceId}",
                    "login", "success", httpContext.TraceIdentifier);
            else
                logger.LogWarning("{EventType} {Result} {TraceId}",
                    "login", "failure", httpContext.TraceIdentifier);
            return succeeded ? Results.NoContent() : Results.Unauthorized();
        }).RequireRateLimiting("login");

        app.MapPost("/api/auth/logout", async (
            SignInManager<ApplicationUser> signIn, HttpContext httpContext, ILogger<Program> logger) =>
        {
            await signIn.SignOutAsync();
            logger.LogInformation("{EventType} {Result} {TraceId}",
                "logout", "success", httpContext.TraceIdentifier);
            return Results.NoContent();
        });

        app.MapGet("/api/me", (HttpContext httpContext) =>
        {
            var principal = httpContext.User;
            if (!Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Не вдалося визначити поточного користувача.");
            return Results.Ok(new CurrentUserResponse(
                userId, principal.Identity?.Name, principal.FindFirst(ClaimTypes.Role)?.Value));
        }).RequireAuthorization();

        app.MapGet("/api/auth/session-check", () => Results.NoContent()).RequireAuthorization();
    }
}

public sealed record RegistrationInput(string? UserName, string? Email, string? DisplayName, string? Password);
public sealed record LoginInput(string? UserName, string? Password);
public sealed record RegisterResponse(Guid Id, string UserName, string DisplayName);
public sealed record CurrentUserResponse(Guid UserId, string? UserName, string? Role);
