namespace Todo.Contracts;

// Everything the app can do with lists and items, independent of where the caller runs. Two implementations:
// ServerTodoService (Todo.Api) uses EF Core and backs the API endpoints and server-side rendering;
// ClientTodoService (Todo.Client) calls the API over HTTP from the browser.
// Writes return null or false when nothing was saved: on the server that means the list or item doesn't exist;
// from the browser it can also mean the API rejected the request.
public interface ITodoService
{
    Task<List<TodoListSummary>> GetListsAsync(CancellationToken ct = default);

    // null when the list doesn't exist.
    Task<TodoListDetails?> GetListAsync(int id, CancellationToken ct = default);

    Task<TodoListDetails?> CreateListAsync(CreateTodoListRequest request, CancellationToken ct = default);

    Task<bool> UpdateListAsync(int id, UpdateTodoListRequest request, CancellationToken ct = default);

    Task<bool> DeleteListAsync(int id, CancellationToken ct = default);

    // null when the item doesn't exist.
    Task<TodoItemDto?> GetItemAsync(int id, CancellationToken ct = default);

    Task<TodoItemDto?> CreateItemAsync(int listId, CreateTodoItemRequest request, CancellationToken ct = default);

    Task<bool> UpdateItemAsync(int id, UpdateTodoItemRequest request, CancellationToken ct = default);

    Task<bool> DeleteItemAsync(int id, CancellationToken ct = default);
}
