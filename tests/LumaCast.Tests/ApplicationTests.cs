using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace LumaCast.Tests;

[TestClass]
public sealed class ApplicationTests
{
    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));

    [TestMethod]
    public async Task LiveKitStatusReturnsPeerToPeerWhenCredentialsAreEmpty()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/api/livekit/status");
        var status = await response.Content.ReadFromJsonAsync<LiveKitStatusContract>();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(status);
        Assert.IsFalse(status.Configured);
        Assert.AreEqual("peer-to-peer", status.Provider);
        AssertSecurityHeaders(response);
    }

    [TestMethod]
    public async Task LiveKitStatusUsesValuesFromAppSettingsSection()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["LiveKit:Url"] = "wss://example.livekit.cloud",
                    ["LiveKit:ApiKey"] = "api-key",
                    ["LiveKit:ApiSecret"] = "api-secret"
                }));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/api/livekit/status");
        var status = await response.Content.ReadFromJsonAsync<LiveKitStatusContract>();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(status);
        Assert.IsTrue(status.Configured);
        Assert.AreEqual("livekit", status.Provider);
    }

    [TestMethod]
    public async Task HealthCheckReturnsHealthy()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/healthz");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("Healthy", await response.Content.ReadAsStringAsync());
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        Assert.AreEqual("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.AreEqual("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.IsTrue(response.Headers.Contains("Content-Security-Policy"));
        Assert.IsTrue(response.Headers.Contains("Permissions-Policy"));
    }

    private sealed record LiveKitStatusContract(bool Configured, string Provider);
}
