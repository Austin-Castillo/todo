namespace Todo.Contracts;

// Shared by request validation (DTO attributes) and the database column sizes (TodoDbContext),
// so the two can't drift apart. const, not static readonly: attribute arguments must be compile-time constants.
public static class TodoLimits
{
    public const int ListNameMaxLength = 100;

    public const int ItemContentMaxLength = 500;
}
