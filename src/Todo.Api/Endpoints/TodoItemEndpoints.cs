using Microsoft.AspNetCore.Http.HttpResults;
using Todo.Contracts;

namespace Todo.Api.Endpoints;

// Shallow nesting: items are created under their list (POST /api/lists/{listId}/items),
// then addressed by their own id, which is unique across all lists.
public static class TodoItemEndpoints
{
    public static IEndpointRouteBuilder MapTodoItemEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapPost("/lists/{listId:int}/items", CreateItem);

        var items = api.MapGroup("/items");

        items.MapGet("/{id:int}", GetItem);
        items.MapPut("/{id:int}", UpdateItem);
        items.MapDelete("/{id:int}", DeleteItem);

        return api;
    }

    private static async Task<Results<Ok<TodoItemDto>, NotFound>> GetItem(
        int id, ITodoService todos, CancellationToken ct)
    {
        var item = await todos.GetItemAsync(id, ct);

        return item is null ? TypedResults.NotFound() : TypedResults.Ok(item);
    }

    private static async Task<Results<Created<TodoItemDto>, NotFound>> CreateItem(
        int listId, CreateTodoItemRequest request, ITodoService todos, CancellationToken ct)
    {
        var item = await todos.CreateItemAsync(listId, request, ct);

        return item is null
            ? TypedResults.NotFound()
            : TypedResults.Created($"/api/items/{item.Id}", item);
    }

    // PUT replaces the whole item, so one idempotent endpoint covers every edit, including marking it done.
    private static async Task<Results<NoContent, NotFound>> UpdateItem(
        int id, UpdateTodoItemRequest request, ITodoService todos, CancellationToken ct) =>
        await todos.UpdateItemAsync(id, request, ct) ? TypedResults.NoContent() : TypedResults.NotFound();

    private static async Task<Results<NoContent, NotFound>> DeleteItem(
        int id, ITodoService todos, CancellationToken ct) =>
        await todos.DeleteItemAsync(id, ct) ? TypedResults.NoContent() : TypedResults.NotFound();
}
