using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

public sealed class CreateIncidentTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private Task<HttpClient> LoginAliceAsync() =>
        factory.LoginAsync("alice", TestSecrets.SeedPassword);

    [Fact]
    public async Task Post_WithInvalidDto_Returns400WithFieldErrors()
    {
        using var client = await LoginAliceAsync();
        var body = new
        {
            title = "   ",
            description = "Штучний опис для перевірки.",
            severity = "7",
            occurredAtUtc = DateTimeOffset.UtcNow.AddYears(1)
        };

        using var response = await client.PostAsJsonAsync("/api/incidents", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var errors = document.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("title", out _));
        Assert.True(errors.TryGetProperty("severity", out _));
        Assert.True(errors.TryGetProperty("occurredAtUtc", out _));
        Assert.DoesNotContain("Exception", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_WithDuplicateActiveTitle_Returns409()
    {
        using var client = await LoginAliceAsync();
        var title = $"Regression-{Guid.NewGuid():N}";
        var body = new
        {
            title,
            description = "Штучний опис для перевірки конфлікту.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        try
        {
            using var first = await client.PostAsJsonAsync("/api/incidents", body);
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);

            using var second = await client.PostAsJsonAsync("/api/incidents", body);
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Equal("application/problem+json",
                second.Content.Headers.ContentType?.MediaType);

            var json = await second.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Exception", json, StringComparison.Ordinal);
        }
        finally
        {
            await DeleteByTitleAsync(title);
        }
    }

    [Fact]
    public async Task Post_HighSeverityDescriptionBoundary_Rejects39AndAccepts40()
    {
        using var client = await LoginAliceAsync();
        var shortTitle = $"Regression-{Guid.NewGuid():N}";
        var longTitle = $"Regression-{Guid.NewGuid():N}";

        try
        {
            using var shortResponse = await client.PostAsJsonAsync("/api/incidents", new
            {
                title = shortTitle,
                description = new string('x', 39),
                severity = "High",
                occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
            });
            Assert.Equal(HttpStatusCode.BadRequest, shortResponse.StatusCode);
            using var document = JsonDocument.Parse(
                await shortResponse.Content.ReadAsStringAsync());
            Assert.True(document.RootElement.GetProperty("errors")
                .TryGetProperty("description", out _));

            using var longResponse = await client.PostAsJsonAsync("/api/incidents", new
            {
                title = longTitle,
                description = new string('x', 40),
                severity = "High",
                occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
            });
            Assert.Equal(HttpStatusCode.Created, longResponse.StatusCode);
        }
        finally
        {
            await DeleteByTitleAsync(shortTitle);
            await DeleteByTitleAsync(longTitle);
        }
    }

    private async Task DeleteByTitleAsync(string title)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(item => item.Title == title).ExecuteDeleteAsync();
    }
}
