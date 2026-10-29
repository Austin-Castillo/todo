using System.Net;
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
