using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CVManagement.Tests.Integration;

/// Brute-force protection on the auth endpoints. A dedicated factory runs with a tight throttle
/// (3 attempts per 2-second window) while the suite's main factory stays wide open, so these tests
/// observe real rejections without poisoning the login flows every other test depends on.
[Collection(SqlApiCollection.Name)]
public sealed class RateLimitTests
{
    private const int PermitLimit = 3;
    private const int WindowSeconds = 2;

    private readonly SqlApiFixture _fixture;

    public RateLimitTests(SqlApiFixture fixture) => _fixture = fixture;

    private async Task<HttpClient> CreateThrottledClientAsync()
    {
        var previousPermit = Environment.GetEnvironmentVariable("RateLimit__AuthPermitLimit");
        var previousWindow = Environment.GetEnvironmentVariable("RateLimit__AuthWindowSeconds");

        Environment.SetEnvironmentVariable("RateLimit__AuthPermitLimit", PermitLimit.ToString());
        Environment.SetEnvironmentVariable("RateLimit__AuthWindowSeconds", WindowSeconds.ToString());

        try
        {
            // The host reads the environment once, at first use, so the factory is pinned to the
            // tight policy before the process-wide settings are restored for everyone else.
            var factory = _fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
            var client = factory.CreateClient();
            await client.GetAsync("/health");

            return client;
        }
        finally
        {
            Environment.SetEnvironmentVariable("RateLimit__AuthPermitLimit", previousPermit);
            Environment.SetEnvironmentVariable("RateLimit__AuthWindowSeconds", previousWindow);
        }
    }

    [Fact]
    public async Task RepeatedFailedLogins_Answer429_AndRecoverAfterTheWindow()
    {
        var client = await CreateThrottledClientAsync();

        // Within the budget: normal rejections, not throttling.
        for (var attempt = 1; attempt <= PermitLimit; attempt++)
        {
            var within = await client.PostAsJsonAsync("/api/auth/login",
                new { username = "admin", password = "wrong-password" });
            Assert.Equal(HttpStatusCode.Unauthorized, within.StatusCode);
        }

        // One machine guessing passwords exhausts its own budget.
        var throttled = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "wrong-password" });
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);

        // The counter follows the source address, not the username: a different guess is still 429.
        var differentUser = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "other-user", password = "wrong-password" });
        Assert.Equal(HttpStatusCode.TooManyRequests, differentUser.StatusCode);

        // The whole auth surface shares the budget, so switching paths does not reset it.
        var otherPath = await client.PostAsJsonAsync("/api/auth/bootstrap",
            new { username = "path-hopper", password = "path-hopper-password" });
        Assert.Equal(HttpStatusCode.TooManyRequests, otherPath.StatusCode);

        // Refresh belongs to the same surface: guessing against it cannot outpace login guessing.
        var refreshPath = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = "some-credential" });
        Assert.Equal(HttpStatusCode.TooManyRequests, refreshPath.StatusCode);

        // The window is a fixed duration: once it passes, the budget returns.
        await Task.Delay(TimeSpan.FromSeconds(WindowSeconds + 2));

        var recovered = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "wrong-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, recovered.StatusCode);

        // Throttling covers the auth surface only; the rest of the API is not slowed by it.
        var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task RateLimit_GuardsTheWholePolicySurface_NotJustOneEndpointShape()
    {
        var client = await CreateThrottledClientAsync();

        // Exhaust through bootstrap attempts instead of login, then confirm login is 429 too:
        // the budget is per address, so the order and shape of the requests cannot bypass it.
        for (var attempt = 1; attempt <= PermitLimit; attempt++)
        {
            var bootstrap = await client.PostAsJsonAsync("/api/auth/bootstrap",
                new { username = $"decoy-{attempt}", password = "decoy-password-1" });
            Assert.Equal(HttpStatusCode.BadRequest, bootstrap.StatusCode);
        }

        var throttled = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = SqlApiFixture.AdminPassword });
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
    }
}
