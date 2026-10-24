namespace Todo.Contracts;

// One row on the lists page: counts only, no items.
public record TodoListSummary(int Id, string Name, int ItemCount, int CompletedCount);
