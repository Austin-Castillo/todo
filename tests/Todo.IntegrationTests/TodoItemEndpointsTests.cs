using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Todo.Api.Data;
using Todo.Contracts;
using static Todo.IntegrationTests.ApiTestHelpers;

namespace Todo.IntegrationTests;

public class TodoItemEndpointsTests(TodoApiFixture fixture)
{
    private readonly HttpClient _client = fixture.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> InvalidContents => new()
    {
        "",
        "   ",
        new string('a', TodoLimits.ItemContentMaxLength + 1),
    };

    [Fact]
    public async Task CreateItem_ReturnsCreatedItem_ThatCanBeFetchedFromLocation()
    {
        // Arrange
        var list = await _client.CreateListAsync("Groceries", Ct);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/lists/{list.Id}/items", new CreateTodoItemRequest("Milk"), Ct);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<TodoItemDto>(Ct);
        Assert.NotNull(created);
        Assert.Equal(list.Id, created.TodoListId);
        Assert.Equal("Milk", created.Content);
        Assert.False(created.IsComplete);

        // Shallow nesting: created under its list, but addressed by its own id.
        Assert.Equal($"/api/items/{created.Id}", response.Headers.Location?.OriginalString);

        var fetched = await _client.GetFromJsonAsync<TodoItemDto>(response.Headers.Location, Ct);
        Assert.Equal(created, fetched);
    }

    [Fact]
    public async Task CreateItem_UnderMissingList_Returns404()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/lists/{MissingId}/items", new CreateTodoItemRequest("Milk"), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(InvalidContents))]
    public async Task CreateItem_WithInvalidContent_Returns400WithContentError(string content)
    {
        // Arrange
        var list = await _client.CreateListAsync("Groceries", Ct);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/lists/{list.Id}/items", new CreateTodoItemRequest(content), Ct);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(Ct);
        Assert.NotNull(problem);
        Assert.Equal("Content", Assert.Single(problem.Errors).Key);
    }

