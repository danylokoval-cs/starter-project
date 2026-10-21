using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SecureLab.Api.Tests;

public sealed class LoginHardeningTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private const string WrongPassword = "Wrong-Password-1!";

    private static WebApplicationFactoryClientOptions Https() => new()
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
    };

    [Fact]
    public async Task A07_UnknownUserAndWrongPassword_LookTheSameOutside()
    {
        using var client = factory.CreateClient(Https());

        using var unknown = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = $"nobody-{Guid.NewGuid():N}", password = WrongPassword });
        using var wrong = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "alice", password = WrongPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(unknown.StatusCode, wrong.StatusCode);
        Assert.Equal(
            TestSecrets.WithoutTraceId(await unknown.Content.ReadAsStringAsync()),
            TestSecrets.WithoutTraceId(await wrong.Content.ReadAsStringAsync()));
        Assert.Equal(unknown.Content.Headers.ContentType?.ToString(), wrong.Content.Headers.ContentType?.ToString());
        Assert.False(unknown.Headers.Contains("Set-Cookie"));
        Assert.False(wrong.Headers.Contains("Set-Cookie"));
        Assert.Empty(unknown.Headers.WwwAuthenticate);
        Assert.Empty(wrong.Headers.WwwAuthenticate);
    }

    [Fact]
    public async Task Login_SetsCookieWithExpectedFlags()
    {
        using var client = factory.CreateClient(Https());

        using var response = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "alice", password = TestSecrets.SeedPassword });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal))
            .ToLowerInvariant();
        Assert.Contains("httponly", cookie);
        Assert.Contains("secure", cookie);
        Assert.Contains("samesite=lax", cookie);
        Assert.Contains("path=/", cookie);
    }

    [Fact]
    public async Task Login_RateLimit_Returns429ThenRecovers()
    {
        await using var host = factory.WithWebHostBuilder(_ => { });
        using var client = host.CreateClient(Https());

        var rejected = false;
        for (var attempt = 0; attempt < 30 && !rejected; attempt++)
        {
            using var response = await client.PostAsJsonAsync("/api/auth/login",
                new { userName = "nobody", password = WrongPassword });
            rejected = response.StatusCode == HttpStatusCode.TooManyRequests;
        }
        Assert.True(rejected, "Очікувався 429 після перевищення ліміту.");

        await Task.Delay(TimeSpan.FromSeconds(10) + TimeSpan.FromMilliseconds(500));

        using var after = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "nobody", password = WrongPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }
}