using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Todo.Contracts;
using static Todo.IntegrationTests.ApiTestHelpers;

namespace Todo.IntegrationTests;

// xUnit creates a new instance per test, injecting the shared assembly fixture.
public class TodoListEndpointsTests(TodoApiFixture fixture)
{
    private readonly HttpClient _client = fixture.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Attribute arguments must be constants, so the too-long name comes from a property instead of [InlineData].
    public static TheoryData<string> InvalidNames => new()
    {
        "",
        "   ",
        new string('a', TodoLimits.ListNameMaxLength + 1),
    };

    [Fact]
    public async Task CreateList_ReturnsCreatedList_ThatCanBeFetchedFromLocation()
    {
        // Arrange
        var request = new CreateTodoListRequest("Groceries");

        // Act
        var response = await _client.PostAsJsonAsync("/api/lists", request, Ct);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<TodoListDetails>(Ct);
        Assert.NotNull(created);
        Assert.Equal("Groceries", created.Name);
        Assert.Empty(created.Items);
        Assert.Equal($"/api/lists/{created.Id}", response.Headers.Location?.OriginalString);

        var fetched = await _client.GetFromJsonAsync<TodoListDetails>(response.Headers.Location, Ct);

        // Equivalent, not Equal: record equality compares the Items lists by reference, not by contents.
        Assert.Equivalent(created, fetched, strict: true);
    }

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public async Task CreateList_WithInvalidName_Returns400WithNameError(string name)
    {
        // Act
        var response = await _client.PostAsJsonAsync("/api/lists", new CreateTodoListRequest(name), Ct);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(Ct);
        Assert.NotNull(problem);
        Assert.Equal("Name", Assert.Single(problem.Errors).Key);
    }

    // The longest name validation allows must also fit the database column, or the insert fails with a 500.
    [Fact]
    public async Task CreateList_WithNameAtMaxLength_ReturnsCreated()
    {
        // Arrange
        var request = new CreateTodoListRequest(new string('a', TodoLimits.ListNameMaxLength));

        // Act
        var response = await _client.PostAsJsonAsync("/api/lists", request, Ct);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task GetLists_ReturnsItemAndCompletedCountsPerList()
    {
        // Arrange
        var groceries = await _client.CreateListAsync("Groceries", Ct);
        var milk = await _client.CreateItemAsync(groceries.Id, "Milk", Ct);
        await _client.CreateItemAsync(groceries.Id, "Eggs", Ct);
        await _client.CreateItemAsync(groceries.Id, "Bread", Ct);
        await _client.UpdateItemAsync(milk.Id, new UpdateTodoItemRequest("Milk", IsComplete: true), Ct);

        var empty = await _client.CreateListAsync("Empty", Ct);

        // Act
        var summaries = await _client.GetFromJsonAsync<List<TodoListSummary>>("/api/lists", Ct);

        // Assert
        Assert.NotNull(summaries);

        // Other tests' lists share the database, so look for these two instead of asserting the whole response.
        Assert.Contains(new TodoListSummary(groceries.Id, "Groceries", ItemCount: 3, CompletedCount: 1), summaries);
        Assert.Contains(new TodoListSummary(empty.Id, "Empty", ItemCount: 0, CompletedCount: 0), summaries);
    }

    [Fact]
    public async Task GetList_ReturnsItsItemsInCreationOrder()
    {
        // Arrange
        var list = await _client.CreateListAsync("Groceries", Ct);
        TodoItemDto[] items =
        [
            await _client.CreateItemAsync(list.Id, "Milk", Ct),
            await _client.CreateItemAsync(list.Id, "Eggs", Ct),
            await _client.CreateItemAsync(list.Id, "Bread", Ct),
        ];

        // Act
        var fetched = await _client.GetFromJsonAsync<TodoListDetails>($"/api/lists/{list.Id}", Ct);

        // Assert
        Assert.NotNull(fetched);
        Assert.Equal(items, fetched.Items);
    }

    [Fact]
    public async Task GetList_WhenMissing_Returns404()
    {
        var response = await _client.GetAsync($"/api/lists/{MissingId}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateList_RenamesTheList()
    {
        // Arrange
        var list = await _client.CreateListAsync("Groceries", Ct);

        // Act
        var response = await _client.PutAsJsonAsync(
            $"/api/lists/{list.Id}", new UpdateTodoListRequest("Weekend groceries"), Ct);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var fetched = await _client.GetFromJsonAsync<TodoListDetails>($"/api/lists/{list.Id}", Ct);
        Assert.Equal("Weekend groceries", fetched?.Name);
    }

    [Fact]
    public async Task UpdateList_WhenMissing_Returns404()
    {
        var response = await _client.PutAsJsonAsync(
            $"/api/lists/{MissingId}", new UpdateTodoListRequest("Groceries"), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteList_RemovesTheList()
    {
        // Arrange
        var list = await _client.CreateListAsync("Groceries", Ct);

        // Act
        var response = await _client.DeleteAsync($"/api/lists/{list.Id}", Ct);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var fetch = await _client.GetAsync($"/api/lists/{list.Id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, fetch.StatusCode);
    }

    [Fact]
    public async Task DeleteList_AlsoDeletesItsItems()
    {
        // Arrange
        var list = await _client.CreateListAsync("Groceries", Ct);
        var item = await _client.CreateItemAsync(list.Id, "Milk", Ct);

        // Act
        await _client.DeleteAsync($"/api/lists/{list.Id}", Ct);

        // Assert: the foreign key's ON DELETE CASCADE removed the item with its list.
        var fetch = await _client.GetAsync($"/api/items/{item.Id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, fetch.StatusCode);
    }

    [Fact]
    public async Task DeleteList_WhenMissing_Returns404()
    {
        var response = await _client.DeleteAsync($"/api/lists/{MissingId}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
