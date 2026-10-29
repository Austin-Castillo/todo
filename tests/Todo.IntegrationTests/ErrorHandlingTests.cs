using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Todo.Contracts;
using static Todo.IntegrationTests.ApiTestHelpers;

namespace Todo.IntegrationTests;

// Error responses by audience: Problem Details JSON for the API (programs), pages for everything else (people).
public class ErrorHandlingTests(TodoApiFixture fixture)
{
    private const string ProblemJson = "application/problem+json";

    // Stands in for exception details that must never reach a client: connection strings, SQL, stack traces.
    private const string SecretDetail = "Simulated failure with internal details";

    private readonly HttpClient _client = fixture.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UnknownPage_Returns404WithNotFoundPage()
    {
        // Act
        var response = await _client.GetAsync("/no-such-page", Ct);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Not Found", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task UnknownApiPath_Returns404ProblemDetails()
    {
        // Act
        var response = await _client.GetAsync("/api/no-such-thing", Ct);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    // The NotFound page handling is scoped away from /api, so an endpoint's own 404 stays an API response.
    [Fact]
    public async Task ApiNotFound_Returns404ProblemDetails_NotTheNotFoundPage()
    {
        // Act
        var response = await _client.GetAsync($"/api/lists/{MissingId}", Ct);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    // Development shows the developer exception page, so these run the app as Production.
    [Fact]
    public async Task ApiException_InProduction_Returns500ProblemDetailsWithoutDetails()
    {
        // Arrange
        var client = CreateFailingProductionClient();

        // Act
        var response = await client.GetAsync("/api/lists", Ct);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(SecretDetail, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task PageException_InProduction_ShowsErrorPageWithoutDetails()
    {
        // Arrange
        var client = CreateFailingProductionClient();

        // Act: prerendering the home page loads the lists on the server, which throws.
        var response = await client.GetAsync("/", Ct);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        var html = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("Something went wrong", html);
        Assert.DoesNotContain(SecretDetail, html);
    }

    private HttpClient CreateFailingProductionClient() => fixture.CreateClient(host =>
    {
        host.UseEnvironment("Production");
        host.ConfigureTestServices(services => services.AddScoped<ITodoService, FailingTodoService>());
    });

    // Every call fails, as if the database were down.
    private sealed class FailingTodoService : ITodoService
    {
        public Task<List<TodoListSummary>> GetListsAsync(CancellationToken ct = default) => throw Failure();

        public Task<TodoListDetails?> GetListAsync(int id, CancellationToken ct = default) => throw Failure();

        public Task<TodoListDetails?> CreateListAsync(CreateTodoListRequest request, CancellationToken ct = default) =>
            throw Failure();

        public Task<bool> UpdateListAsync(int id, UpdateTodoListRequest request, CancellationToken ct = default) =>
            throw Failure();

        public Task<bool> DeleteListAsync(int id, CancellationToken ct = default) => throw Failure();

        public Task<TodoItemDto?> GetItemAsync(int id, CancellationToken ct = default) => throw Failure();

        public Task<TodoItemDto?> CreateItemAsync(int listId, CreateTodoItemRequest request, CancellationToken ct = default) =>
            throw Failure();

        public Task<bool> UpdateItemAsync(int id, UpdateTodoItemRequest request, CancellationToken ct = default) =>
            throw Failure();

        public Task<bool> DeleteItemAsync(int id, CancellationToken ct = default) => throw Failure();

        private static InvalidOperationException Failure() => new(SecretDetail);
    }
}
