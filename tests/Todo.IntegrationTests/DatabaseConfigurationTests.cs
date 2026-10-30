using Microsoft.Extensions.DependencyInjection;
using Todo.Api.Data;

namespace Todo.IntegrationTests;

// Settings that only matter on Azure SQL, so nothing else would notice them going missing.
public class DatabaseConfigurationTests(TodoApiFixture fixture)
{
    // Azure SQL drops connections in normal running (ADR-0005). Without retries, the first page load after a
    // serverless database pauses shows the Error page.
    [Fact]
    public void DbContext_RetriesOnTransientFailures()
    {
        // Arrange
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();

        // Act
        var strategy = db.Database.CreateExecutionStrategy();

        // Assert
        Assert.True(strategy.RetriesOnFailure);
    }
}
