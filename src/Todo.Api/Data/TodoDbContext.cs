using Microsoft.EntityFrameworkCore;
using Todo.Api.Data.Entities;
using Todo.Contracts;

namespace Todo.Api.Data;

public class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
{
    public DbSet<TodoList> TodoLists => Set<TodoList>();

    public DbSet<TodoItem> TodoItems => Set<TodoItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Keys, the relationship and cascade delete all come from conventions (see the entities).
        // Only what conventions can't know is configured here.
        modelBuilder.Entity<TodoList>(list =>
        {
            list.Property(l => l.Name).HasMaxLength(TodoLimits.ListNameMaxLength);
        });

        modelBuilder.Entity<TodoItem>(item =>
        {
            item.Property(i => i.Content).HasMaxLength(TodoLimits.ItemContentMaxLength);
        });
    }
}
