using System.Net;
using System.Net.Http.Json;
using Todo.Contracts;

namespace Todo.Client.Services;

// The browser's implementation: every call goes to the API, which runs ServerTodoService on the server.
// Reads: 404 becomes null and any other failure throws, as GetFromJsonAsync does.
// Writes: any failure (404, a 400 from validation, a 500) returns null or false, so the page can say "couldn't save".
public sealed class ClientTodoService(HttpClient http) : ITodoService
{
    public async Task<List<TodoListSummary>> GetListsAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<TodoListSummary>>("api/lists", ct) ?? [];

    public async Task<TodoListDetails?> GetListAsync(int id, CancellationToken ct = default) =>
        await ReadAsync<TodoListDetails>(await http.GetAsync($"api/lists/{id}", ct), ct);

    public async Task<TodoItemDto?> GetItemAsync(int id, CancellationToken ct = default) =>
        await ReadAsync<TodoItemDto>(await http.GetAsync($"api/items/{id}", ct), ct);

    public async Task<TodoListDetails?> CreateListAsync(CreateTodoListRequest request, CancellationToken ct = default) =>
        await CreatedAsync<TodoListDetails>(await http.PostAsJsonAsync("api/lists", request, ct), ct);

    public async Task<bool> UpdateListAsync(int id, UpdateTodoListRequest request, CancellationToken ct = default) =>
        Succeeded(await http.PutAsJsonAsync($"api/lists/{id}", request, ct));

    public async Task<bool> DeleteListAsync(int id, CancellationToken ct = default) =>
        Succeeded(await http.DeleteAsync($"api/lists/{id}", ct));

    public async Task<TodoItemDto?> CreateItemAsync(int listId, CreateTodoItemRequest request, CancellationToken ct = default) =>
        await CreatedAsync<TodoItemDto>(await http.PostAsJsonAsync($"api/lists/{listId}/items", request, ct), ct);

    public async Task<bool> UpdateItemAsync(int id, UpdateTodoItemRequest request, CancellationToken ct = default) =>
        Succeeded(await http.PutAsJsonAsync($"api/items/{id}", request, ct));

    public async Task<bool> DeleteItemAsync(int id, CancellationToken ct = default) =>
        Succeeded(await http.DeleteAsync($"api/items/{id}", ct));

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct) where T : class
    {
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>(ct);
        }
    }

    private static async Task<T?> CreatedAsync<T>(HttpResponseMessage response, CancellationToken ct) where T : class
    {
        using (response)
        {
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<T>(ct) : null;
        }
    }

    private static bool Succeeded(HttpResponseMessage response)
    {
        using (response)
        {
            return response.IsSuccessStatusCode;
        }
    }
}
