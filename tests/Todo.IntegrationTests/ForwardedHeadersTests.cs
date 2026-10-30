using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace Todo.IntegrationTests;

// Requests as the Container Apps ingress forwards them: plain HTTP from the ingress's own address, with what it saw
// in X-Forwarded-Proto and X-Forwarded-For. The app trusts these headers from any source (see Program.cs).
public class ForwardedHeadersTests(TodoApiFixture fixture)
{
    private const string HstsHeader = "Strict-Transport-Security";

    // Not loopback, so a trust list left at its default (loopback only) would ignore the headers.
    private static readonly IPAddress IngressAddress = IPAddress.Parse("100.100.0.5");

    // UseHsts skips localhost, the test server's default host.
    private const string PublicHost = "todo.example.com";

    [Fact]
    public async Task InProduction_WhenIngressSaysHttps_SendsHsts()
    {
        // Arrange
        var server = fixture.CreateServer(host => host.UseEnvironment("Production"));

        // Act
        var context = await SendFromIngressAsync(server, forwardedProto: "https");

        // Assert: 30 days, no includeSubDomains or preload.
        Assert.Equal("max-age=2592000", context.Response.Headers[HstsHeader].ToString());
    }

    // Without the ingress's header the request is plain HTTP, and HSTS is only sent over HTTPS.
    [Fact]
    public async Task InProduction_WithoutForwardedProto_SendsNoHsts()
    {
        // Arrange
        var server = fixture.CreateServer(host => host.UseEnvironment("Production"));

        // Act
        var context = await SendFromIngressAsync(server, forwardedProto: null);

        // Assert
        Assert.False(context.Response.Headers.ContainsKey(HstsHeader));
    }

    // Browsers remember HSTS, so Development never sends it.
    [Fact]
    public async Task InDevelopment_SendsNoHsts()
    {
        // Arrange
        var server = fixture.CreateServer(_ => { });

        // Act
        var context = await SendFromIngressAsync(server, forwardedProto: "https");

        // Assert
        Assert.False(context.Response.Headers.ContainsKey(HstsHeader));
    }

    // A client can put anything on the left of X-Forwarded-For; the ingress adds the address it saw on the right.
    [Fact]
    public async Task ClientAddress_IsTheRightmostForwardedFor()
    {
        // Arrange
        var server = fixture.CreateServer(_ => { });

        // Act
        var context = await SendFromIngressAsync(server, forwardedProto: "https", forwardedFor: "198.51.100.1, 203.0.113.7");

        // Assert
        Assert.Equal(IPAddress.Parse("203.0.113.7"), context.Connection.RemoteIpAddress);
    }

    // Returns the request's HttpContext after the app has handled it.
    private static Task<HttpContext> SendFromIngressAsync(TestServer server, string? forwardedProto, string? forwardedFor = null) =>
        server.SendAsync(context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/";
            context.Request.Host = new HostString(PublicHost);
            context.Connection.RemoteIpAddress = IngressAddress;

            if (forwardedProto is not null)
            {
                context.Request.Headers["X-Forwarded-Proto"] = forwardedProto;
            }

            if (forwardedFor is not null)
            {
                context.Request.Headers["X-Forwarded-For"] = forwardedFor;
            }
        }, TestContext.Current.CancellationToken);
}
