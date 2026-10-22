namespace Todo.Api.Data.Entities;

public class TodoList
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public List<TodoItem> Items { get; set; } = [];
}
