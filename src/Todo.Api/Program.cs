using Microsoft.EntityFrameworkCore;
using Todo.Api.Data;
using Todo.Api.Endpoints;
using Todo.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Checks the DataAnnotations on endpoint parameters (the request DTOs) before each handler runs,
// and returns a 400 with Problem Details if they fail.
builder.Services.AddValidation();

// Scoped by default: one TodoDbContext per HTTP request, disposed when the request ends.
builder.Services.AddDbContext<TodoDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("Todo"));

    if (builder.Environment.IsDevelopment())
    {
        // Show parameter values in the logged SQL. Development only: they can contain user data.
        options.EnableSensitiveDataLogging();
    }
});

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
api.MapTodoListEndpoints();
api.MapTodoItemEndpoints();

// Unknown /api paths return 404; every other unknown path gets the client's index.html,
// so client-side routes like /lists/3 survive a refresh or a deep link.
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();
