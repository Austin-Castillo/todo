namespace Todo.Contracts;

// "Dto" suffix because TodoItem is the entity's name in Todo.Api.
// IsComplete is CompletedAt != null; both are sent, so a browser still running an older client keeps working.
// CompletedAt is UTC. It defaults to null so that a response from a server older than this field still reads.
public record TodoItemDto(int Id, int TodoListId, string Content, bool IsComplete, DateTime? CompletedAt = null);
