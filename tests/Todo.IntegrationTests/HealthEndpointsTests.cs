using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Todo.Api.Data;
using Todo.Api.Endpoints;

namespace Todo.IntegrationTests;

// The probes' endpoint and the local database check. See ADR-0009.
public class HealthEndpointsTests(TodoApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Production, because that's where the probes call it.
    [Fact]
    public async Task Alive_InProduction_ReturnsHealthy()
    {
        // Arrange
        var client = fixture.CreateClient(host => host.UseEnvironment("Production"));

        // Act
        var response = await client.GetAsync(HealthEndpoints.AlivePath, Ct);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(Ct));
    }

    // Any check without the "live" tag, the database check included, must not affect liveness: restarting the
    // app can't fix a failing dependency. An extra failing check stands in for the database being down.
    [Fact]
    public async Task Alive_IgnoresChecksNotTaggedLive_WhileHealthReportsThem()
    {
        // Arrange
        var client = fixture.CreateClient(host => host.ConfigureTestServices(services =>
            services.AddHealthChecks().AddCheck("failing", () => HealthCheckResult.Unhealthy())));

        // Act
        var alive = await client.GetAsync(HealthEndpoints.AlivePath, Ct);
        var health = await client.GetAsync(HealthEndpoints.HealthPath, Ct);

        // Assert
        Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, health.StatusCode);
        Assert.Equal("Unhealthy", await health.Content.ReadAsStringAsync(Ct));
    }

    // With the test above, this pins that the database is checked by /health and never by /alive.
    [Fact]
    public void DatabaseCheck_IsRegistered_AndNotTaggedLive()
    {
        // Arrange
        var options = fixture.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

        // Act
        var databaseCheck = Assert.Single(options.Registrations, r => r.Name == nameof(TodoDbContext));

        // Assert
        Assert.DoesNotContain("live", databaseCheck.Tags);
    }

    [Fact]
    public async Task Health_InDevelopment_WithTheDatabaseUp_ReturnsHealthy()
    {
        // Act
        var response = await fixture.CreateClient().GetAsync(HealthEndpoints.HealthPath, Ct);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Health_InProduction_IsNotMapped()
    {
        // Arrange
        var client = fixture.CreateClient(host => host.UseEnvironment("Production"));

        // Act
        var response = await client.GetAsync(HealthEndpoints.HealthPath, Ct);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
