using System.ComponentModel.DataAnnotations;

namespace Todo.Contracts;

public record UpdateTodoListRequest(
    [Required, MaxLength(TodoLimits.ListNameMaxLength)] string Name);
