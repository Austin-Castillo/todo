using System.ComponentModel.DataAnnotations;

namespace Todo.Contracts;

public record CreateTodoItemRequest(
    [Required, MaxLength(TodoLimits.ItemContentMaxLength)] string Content);
