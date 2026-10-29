using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Todo.Api.Data;
using Todo.Api.Data.Entities;
using Todo.Contracts;

namespace Todo.Api.Endpoints;

public static class TodoListEndpoints
{
    public static IEndpointRouteBuilder MapTodoListEndpoints(this IEndpointRouteBuilder api)
    {
        var lists = api.MapGroup("/lists");

        lists.MapGet("/", GetLists);
        lists.MapGet("/{id:int}", GetList);
        lists.MapPost("/", CreateList);
        lists.MapPut("/{id:int}", UpdateList);
        lists.MapDelete("/{id:int}", DeleteList);

        return api;
    }

    // Reads project straight into DTOs: EF selects only the columns the DTO needs, computes the counts
    // in SQL, and tracks nothing (only entities are tracked), so AsNoTracking() isn't needed.

    private static async Task<Ok<List<TodoListSummary>>> GetLists(TodoDbContext db, CancellationToken ct)
    {
        var lists = await db.TodoLists
            .OrderBy(l => l.Id)
            .Select(l => new TodoListSummary(
                l.Id,
                l.Name,
                l.Items.Count,
                l.Items.Count(i => i.IsComplete)))
            .ToListAsync(ct);

        return TypedResults.Ok(lists);
    }

    private static async Task<Results<Ok<TodoListDetails>, NotFound>> GetList(
        int id, TodoDbContext db, CancellationToken ct)
    {
        var list = await db.TodoLists
            .Where(l => l.Id == id)
            .Select(l => new TodoListDetails(
                l.Id,
                l.Name,
                l.Items
                    .OrderBy(i => i.Id)
                    .Select(i => new TodoItemDto(i.Id, i.TodoListId, i.Content, i.IsComplete))
                    .ToList()))
            .FirstOrDefaultAsync(ct);

        return list is null ? TypedResults.NotFound() : TypedResults.Ok(list);
    }

    // Writes: creates and updates go through the change tracker; deletes are single set-based statements,
    // because nothing needs the entity, only "remove this row if it exists".

    private static async Task<Created<TodoListDetails>> CreateList(
        CreateTodoListRequest request, TodoDbContext db, CancellationToken ct)
    {
        var list = new TodoList { Name = request.Name };
        db.TodoLists.Add(list);

        // INSERT ... OUTPUT INSERTED.[Id]: EF writes the generated id back into list.Id.
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/lists/{list.Id}", new TodoListDetails(list.Id, list.Name, []));
    }

    private static async Task<Results<NoContent, NotFound>> UpdateList(
        int id, UpdateTodoListRequest request, TodoDbContext db, CancellationToken ct)
    {
        var list = await db.TodoLists.FindAsync([id], ct);
        if (list is null)
        {
            return TypedResults.NotFound();
        }

        list.Name = request.Name;

        // UPDATE of the changed columns only; no SQL at all if the name didn't change.
        await db.SaveChangesAsync(ct);

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> DeleteList(
        int id, TodoDbContext db, CancellationToken ct)
    {
        // One DELETE, nothing loaded. The foreign key's ON DELETE CASCADE removes the list's items.
        var deleted = await db.TodoLists.Where(l => l.Id == id).ExecuteDeleteAsync(ct);

        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }
}
