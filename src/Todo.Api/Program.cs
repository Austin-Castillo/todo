using Microsoft.EntityFrameworkCore;
using Todo.Api.Components;
using Todo.Api.Data;
using Todo.Api.Endpoints;
using Todo.Api.Services;
using Todo.Contracts;

var builder = WebApplication.CreateBuilder(args);

// Blazor Web App: the server renders the page shell (Components/App.razor); the pages run in the browser as WebAssembly.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

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

// Used by the API endpoints, and by server-side rendering of the pages. The Client registers its own HTTP version.
builder.Services.AddScoped<ITodoService, ServerTodoService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseWebAssemblyDebugging();
}

// No UseHttpsRedirection: in production, HTTPS is handled by the hosting platform's ingress in front of the container.

// Required by MapRazorComponents: server-rendered forms are checked for an antiforgery token.
app.UseAntiforgery();

// Static files, including the client's _framework files (fingerprinted and compressed). Client and API share one origin.
app.MapStaticAssets();

var api = app.MapGroup("/api");
api.MapTodoListEndpoints();
api.MapTodoItemEndpoints();

// The client's @page routes are registered here too, so a deep link like /lists/3 renders the app without a fallback.
// Unknown paths, /api ones included, match nothing and return 404.
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Todo.Client._Imports).Assembly);

app.Run();
