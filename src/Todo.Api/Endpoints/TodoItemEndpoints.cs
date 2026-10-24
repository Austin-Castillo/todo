using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Todo.Api.Data;
using Todo.Api.Data.Entities;
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
        int id, TodoDbContext db, CancellationToken ct)
    {
        var item = await db.TodoItems
            .Where(i => i.Id == id)
            .Select(i => new TodoItemDto(i.Id, i.TodoListId, i.Content, i.IsComplete))
            .FirstOrDefaultAsync(ct);

        return item is null ? TypedResults.NotFound() : TypedResults.Ok(item);
    }

    private static async Task<Results<Created<TodoItemDto>, NotFound>> CreateItem(
        int listId, CreateTodoItemRequest request, TodoDbContext db, CancellationToken ct)
    {
        // Gives the normal case a clear 404. If the list is deleted between this check and the insert,
        // the foreign key still rejects the item (a 500), so an orphaned item can't exist.
        if (!await db.TodoLists.AnyAsync(l => l.Id == listId, ct))
        {
            return TypedResults.NotFound();
        }

        var item = new TodoItem { TodoListId = listId, Content = request.Content };
        db.TodoItems.Add(item);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/items/{item.Id}",
            new TodoItemDto(item.Id, item.TodoListId, item.Content, item.IsComplete));
    }

    // PUT replaces the whole item, so one idempotent endpoint covers every edit, including marking it done.
    // Trade-off: a client sending a stale copy overwrites newer changes (a lost update). Acceptable for a
    // single-user app; the fix is a concurrency token, planned for Stage 1.1.
    private static async Task<Results<NoContent, NotFound>> UpdateItem(
        int id, UpdateTodoItemRequest request, TodoDbContext db, CancellationToken ct)
    {
        var item = await db.TodoItems.FindAsync([id], ct);
        if (item is null)
        {
            return TypedResults.NotFound();
        }

        item.Content = request.Content;
        item.IsComplete = request.IsComplete;
        await db.SaveChangesAsync(ct);

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> DeleteItem(
        int id, TodoDbContext db, CancellationToken ct)
    {
        var deleted = await db.TodoItems.Where(i => i.Id == id).ExecuteDeleteAsync(ct);

        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }
}
