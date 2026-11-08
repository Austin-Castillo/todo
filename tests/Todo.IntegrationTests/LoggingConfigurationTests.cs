using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Todo.IntegrationTests;

// Console output: one JSON object per entry outside Development (one row each in Log Analytics), text in Development.
public class LoggingConfigurationTests(TodoApiFixture fixture)
{
    [Fact]
    public void ConsoleFormatter_InProduction_IsJsonWithScopes()
    {
        // Arrange
        var services = fixture.CreateServer(host => host.UseEnvironment("Production")).Services;

        // Act
        var console = services.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue;
        var json = services.GetRequiredService<IOptionsMonitor<JsonConsoleFormatterOptions>>().CurrentValue;

        // Assert
        Assert.Equal(ConsoleFormatterNames.Json, console.FormatterName);
        Assert.True(json.IncludeScopes);
    }

    [Fact]
    public void ConsoleFormatter_InDevelopment_IsNotJson()
    {
        // Act
        var console = fixture.Services.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue;

        // Assert
        Assert.NotEqual(ConsoleFormatterNames.Json, console.FormatterName);
    }
}
