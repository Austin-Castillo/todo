using Todo.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseWebAssemblyDebugging();
}

// No UseHttpsRedirection: in production, HTTPS is handled by the hosting platform's ingress in front of the container.

// Serve the Blazor WebAssembly client (Todo.Client) from this app, so client and API share one origin.
// MapStaticAssets also serves the client's _framework files (fingerprinted and compressed).
app.MapStaticAssets();

var api = app.MapGroup("/api");
api.MapGet("/ping", () => new PingResponse("pong", DateTimeOffset.UtcNow));

// Unknown /api paths return 404; every other unknown path gets the client's index.html,
// so client-side routes like /lists/3 survive a refresh or a deep link.
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();
