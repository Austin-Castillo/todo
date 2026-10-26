using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Todo.Client.Services;
using Todo.Contracts;

// No root components here: the server's App.razor renders the page shell and marks where the interactive Routes go.
var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// The pages use ITodoService; in the browser it calls the API. The server registers its own EF Core version.
builder.Services.AddScoped<ITodoService, ClientTodoService>();

await builder.Build().RunAsync();
