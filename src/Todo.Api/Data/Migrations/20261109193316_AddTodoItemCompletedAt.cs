using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Todo.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTodoItemCompletedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "TodoItems",
                type: "datetime2",
                nullable: true);

            // Backfill: items already done get the migration's time, since when they were done was never recorded.
            // Items the old code checks or unchecks after this runs, before the new code takes over, are fixed by
            // a second backfill in the next release.
            migrationBuilder.Sql("UPDATE TodoItems SET CompletedAt = SYSUTCDATETIME() WHERE IsComplete = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "TodoItems");
        }
    }
}
