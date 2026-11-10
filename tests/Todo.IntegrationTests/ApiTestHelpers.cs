using System.Net.Http.Json;
using Todo.Contracts;

namespace Todo.IntegrationTests;

// Arrange steps go through the API too, so the tests depend only on the public contract, not the schema.
// Each helper throws if its call fails, so a broken arrange step can't pass for the behavior under test.
internal static class ApiTestHelpers
{
    // Identity values start at 1 and a test run creates a few hundred rows, so no row ever has this id.
    public const int MissingId = int.MaxValue;

    public static async Task<TodoListDetails> CreateListAsync(
        this HttpClient client, string name, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("/api/lists", new CreateTodoListRequest(name), ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<TodoListDetails>(ct)
            ?? throw new InvalidOperationException("The response had no body.");
    }

    public static async Task<TodoItemDto> CreateItemAsync(
        this HttpClient client, int listId, string content, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/lists/{listId}/items", new CreateTodoItemRequest(content), ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<TodoItemDto>(ct)
            ?? throw new InvalidOperationException("The response had no body.");
    }

    public static async Task<TodoItemDto> GetItemAsync(this HttpClient client, int id, CancellationToken ct) =>
        await client.GetFromJsonAsync<TodoItemDto>($"/api/items/{id}", ct)
            ?? throw new InvalidOperationException("The response had no body.");

    public static async Task UpdateItemAsync(
        this HttpClient client, int id, UpdateTodoItemRequest request, CancellationToken ct)
    {
        var response = await client.PutAsJsonAsync($"/api/items/{id}", request, ct);
        response.EnsureSuccessStatusCode();
    }
}
