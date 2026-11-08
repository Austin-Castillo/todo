using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Todo.Api;
using Todo.Api.Endpoints;
using static Todo.IntegrationTests.ApiTestHelpers;

namespace Todo.IntegrationTests;

// Telemetry is off unless configured, and when on it records requests with their SQL calls but not the probes.
// See ADR-0008.
public class TelemetryTests(TodoApiFixture fixture)
{
    // Points at a closed local port, so nothing leaves the machine.
    private const string FakeConnectionString =
        "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=http://127.0.0.1:1/";

    private const string FakeClientId = "00000000-0000-0000-0000-000000000001";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Telemetry_WithoutConnectionString_IsOff()
    {
        // Act
        var tracerProvider = fixture.Services.GetService<TracerProvider>();

        // Assert
        Assert.Null(tracerProvider);
    }

    [Fact]
    public void Telemetry_WithConnectionStringButNoClientId_FailsAtStartup()
    {
        // Act
        var exception = Record.Exception(() =>
            fixture.CreateServer(host => host.UseSetting(Telemetry.ConnectionStringKey, FakeConnectionString)));

        // Assert: the startup exception may arrive wrapped by the test host.
        Assert.NotNull(exception);
        Assert.Contains(Telemetry.ClientIdKey, exception.ToString());
    }

    [Fact]
    public async Task Telemetry_WhenOn_TracesARequestWithItsSqlCalls_ButNotTheProbe()
    {
        // Arrange: the real configuration, plus an in-memory exporter to read what would be sent.
        var exported = new List<Activity>();
        var server = fixture.CreateServer(host =>
        {
            host.UseSetting(Telemetry.ConnectionStringKey, FakeConnectionString);
            host.UseSetting(Telemetry.ClientIdKey, FakeClientId);
            host.ConfigureTestServices(services =>
            {
                services.Configure<AzureMonitorOptions>(options => options.DisableOfflineStorage = true);
                services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(exported));
            });
        });
        var client = server.CreateClient();

        // Every test server in this process reports to the same listeners, so each request carries its own trace
        // ID in a W3C traceparent header, as an upstream caller would, and the assertions look only at those.
        var listTrace = ActivityTraceId.CreateRandom();
        var probeTrace = ActivityTraceId.CreateRandom();

        // Act
        await SendWithTrace(client, $"/api/lists/{MissingId}", listTrace);
        await SendWithTrace(client, HealthEndpoints.AlivePath, probeTrace);
        var listSpans = await WaitForSpans(exported, listTrace, span => span.Kind == ActivityKind.Server);

        // Assert: the request, and the SQL query it ran as a child span in the same trace.
        var request = Assert.Single(listSpans, span => span.Kind == ActivityKind.Server);
        Assert.Equal($"/api/lists/{MissingId}", request.GetTagItem("url.path"));
        Assert.Contains(listSpans, span => span.Kind == ActivityKind.Client && span.ParentSpanId == request.SpanId);

        Assert.DoesNotContain(Snapshot(exported), span => span.TraceId == probeTrace);
    }

    private static async Task SendWithTrace(HttpClient client, string path, ActivityTraceId traceId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");
        using var response = await client.SendAsync(request, Ct);
    }

    // A server span ends just after its response is sent, so the test can see the response first.
    private static async Task<List<Activity>> WaitForSpans(
        List<Activity> exported, ActivityTraceId traceId, Func<Activity, bool> until)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var spans = Snapshot(exported).Where(span => span.TraceId == traceId).ToList();
            if (spans.Any(until))
            {
                return spans;
            }

            await Task.Delay(100, Ct);
        }

        return Snapshot(exported).Where(span => span.TraceId == traceId).ToList();
    }

    // The exporter adds to the list from the threads that end each span, so a copy can race an add: retry it.
    private static List<Activity> Snapshot(List<Activity> exported)
    {
        while (true)
        {
            try
            {
                return exported.ToArray().Where(span => span is not null).ToList();
            }
            catch (ArgumentException)
            {
            }
        }
    }
}