    // The longest content validation allows must also fit the database column, or the insert fails with a 500.
    [Fact]
    public async Task CreateItem_WithContentAtMaxLength_ReturnsCreated()
    {
        // Arrange
        var list = await _client.CreateListAsync("Groceries", Ct);
        var request = new CreateTodoItemRequest(new string('a', TodoLimits.ItemContentMaxLength));

        // Act
        var response = await _client.PostAsJsonAsync($"/api/lists/{list.Id}/items", request, Ct);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task GetItem_WhenMissing_Returns404()
    {
        var response = await _client.GetAsync($"/api/items/{MissingId}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateItem_ReplacesContentAndCompletion()
    {
        // Arrange
        var list = await _client.CreateListAsync("Groceries", Ct);
        var item = await _client.CreateItemAsync(list.Id, "Milk", Ct);

        // Act
        var response = await _client.PutAsJsonAsync(
            $"/api/items/{item.Id}", new UpdateTodoItemRequest("Oat milk", IsComplete: true), Ct);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var fetched = await _client.GetFromJsonAsync<TodoItemDto>($"/api/items/{item.Id}", Ct);
        Assert.Equal(new TodoItemDto(item.Id, list.Id, "Oat milk", IsComplete: true), fetched);
    }

    // PUT replaces the whole item, so a field left out of the body isn't "unchanged": it gets its default.
    // Pinned here so that changing it (e.g. making isComplete required) is a deliberate decision.
    [Fact]
    public async Task UpdateItem_WithoutIsComplete_MarksItemNotDone()
    {
        // Arrange
        var list = await _client.CreateListAsync("Groceries", Ct);
        var item = await _client.CreateItemAsync(list.Id, "Milk", Ct);
        await _client.UpdateItemAsync(item.Id, new UpdateTodoItemRequest("Milk", IsComplete: true), Ct);

        // Act: an anonymous object, because UpdateTodoItemRequest always sends isComplete.
        var response = await _client.PutAsJsonAsync($"/api/items/{item.Id}", new { content = "Milk" }, Ct);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var fetched = await _client.GetFromJsonAsync<TodoItemDto>($"/api/items/{item.Id}", Ct);
        Assert.False(fetched?.IsComplete);
    }

    // CompletedAt isn't in the API yet (the code still reads IsComplete), so these check the database directly.

    [Fact]
    public async Task UpdateItem_MarkingDone_RecordsWhenItWasDone()
    {
        // Arrange
        var list = await _client.CreateListAsync("Groceries", Ct);
        var item = await _client.CreateItemAsync(list.Id, "Milk", Ct);
        Assert.Null(await CompletedAtInDatabaseAsync(item.Id));
        var before = DateTime.UtcNow;

        // Act
        await _client.UpdateItemAsync(item.Id, new UpdateTodoItemRequest("Milk", IsComplete: true), Ct);

        // Assert
        var completedAt = await CompletedAtInDatabaseAsync(item.Id);
        Assert.NotNull(completedAt);
        Assert.InRange(completedAt.Value, before, DateTime.UtcNow);
    }

    [Fact]
    public async Task UpdateItem_EditingADoneItem_KeepsWhenItWasDone()
    {
        // Arrange
        var list = await _client.CreateListAsync("Groceries", Ct);
        var item = await _client.CreateItemAsync(list.Id, "Milk", Ct);
        await _client.UpdateItemAsync(item.Id, new UpdateTodoItemRequest("Milk", IsComplete: true), Ct);
        var completedAt = await CompletedAtInDatabaseAsync(item.Id);

        // Act
        await _client.UpdateItemAsync(item.Id, new UpdateTodoItemRequest("Oat milk", IsComplete: true), Ct);

        // Assert
        Assert.NotNull(completedAt);
        Assert.Equal(completedAt, await CompletedAtInDatabaseAsync(item.Id));
    }

    [Fact]
    public async Task UpdateItem_MarkingNotDone_ClearsWhenItWasDone()
    {
        // Arrange
        var list = await _client.CreateListAsync("Groceries", Ct);
        var item = await _client.CreateItemAsync(list.Id, "Milk", Ct);
        await _client.UpdateItemAsync(item.Id, new UpdateTodoItemRequest("Milk", IsComplete: true), Ct);

        // Act
        await _client.UpdateItemAsync(item.Id, new UpdateTodoItemRequest("Milk", IsComplete: false), Ct);

        // Assert
        Assert.Null(await CompletedAtInDatabaseAsync(item.Id));
    }

    private async Task<DateTime?> CompletedAtInDatabaseAsync(int itemId)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        return await db.TodoItems.Where(i => i.Id == itemId).Select(i => i.CompletedAt).SingleAsync(Ct);
    }

    [Fact]
    public async Task UpdateItem_WithInvalidContent_Returns400()
    {
        // Arrange
        var list = await _client.CreateListAsync("Groceries", Ct);
        var item = await _client.CreateItemAsync(list.Id, "Milk", Ct);

        // Act
        var response = await _client.PutAsJsonAsync(
            $"/api/items/{item.Id}", new UpdateTodoItemRequest("", IsComplete: false), Ct);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateItem_WhenMissing_Returns404()
    {
        var response = await _client.PutAsJsonAsync(
            $"/api/items/{MissingId}", new UpdateTodoItemRequest("Milk", IsComplete: false), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteItem_RemovesOnlyThatItem()
    {
        // Arrange
        var list = await _client.CreateListAsync("Groceries", Ct);
        var milk = await _client.CreateItemAsync(list.Id, "Milk", Ct);
        var eggs = await _client.CreateItemAsync(list.Id, "Eggs", Ct);

        // Act
        var response = await _client.DeleteAsync($"/api/items/{milk.Id}", Ct);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var fetched = await _client.GetFromJsonAsync<TodoListDetails>($"/api/lists/{list.Id}", Ct);
        Assert.NotNull(fetched);
        Assert.Equal([eggs], fetched.Items);
    }

    [Fact]
    public async Task DeleteItem_WhenMissing_Returns404()
    {
        var response = await _client.DeleteAsync($"/api/items/{MissingId}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
