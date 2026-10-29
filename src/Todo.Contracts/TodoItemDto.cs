namespace Todo.Contracts;

// "Dto" suffix because TodoItem is the entity's name in Todo.Api.
public record TodoItemDto(int Id, int TodoListId, string Content, bool IsComplete);
