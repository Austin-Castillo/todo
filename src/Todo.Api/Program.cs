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

// Error responses from the API use the Problem Details format (RFC 9457), like the validation 400s already do.
builder.Services.AddProblemDetails();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseWebAssemblyDebugging();
}
else
{
    // Unhandled exceptions outside Development (which shows the detailed developer exception page instead)
    // re-run the request as the Error page. The /api branch below handles API exceptions first.
    app.UseExceptionHandler("/error", createScopeForErrors: true);
}

// Error statuses with no body, such as a 404 for an unknown URL, re-run the request as the NotFound page, keeping
// the status code. The /api branch below answers API errors first, so these see a finished response and skip it.
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// API errors are Problem Details JSON. This branch sits inside the page handlers above, so it handles API errors
// before they get there. (The page handlers stay outside any branch: re-running the request from inside a UseWhen
// branch found no endpoint, so the Error and NotFound pages never rendered.)
app.UseWhen(IsApiRequest, api =>
{
    if (!app.Environment.IsDevelopment())
    {
        api.UseExceptionHandler();
    }

    api.UseStatusCodePages();
});

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

// The API's errors are for programs (JSON); everything else is for people (pages).
static bool IsApiRequest(HttpContext context) => context.Request.Path.StartsWithSegments("/api");
