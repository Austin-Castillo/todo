namespace Todo.Api.Data.Entities;

public class TodoItem
{
    public int Id { get; set; }

    public required string Content { get; set; }

    public bool IsComplete { get; set; }

    // Failure demo: a model change with no migration. CI should go red; reverted before merging.
    public string? Notes { get; set; }

    public int TodoListId { get; set; }

    // Set by EF Core when the item is loaded with its list; null! because it's never null once loaded.
    public TodoList TodoList { get; set; } = null!;
}
