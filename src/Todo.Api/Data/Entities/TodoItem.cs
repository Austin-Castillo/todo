namespace Todo.Api.Data.Entities;

public class TodoItem
{
    public int Id { get; set; }

    public required string Content { get; set; }

    public bool IsComplete { get; set; }

    // When the item was last marked done (UTC); null while it isn't done. Replacing IsComplete by expand and
    // contract: for now the code writes both and still reads IsComplete (docs/checklist.md, G).
    public DateTime? CompletedAt { get; set; }

    public int TodoListId { get; set; }

    // Set by EF Core when the item is loaded with its list; null! because it's never null once loaded.
    public TodoList TodoList { get; set; } = null!;
}
