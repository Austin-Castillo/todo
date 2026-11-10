using Microsoft.EntityFrameworkCore;
using Todo.Api.Data;
using Todo.Api.Data.Entities;
using Todo.Contracts;

namespace Todo.Api.Services;

// The EF Core implementation. Scoped, like the TodoDbContext it uses: one instance per HTTP request.
public sealed class ServerTodoService(TodoDbContext db) : ITodoService
{
    // Reads project straight into DTOs: EF selects only the columns the DTO needs, computes the counts
    // in SQL, and tracks nothing (only entities are tracked), so AsNoTracking() isn't needed.

    public Task<List<TodoListSummary>> GetListsAsync(CancellationToken ct = default) =>
        db.TodoLists
            .OrderBy(l => l.Id)
            .Select(l => new TodoListSummary(
                l.Id,
                l.Name,
                l.Items.Count,
                l.Items.Count(i => i.IsComplete)))
            .ToListAsync(ct);

    public Task<TodoListDetails?> GetListAsync(int id, CancellationToken ct = default) =>
        db.TodoLists
            .Where(l => l.Id == id)
            .Select(l => new TodoListDetails(
                l.Id,
                l.Name,
                l.Items
                    .OrderBy(i => i.Id)
                    .Select(i => new TodoItemDto(i.Id, i.TodoListId, i.Content, i.IsComplete))
                    .ToList()))
            .FirstOrDefaultAsync(ct);

    public Task<TodoItemDto?> GetItemAsync(int id, CancellationToken ct = default) =>
        db.TodoItems
            .Where(i => i.Id == id)
            .Select(i => new TodoItemDto(i.Id, i.TodoListId, i.Content, i.IsComplete))
            .FirstOrDefaultAsync(ct);

    // Writes: creates and updates go through the change tracker; deletes are single set-based statements,
    // because nothing needs the entity, only "remove this row if it exists".

    public async Task<TodoListDetails?> CreateListAsync(CreateTodoListRequest request, CancellationToken ct = default)
    {
        var list = new TodoList { Name = request.Name };
        db.TodoLists.Add(list);

        // INSERT ... OUTPUT INSERTED.[Id]: EF writes the generated id back into list.Id.
        await db.SaveChangesAsync(ct);

        return new TodoListDetails(list.Id, list.Name, []);
    }

    public async Task<bool> UpdateListAsync(int id, UpdateTodoListRequest request, CancellationToken ct = default)
    {
        var list = await db.TodoLists.FindAsync([id], ct);
        if (list is null)
        {
            return false;
        }

        list.Name = request.Name;

        // UPDATE of the changed columns only; no SQL at all if the name didn't change.
        await db.SaveChangesAsync(ct);

        return true;
    }

    // One DELETE, nothing loaded. The foreign key's ON DELETE CASCADE removes the list's items.
    public async Task<bool> DeleteListAsync(int id, CancellationToken ct = default) =>
        await db.TodoLists.Where(l => l.Id == id).ExecuteDeleteAsync(ct) > 0;

    public async Task<TodoItemDto?> CreateItemAsync(int listId, CreateTodoItemRequest request, CancellationToken ct = default)
    {
        // Gives the normal case a clear "not found". If the list is deleted between this check and the insert,
        // the foreign key still rejects the item (an exception, so a 500), so an orphaned item can't exist.
        if (!await db.TodoLists.AnyAsync(l => l.Id == listId, ct))
        {
            return null;
        }

        var item = new TodoItem { TodoListId = listId, Content = request.Content };
        db.TodoItems.Add(item);
        await db.SaveChangesAsync(ct);

        return new TodoItemDto(item.Id, item.TodoListId, item.Content, item.IsComplete);
    }

    // Replaces the whole item, so one method covers every edit, including marking it done.
    // Trade-off: a caller sending a stale copy overwrites newer changes (a lost update). Acceptable for a
    // single-user app; the fix is a concurrency token, planned for Stage 1.1.
    public async Task<bool> UpdateItemAsync(int id, UpdateTodoItemRequest request, CancellationToken ct = default)
    {
        var item = await db.TodoItems.FindAsync([id], ct);
        if (item is null)
        {
            return false;
        }

        // CompletedAt changes only when the item is checked or unchecked, so editing a done item's text keeps
        // the time it was done.
        if (request.IsComplete != item.IsComplete)
        {
            item.CompletedAt = request.IsComplete ? DateTime.UtcNow : null;
        }

        item.Content = request.Content;
        item.IsComplete = request.IsComplete;
        await db.SaveChangesAsync(ct);

        return true;
    }

    public async Task<bool> DeleteItemAsync(int id, CancellationToken ct = default) =>
        await db.TodoItems.Where(i => i.Id == id).ExecuteDeleteAsync(ct) > 0;
}
