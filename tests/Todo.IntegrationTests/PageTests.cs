using System.Net;
using Todo.Contracts;
using static Todo.IntegrationTests.ApiTestHelpers;

namespace Todo.IntegrationTests;

// The pages as the server renders them on a full page load: prerendered HTML, before any WebAssembly runs.
public class PageTests(TodoApiFixture fixture)
{
    private readonly HttpClient _client = fixture.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HomePage_IsPrerenderedWithTheLists()
    {
        // Arrange
        var list = await _client.CreateListAsync($"Prerendered list {Guid.NewGuid():N}", Ct);

        // Act
        var response = await _client.GetAsync("/", Ct);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(list.Name, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task ListPage_IsPrerenderedWithItsItems()
    {
        // Arrange
        var list = await _client.CreateListAsync("Prerendered items", Ct);
        var item = await _client.CreateItemAsync(list.Id, $"Prerendered item {Guid.NewGuid():N}", Ct);

        // Act
        var response = await _client.GetAsync($"/lists/{list.Id}", Ct);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(item.Content, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task ListPage_ShowsDoneItemsUnderCompleted_MostRecentFirst()
    {
        // Arrange: done in the order bread, then milk; eggs not done.
        var list = await _client.CreateListAsync("Groceries", Ct);
        var bread = await _client.CreateItemAsync(list.Id, "Bread", Ct);
        var milk = await _client.CreateItemAsync(list.Id, "Milk", Ct);
        await _client.CreateItemAsync(list.Id, "Eggs", Ct);
        await _client.UpdateItemAsync(bread.Id, new UpdateTodoItemRequest("Bread", IsComplete: true), Ct);
        await _client.UpdateItemAsync(milk.Id, new UpdateTodoItemRequest("Milk", IsComplete: true), Ct);

        // Act
        var html = await _client.GetStringAsync($"/lists/{list.Id}", Ct);

        // Assert: Eggs, then the Completed section, then Milk before Bread.
        var eggs = html.IndexOf("Eggs", StringComparison.Ordinal);
        var completed = html.IndexOf("Completed (2)", StringComparison.Ordinal);
        var milkAt = html.IndexOf("Milk", completed, StringComparison.Ordinal);
        var breadAt = html.IndexOf("Bread", completed, StringComparison.Ordinal);
        Assert.True(eggs >= 0 && eggs < completed, "Eggs should come before the Completed section.");
        Assert.True(milkAt > completed && milkAt < breadAt, "Milk, done last, should come first under Completed.");
    }

    // NavigationManager.NotFound() during prerendering sets the response status, not just the page content.
    [Fact]
    public async Task ListPage_ForMissingList_Returns404()
    {
        // Act
        var response = await _client.GetAsync($"/lists/{MissingId}", Ct);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
