using System.ComponentModel.DataAnnotations;

namespace Todo.Contracts;

// The whole item, not just the changed fields: see the PUT /api/items/{id} endpoint for why.
public record UpdateTodoItemRequest(
    [Required, MaxLength(TodoLimits.ItemContentMaxLength)] string Content,
    bool IsComplete);
