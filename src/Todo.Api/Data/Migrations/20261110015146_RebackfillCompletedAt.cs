using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Todo.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RebackfillCompletedAt : Migration
    {
        // Data only, no schema change. The first backfill (AddTodoItemCompletedAt) ran while the release before it
        // still served, and that release wrote only IsComplete: items it checked or unchecked afterwards, during the
        // deploy or during a rollback, have a CompletedAt that disagrees. From this release on, the code reads only
        // CompletedAt, so bring every row into line first. The release before this one writes both columns, so
        // nothing goes out of line while this runs. Safe to run twice: it touches only rows that disagree.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Checked by the old code: done, but no time. When isn't known, so the migration's time.
            migrationBuilder.Sql("UPDATE TodoItems SET CompletedAt = SYSUTCDATETIME() WHERE IsComplete = 1 AND CompletedAt IS NULL;");

            // Unchecked by the old code: not done, but still has the time it was done.
            migrationBuilder.Sql("UPDATE TodoItems SET CompletedAt = NULL WHERE IsComplete = 0 AND CompletedAt IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: the rows it changed now agree with IsComplete, which the older code reads.
        }
    }
}
