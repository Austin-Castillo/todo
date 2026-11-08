using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Instrumentation.AspNetCore;
using Todo.Api.Endpoints;

namespace Todo.Api;

// Traces, metrics and logs to Application Insights with the Azure Monitor OpenTelemetry Distro. See ADR-0008.
public static class Telemetry
{
    // Set only on the Container App. Without it (dotnet run, Compose, CI, the tests) telemetry stays off.
    public const string ConnectionStringKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    // The client ID of the managed identity that signs in to Application Insights (id-todo-app).
    public const string ClientIdKey = "AZURE_CLIENT_ID";

    public static WebApplicationBuilder AddTodoTelemetry(this WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration[ConnectionStringKey];
        if (string.IsNullOrEmpty(connectionString))
        {
            return builder;
        }

        // Fail at startup rather than send nothing: the resource accepts only Entra sign-ins (local auth is
        // disabled), so without the identity every export would be rejected, quietly, in the background.
        var clientId = builder.Configuration[ClientIdKey];
        if (string.IsNullOrEmpty(clientId))
        {
            throw new InvalidOperationException(
                $"{ConnectionStringKey} is set but {ClientIdKey} isn't: telemetry signs in with a managed identity.");
        }

        // Instrumentation for incoming requests, HttpClient and SqlClient, an exporter for traces, metrics and
        // logs, and the ILogger provider. The connection string says where to send; the identity proves who sends.
        builder.Services.AddOpenTelemetry().UseAzureMonitor(options =>
        {
            options.ConnectionString = connectionString;
            options.Credential = new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId));

            // Keep every trace. Since distro 1.5.0 the default is rate-limited sampling (5 traces per second),
            // which also dropped the first requests after startup, the ones a cold start makes interesting. This
            // app's traffic is tiny, and every failing request should be findable.
            options.SamplingRatio = 1.0f;
            options.TracesPerSecond = null;
        });

        // The probes call /alive every few seconds while a replica runs: no request rows for them.
        builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
            options.Filter = context => !HealthEndpoints.IsHealthRequest(context.Request.Path));

        return builder;
    }
}
