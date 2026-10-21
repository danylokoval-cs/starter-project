using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

public sealed class RegistrationTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private HttpClient NewClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
    });

    private static string Suffix() => Guid.NewGuid().ToString("N")[..10];

    [Fact]
    public async Task T01_ValidRegistration_Returns201WithSafeBody()
    {
        var suffix = Suffix();
        var password = TestSecrets.NewStrongPassword();
        try
        {
            using var client = NewClient();
            using var response = await client.PostAsJsonAsync("/api/auth/register", new
            {
                userName = $"reg{suffix}", email = $"reg{suffix}@example.test",
                displayName = "Тестовий користувач", password
            });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal("/api/me", response.Headers.Location?.OriginalString);
            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            Assert.Equal($"reg{suffix}", document.RootElement.GetProperty("userName").GetString());
            Assert.DoesNotContain(password, json, StringComparison.Ordinal);
            Assert.DoesNotContain("hash", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("email", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await DeleteUserAsync($"reg{suffix}");
        }
    }

    [Fact]
    public async Task T02_WeakPassword_Returns400WithoutLeakingRules()
    {
        var suffix = Suffix();
        var weak = "short1!";
        using var client = NewClient();
        using var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            userName = $"weak{suffix}", email = $"weak{suffix}@example.test",
            displayName = "Тестовий користувач", password = weak
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(weak, json, StringComparison.Ordinal);
        Assert.DoesNotContain("PasswordTooShort", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", json, StringComparison.Ordinal);
        await DeleteUserAsync($"weak{suffix}");
    }

    [Fact]
    public async Task Register_MissingFields_Returns400WithFieldErrors()
    {
        using var client = NewClient();
        using var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            userName = "   ", email = "", displayName = "x", password = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = document.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("userName", out _));
        Assert.True(errors.TryGetProperty("email", out _));
        Assert.True(errors.TryGetProperty("password", out _));
    }

    [Fact]
    public async Task Register_DuplicateUserNameOrEmail_Returns409WithSameBody()
    {
        var suffix = Suffix();
        var password = TestSecrets.NewStrongPassword();
        try
        {
            using var client = NewClient();
            using var first = await client.PostAsJsonAsync("/api/auth/register", new
            {
                userName = $"dup{suffix}", email = $"dup{suffix}@example.test",
                displayName = "Тестовий користувач", password
            });
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);

            using var sameName = await client.PostAsJsonAsync("/api/auth/register", new
            {
                userName = $"dup{suffix}", email = $"other{suffix}@example.test",
                displayName = "Тестовий користувач", password
            });
            using var sameEmail = await client.PostAsJsonAsync("/api/auth/register", new
            {
                userName = $"other{suffix}", email = $"dup{suffix}@example.test",
                displayName = "Тестовий користувач", password
            });

            Assert.Equal(HttpStatusCode.Conflict, sameName.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, sameEmail.StatusCode);
            Assert.Equal("application/problem+json", sameName.Content.Headers.ContentType?.MediaType);
            var nameBody = TestSecrets.WithoutTraceId(await sameName.Content.ReadAsStringAsync());
            Assert.Equal(nameBody, TestSecrets.WithoutTraceId(await sameEmail.Content.ReadAsStringAsync()));
            Assert.DoesNotContain(password, nameBody, StringComparison.Ordinal);
            Assert.DoesNotContain("Duplicate", nameBody, StringComparison.Ordinal);
        }
        finally
        {
            await DeleteUserAsync($"dup{suffix}");
            await DeleteUserAsync($"other{suffix}");
        }
    }

    private async Task DeleteUserAsync(string userName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByNameAsync(userName);
        if (user is not null) await users.DeleteAsync(user);
    }
}