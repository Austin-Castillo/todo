using System.ComponentModel.DataAnnotations;

namespace Todo.Contracts;

public record CreateTodoListRequest(
    [Required, MaxLength(TodoLimits.ListNameMaxLength)] string Name);
