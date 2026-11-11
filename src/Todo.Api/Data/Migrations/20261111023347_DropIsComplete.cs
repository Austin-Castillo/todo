using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Todo.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropIsComplete : Migration
    {
        // The contract: the last of four releases replacing IsComplete with CompletedAt (docs/checklist.md, G).
        // The previous release stopped mapping IsComplete, and its migration already moved the snapshot on, so EF
        // scaffolded this one empty and the drop is written by hand. Safe while the previous release serves: it
        // doesn't map the column. Nothing is lost: IsComplete is always CompletedAt IS NOT NULL.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // On SQL Server, EF drops the column's default constraint (DF_TodoItems_IsComplete) first.
            migrationBuilder.DropColumn(
                name: "IsComplete",
                table: "TodoItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to how the previous release left it: the same named default (its own Down drops it by name),
            // and the values rebuilt from CompletedAt.
            migrationBuilder.Sql("ALTER TABLE TodoItems ADD IsComplete bit NOT NULL CONSTRAINT DF_TodoItems_IsComplete DEFAULT 0;");
            migrationBuilder.Sql("UPDATE TodoItems SET IsComplete = 1 WHERE CompletedAt IS NOT NULL;");
        }
    }
}
