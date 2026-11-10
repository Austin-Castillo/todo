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

            // datetime2 stores no time zone, so EF reads it back as DateTimeKind.Unspecified, and JSON would send it
            // without a "Z". The app only ever writes UTC, so mark it UTC on the way out. No schema change.
            item.Property(i => i.CompletedAt).HasConversion(
                v => v,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
        });
    }
}
