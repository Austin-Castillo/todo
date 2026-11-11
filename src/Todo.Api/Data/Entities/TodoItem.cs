namespace Todo.Api.Data.Entities;

public class TodoItem
{
    public int Id { get; set; }

    public required string Content { get; set; }

    // When the item was last marked done (UTC); null while it isn't done. It replaced an IsComplete column by
    // expand and contract (docs/checklist.md, G). That column is no longer mapped but still in the database,
    // with a default for the rows this code inserts, until the next release drops it.
    public DateTime? CompletedAt { get; set; }

    public int TodoListId { get; set; }

    // Set by EF Core when the item is loaded with its list; null! because it's never null once loaded.
    public TodoList TodoList { get; set; } = null!;
}
