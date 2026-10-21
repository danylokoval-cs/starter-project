using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

public sealed class ProtectedCreateTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    [Fact]
    public async Task A04_CreateWithoutOwner_UsesAuthenticatedPrincipal()
    {
        var title = $"Owner-{Guid.NewGuid():N}";
        try
        {
            using var client = await factory.LoginAsync("bob", TestSecrets.SeedPassword);
            using var response = await client.PostAsJsonAsync("/api/incidents", new
            {
                title,
                description = "Штучний опис для перевірки власника.",
                severity = "Low",
                occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
            });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(DbSeeder.BobId, await OwnerOfAsync(title));
        }
        finally
        {
            await DeleteByTitleAsync(title);
        }
    }

    [Fact]
    public async Task A05_ForgedOwnerInBody_IsIgnored()
    {
        var title = $"Forged-{Guid.NewGuid():N}";
        try
        {
            using var client = await factory.LoginAsync("bob", TestSecrets.SeedPassword);
            using var response = await client.PostAsJsonAsync("/api/incidents", new
            {
                title,
                description = "Штучний опис для перевірки підробленого власника.",
                severity = "Low",
                occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                ownerUserId = DbSeeder.AliceId,
                userId = DbSeeder.AliceId
            });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(DbSeeder.BobId, await OwnerOfAsync(title));
        }
        finally
        {
            await DeleteByTitleAsync(title);
        }
    }

    private async Task<Guid> OwnerOfAsync(string title)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        return await db.Incidents.Where(item => item.Title == title)
            .Select(item => item.OwnerUserId).SingleAsync();
    }

    private async Task DeleteByTitleAsync(string title)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(item => item.Title == title).ExecuteDeleteAsync();
    }
}
