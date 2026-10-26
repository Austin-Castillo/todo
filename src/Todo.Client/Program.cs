using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

// No root components here: the server's App.razor renders the page shell and marks where the interactive Routes go.
var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

await builder.Build().RunAsync();
