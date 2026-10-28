using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;
using SecureLab.Api.Scaffolding;

namespace SecureLab.Api.Tests;

public sealed class AuthorizationTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static readonly Guid AliceSecondIncidentId =
        Guid.Parse("20000000-0000-0000-0000-000000000005");
    private static readonly Guid BobSecondIncidentId =
        Guid.Parse("20000000-0000-0000-0000-000000000004");
    private static readonly Guid CommentOnAliceId =
        Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid CommentOnBobId =
        Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid InternalCommentOnAliceId =
        Guid.Parse("30000000-0000-0000-0000-000000000003");

    [Fact]
    public async Task A01_Anonymous_ProtectedRoutes_Return401WithoutRedirect()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var unknownId = Guid.NewGuid();
        var requests = new (HttpMethod Method, string Path)[]
        {
            (HttpMethod.Get, "/api/incidents"),
            (HttpMethod.Get, $"/api/incidents/{DbSeeder.AliceIncidentId}"),
            (HttpMethod.Get, "/api/incidents/search?q=test"),
            (HttpMethod.Get, "/api/incidents/severity-summary"),
            (HttpMethod.Get, $"/api/incident-comments/{CommentOnAliceId}"),
            (HttpMethod.Put, $"/api/incidents/{unknownId}"),
            (HttpMethod.Delete, $"/api/incidents/{unknownId}"),
            (HttpMethod.Post, $"/api/incidents/{unknownId}/triage")
        };

        foreach (var (method, path) in requests)
        {
            using var request = new HttpRequestMessage(method, path);
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Null(response.Headers.Location);
            Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task A02_Owner_ReadsOwnDetails()
    {
        using var alice = await LoginAsync("alice");

        using var response = await alice.GetAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var details = await response.Content.ReadFromJsonAsync<IncidentDetailsResponse>();
        Assert.NotNull(details);
        Assert.Equal(DbSeeder.AliceIncidentId, details.Id);
        Assert.Equal("Аліса Коваль", details.OwnerDisplayName);
    }

    [Fact]
    public async Task A03_NonOwner_ForeignDetails_Returns404WithoutData()
    {
        using var alice = await LoginAsync("alice");

        using var response = await alice.GetAsync($"/api/incidents/{DbSeeder.BobIncidentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Інцидент не знайдено", document.RootElement.GetProperty("title").GetString());
        Assert.False(document.RootElement.TryGetProperty("detail", out _));
        Assert.False(document.RootElement.TryGetProperty("description", out _));
        Assert.False(document.RootElement.TryGetProperty("ownerDisplayName", out _));
        Assert.False(document.RootElement.TryGetProperty("comments", out _));
    }

    [Fact]
    public async Task A03R_Analyst_ReadsForeignIncidentWithInternalComment()
    {
        using var morgan = await LoginAsync("morgan");

        var details = await morgan.GetFromJsonAsync<IncidentDetailsResponse>(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.NotNull(details);
        Assert.Contains(details.Comments, comment => comment.Id == InternalCommentOnAliceId);
    }

    [Fact]
    public async Task A03R_Analyst_CannotUpdateForeignContent()
    {
        using var alice = await LoginAsync("alice");
        using var morgan = await LoginAsync("morgan");
        var id = await CreateAsync(alice, "Analyst-edit");
        try
        {
            var before = await StateAsync(id);

            using var response = await morgan.PutAsJsonAsync(
                $"/api/incidents/{id}", ValidUpdate("Analyst rewrite"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(before, await StateAsync(id));
        }
        finally
        {
            await DeleteAsync(id);
        }
    }

    [Fact]
    public async Task A04_NonOwner_UpdateAndDelete_Return404AndKeepState()
    {
        using var alice = await LoginAsync("alice");
        using var bob = await LoginAsync("bob");
        var id = await CreateAsync(bob, "Bob-owned");
        try
        {
            var before = await StateAsync(id);

            using var update = await alice.PutAsJsonAsync(
                $"/api/incidents/{id}", ValidUpdate("Alice rewrite"));
            using var delete = await alice.DeleteAsync($"/api/incidents/{id}");

            Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
            Assert.Equal(before, await StateAsync(id));
        }
        finally
        {
            await DeleteAsync(id);
        }
    }

    [Fact]
    public async Task A06_Reporter_Triage_IsDenied_AndStatusStaysNew()
    {
        using var alice = await LoginAsync("alice");
        using var bob = await LoginAsync("bob");
        var id = await CreateAsync(alice, "Triage-denied");
        try
        {
            var before = await StateAsync(id);

            using var own = await alice.PostAsJsonAsync(
                $"/api/incidents/{id}/triage", new { status = "Closed", role = "Analyst" });
            using var other = await bob.PostAsync($"/api/incidents/{id}/triage", null);

            Assert.Equal(HttpStatusCode.Forbidden, own.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
            var after = await StateAsync(id);
            Assert.NotNull(after);
            Assert.Equal(IncidentStatus.New, after.Status);
            Assert.Equal(before, after);
        }
        finally
        {
            await DeleteAsync(id);
        }
    }

    [Fact]
    public async Task A07_Analyst_Triage_IsAllowed_AndRecordsHistory()
    {
        using var alice = await LoginAsync("alice");
        using var morgan = await LoginAsync("morgan");
        var id = await CreateAsync(alice, "Triage-allowed");
        try
        {
            using var first = await morgan.PostAsync($"/api/incidents/{id}/triage", null);
            using var second = await morgan.PostAsync($"/api/incidents/{id}/triage", null);

            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            var after = await StateAsync(id);
            Assert.NotNull(after);
            Assert.Equal(IncidentStatus.Triaged, after.Status);
            var history = Assert.Single(await HistoryAsync(id));
            Assert.Equal(IncidentStatus.New, history.OldStatus);
            Assert.Equal(IncidentStatus.Triaged, history.NewStatus);
            Assert.Equal(DbSeeder.MorganId, history.ChangedByUserId);
        }
        finally
        {
            await DeleteAsync(id);
        }
    }

    [Fact]
    public async Task A08a_Update_IgnoresForgedProtectedFields()
    {
        using var alice = await LoginAsync("alice");
        var id = await CreateAsync(alice, "Forged-update");
        try
        {
            using var response = await alice.PutAsJsonAsync($"/api/incidents/{id}", new
            {
                title = "Updated by owner",
                description = "Оновлений штучний опис для перевірки.",
                ownerUserId = DbSeeder.BobId,
                role = "Analyst",
                status = "Closed",
                isTransitionAllowed = true
            });

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            var after = await StateAsync(id);
            Assert.NotNull(after);
            Assert.Equal("Updated by owner", after.Title);
            Assert.Equal(DbSeeder.AliceId, after.OwnerUserId);
            Assert.Equal(IncidentStatus.New, after.Status);
        }
        finally
        {
            await DeleteAsync(id);
        }
    }

    [Fact]
    public async Task A09_DeniedTriage_WritesSafeAuditEvent()
    {
        using var alice = await LoginAsync("alice");
        var id = await CreateAsync(alice, "Audit-denied");
        try
        {
            using var response = await alice.PostAsJsonAsync(
                $"/api/incidents/{id}/triage", new { status = "Closed", note = "SECRET-NOTE-MARKER" });

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var entry = Assert.Single(
                factory.Logs.Messages,
                message => message.Contains("authorization.denied", StringComparison.Ordinal)
                    && message.Contains(id.ToString(), StringComparison.Ordinal));
            Assert.Contains($"subject={DbSeeder.AliceId}", entry, StringComparison.Ordinal);
            Assert.Contains("action=incident.triage", entry, StringComparison.Ordinal);
            Assert.Contains("result=denied", entry, StringComparison.Ordinal);
            Assert.Contains("occurredAtUtc=", entry, StringComparison.Ordinal);
            Assert.Contains("traceId=", entry, StringComparison.Ordinal);
            Assert.DoesNotContain("SECRET-NOTE-MARKER", entry, StringComparison.Ordinal);
            Assert.DoesNotContain("Cookie", entry, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                factory.Logs.Messages,
                message => message.Contains(TestSecrets.SeedPassword, StringComparison.Ordinal));
        }
        finally
        {
            await DeleteAsync(id);
        }
    }

    [Fact]
    public async Task A10_ForeignAndUnknownId_Details_AreIndistinguishable()
    {
        using var alice = await LoginAsync("alice");

        using var foreign = await alice.GetAsync($"/api/incidents/{DbSeeder.BobIncidentId}");
        using var unknown = await alice.GetAsync($"/api/incidents/{Guid.NewGuid()}");

        Assert.Equal(unknown.StatusCode, foreign.StatusCode);
        Assert.Equal(
            unknown.Content.Headers.ContentType?.ToString(),
            foreign.Content.Headers.ContentType?.ToString());
        Assert.Equal(
            TestSecrets.WithoutTraceId(await unknown.Content.ReadAsStringAsync()),
            TestSecrets.WithoutTraceId(await foreign.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task A10_ForeignAndUnknownId_Update_AreIndistinguishable()
    {
        using var alice = await LoginAsync("alice");
        using var bob = await LoginAsync("bob");
        var id = await CreateAsync(bob, "Bob-update");
        try
        {
            using var foreign = await alice.PutAsJsonAsync(
                $"/api/incidents/{id}", ValidUpdate("Same body"));
            using var unknown = await alice.PutAsJsonAsync(
                $"/api/incidents/{Guid.NewGuid()}", ValidUpdate("Same body"));

            Assert.Equal(unknown.StatusCode, foreign.StatusCode);
            Assert.Equal(
                unknown.Content.Headers.ContentType?.ToString(),
                foreign.Content.Headers.ContentType?.ToString());
            Assert.Equal(
                TestSecrets.WithoutTraceId(await unknown.Content.ReadAsStringAsync()),
                TestSecrets.WithoutTraceId(await foreign.Content.ReadAsStringAsync()));
        }
        finally
        {
            await DeleteAsync(id);
        }
    }

    [Fact]
    public async Task A11_Search_ReturnsOnlyAllowedIncidents()
    {
        using var alice = await LoginAsync("alice");

        var foreignQuery = await alice.GetFromJsonAsync<List<SearchIncidentResponse>>(
            "/api/incidents/search?q=O%27Brien");
        var ownQuery = await alice.GetFromJsonAsync<List<SearchIncidentResponse>>(
            "/api/incidents/search?q=навчальн");

        Assert.NotNull(foreignQuery);
        Assert.Empty(foreignQuery);
        Assert.NotNull(ownQuery);
        Assert.Contains(ownQuery, row => row.Id == AliceSecondIncidentId);
        Assert.DoesNotContain(ownQuery, row => row.Id == BobSecondIncidentId);
    }

    [Fact]
    public async Task A11_SeveritySummary_CountsOnlyAllowedIncidents()
    {
        using var alice = await LoginAsync("alice");
        using var morgan = await LoginAsync("morgan");

        var aliceSummary = await alice.GetFromJsonAsync<List<IncidentSeveritySummaryResponse>>(
            "/api/incidents/severity-summary");
        var morganSummary = await morgan.GetFromJsonAsync<List<IncidentSeveritySummaryResponse>>(
            "/api/incidents/severity-summary");

        Assert.NotNull(aliceSummary);
        Assert.NotNull(morganSummary);
        Assert.Equal(
            await CountAsync(item => item.OwnerUserId == DbSeeder.AliceId),
            aliceSummary.Sum(item => item.Count));
        Assert.Equal(await CountAsync(_ => true), morganSummary.Sum(item => item.Count));
        Assert.DoesNotContain(aliceSummary, item => item.Severity == "High");
    }

    [Fact]
    public async Task A12_ChildComment_FollowsParentIncidentVisibility()
    {
        using var alice = await LoginAsync("alice");
        using var morgan = await LoginAsync("morgan");

        using var ownPublic = await alice.GetAsync($"/api/incident-comments/{CommentOnAliceId}");
        using var foreign = await alice.GetAsync($"/api/incident-comments/{CommentOnBobId}");
        using var internalOwn = await alice.GetAsync(
            $"/api/incident-comments/{InternalCommentOnAliceId}");
        using var unknown = await alice.GetAsync($"/api/incident-comments/{Guid.NewGuid()}");
        using var analyst = await morgan.GetAsync(
            $"/api/incident-comments/{InternalCommentOnAliceId}");
        var aliceDetails = await alice.GetFromJsonAsync<IncidentDetailsResponse>(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.OK, ownPublic.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, internalOwn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, analyst.StatusCode);
        Assert.Equal(
            TestSecrets.WithoutTraceId(await unknown.Content.ReadAsStringAsync()),
            TestSecrets.WithoutTraceId(await foreign.Content.ReadAsStringAsync()));
        Assert.Equal(
            TestSecrets.WithoutTraceId(await unknown.Content.ReadAsStringAsync()),
            TestSecrets.WithoutTraceId(await internalOwn.Content.ReadAsStringAsync()));
        Assert.NotNull(aliceDetails);
        Assert.DoesNotContain(aliceDetails.Comments, comment => comment.Id == InternalCommentOnAliceId);
        Assert.Contains(aliceDetails.Comments, comment => comment.Id == CommentOnAliceId);
    }

    [Fact]
    public async Task Administrator_HasNoRightsOverIncidentContent()
    {
        using var admin = await LoginAsync("admin");

        var list = await admin.GetFromJsonAsync<List<IncidentListItemResponse>>("/api/incidents");
        using var details = await admin.GetAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.NotNull(list);
        Assert.Empty(list);
        Assert.Equal(HttpStatusCode.NotFound, details.StatusCode);
    }

    [Fact]
    public async Task Delete_OwnNewIncident_Returns204AndRemovesIt()
    {
        using var alice = await LoginAsync("alice");
        var id = await CreateAsync(alice, "Delete-own");
        try
        {
            using var response = await alice.DeleteAsync($"/api/incidents/{id}");

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Null(await StateAsync(id));
        }
        finally
        {
            await DeleteAsync(id);
        }
    }

    [Fact]
    public async Task UpdateAndDelete_AfterTriage_AreForbiddenForOwnerAndAnalyst()
    {
        using var alice = await LoginAsync("alice");
        using var morgan = await LoginAsync("morgan");
        var id = await CreateAsync(alice, "After-triage");
        try
        {
            using var triage = await morgan.PostAsync($"/api/incidents/{id}/triage", null);
            Assert.Equal(HttpStatusCode.NoContent, triage.StatusCode);
            var before = await StateAsync(id);

            using var ownerUpdate = await alice.PutAsJsonAsync(
                $"/api/incidents/{id}", ValidUpdate("Owner after triage"));
            using var ownerDelete = await alice.DeleteAsync($"/api/incidents/{id}");
            using var analystDelete = await morgan.DeleteAsync($"/api/incidents/{id}");

            Assert.Equal(HttpStatusCode.Forbidden, ownerUpdate.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, ownerDelete.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, analystDelete.StatusCode);
            Assert.Equal(before, await StateAsync(id));
        }
        finally
        {
            await DeleteAsync(id);
        }
    }

    private static readonly Dictionary<string, string> SharedSessionCookies = [];

    private async Task<HttpClient> LoginAsync(string userName)
    {
        if (!SharedSessionCookies.TryGetValue(userName, out var cookie))
        {
            using var loginClient = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
                HandleCookies = false
            });
            using var login = await loginClient.PostAsJsonAsync(
                "/api/auth/login", new { userName, password = TestSecrets.SeedPassword });
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
            cookie = string.Join("; ", login.Headers.GetValues("Set-Cookie")
                .Select(value => value.Split(';')[0]));
            SharedSessionCookies[userName] = cookie;
        }

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    private static object ValidUpdate(string title) => new
    {
        title,
        description = "Оновлений штучний опис для перевірки авторизації."
    };

    private static async Task<Guid> CreateAsync(HttpClient client, string prefix)
    {
        using var response = await client.PostAsJsonAsync("/api/incidents", new
        {
            title = $"{prefix}-{Guid.NewGuid():N}",
            description = "Штучний опис для перевірки авторизації.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreatedIncidentResponse>();
        Assert.NotNull(created);
        return created.Id;
    }

    private async Task<IncidentState?> StateAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        return await db.Incidents
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new IncidentState(
                item.Title, item.Description, item.Status, item.OwnerUserId, item.UpdatedAtUtc))
            .SingleOrDefaultAsync();
    }

    private async Task<List<HistoryRow>> HistoryAsync(Guid incidentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        return await db.IncidentStatusHistory
            .AsNoTracking()
            .Where(item => item.IncidentId == incidentId)
            .Select(item => new HistoryRow(item.OldStatus, item.NewStatus, item.ChangedByUserId))
            .ToListAsync();
    }

    private async Task<int> CountAsync(Expression<Func<Incident, bool>> predicate)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        return await db.Incidents.CountAsync(predicate);
    }

    private async Task DeleteAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(item => item.Id == id).ExecuteDeleteAsync();
    }

    private sealed record IncidentState(
        string Title,
        string Description,
        IncidentStatus Status,
        Guid OwnerUserId,
        DateTimeOffset UpdatedAtUtc);

    private sealed record HistoryRow(
        IncidentStatus OldStatus,
        IncidentStatus NewStatus,
        Guid ChangedByUserId);
}