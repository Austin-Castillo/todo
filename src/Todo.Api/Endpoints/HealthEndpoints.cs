using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Todo.Api.Data;

namespace Todo.Api.Endpoints;

// Health endpoints for the Container Apps probes, named as in Aspire's service defaults template. See ADR-0009.
public static class HealthEndpoints
{
    // Liveness: is the process able to answer HTTP? Runs only the checks tagged "live", so never the database:
    // restarting the app can't fix a database outage. The startup, liveness and readiness probes all call it.
    public const string AlivePath = "/alive";

    // Every check, the database included. Development only, for checking the database locally.
    public const string HealthPath = "/health";

    private const string LiveTag = "live";

    public static IServiceCollection AddTodoHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: [LiveTag])
            // Opens a connection with the app's DbContext settings (Database.CanConnectAsync). Not tagged "live".
            .AddDbContextCheck<TodoDbContext>();

        return services;
    }

    public static WebApplication MapTodoHealthChecks(this WebApplication app)
    {
        // In every environment, since the probes need it in production. Its response is only the status.
        app.MapHealthChecks(AlivePath, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(LiveTag),
        });

        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks(HealthPath);
        }

        return app;
    }

    // For the telemetry filter (ADR-0008): requests from the probes aren't traffic anyone needs to see.
    public static bool IsHealthRequest(PathString path) =>
        path.StartsWithSegments(AlivePath) || path.StartsWithSegments(HealthPath);
}
