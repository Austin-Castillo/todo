using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using Todo.Api.Data;
using Todo.IntegrationTests;

// One fixture for the whole test run: starting SQL Server takes seconds, so every test class shares it.
[assembly: AssemblyFixture(typeof(TodoApiFixture))]

namespace Todo.IntegrationTests;

// Starts a throwaway SQL Server container, runs the real app against it in memory, and applies the migrations.
// Tests share the database and run in parallel, so each test creates its own data and asserts only about that.
public sealed class TodoApiFixture : IAsyncLifetime
{
    // Same image as local development (the todo-sql container), so tests run on the engine the app uses.
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();

    private WebApplicationFactory<Program>? _factory;

    private WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("The fixture hasn't been initialized.");

    public HttpClient CreateClient() => Factory.CreateClient();

    // A client for a variant of the app, such as another environment or a replaced service, on the same database.
    // Starts its own in-memory server; the fixture's factory disposes it along with itself.
    public HttpClient CreateClient(Action<IWebHostBuilder> configure) =>
        Factory.WithWebHostBuilder(configure).CreateClient();

    public async ValueTask InitializeAsync()
    {
        await _sql.StartAsync(TestContext.Current.CancellationToken);

        // The container's connection string points at master; give the app its own database instead.
        var connectionString = new SqlConnectionStringBuilder(_sql.GetConnectionString())
        {
            InitialCatalog = "TodoTests",
        }.ConnectionString;

        // The real Program.cs, with one more configuration layer on top: the test database's connection string.
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host => host.UseSetting("ConnectionStrings:Todo", connectionString));

        // Creates the database and builds the schema from the migrations, which also tests the migrations.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _sql.DisposeAsync();
    }
}
