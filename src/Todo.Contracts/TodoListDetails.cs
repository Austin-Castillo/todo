namespace Todo.Contracts;

// One list with all its items, in creation order.
public record TodoListDetails(int Id, string Name, IReadOnlyList<TodoItemDto> Items);
