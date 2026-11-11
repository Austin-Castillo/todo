using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Todo.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class StopMappingIsComplete : Migration
    {
        // The code stops mapping TodoItem.IsComplete in this release, so EF scaffolded a DropColumn. Not yet: this
        // runs while the previous release still serves, and that release maps the column, so dropping it now would
        // fail every item query until the new revision takes over. The drop is the next release's migration.
        //
        // What this release needs instead: the column is NOT NULL with no default, and the new code's inserts
        // leave it out, so SQL Server would reject them. A default of 0 (not done; CompletedAt says otherwise) fixes
        // that. The previous release always supplies a value, so the default changes nothing for it.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE TodoItems ADD CONSTRAINT DF_TodoItems_IsComplete DEFAULT 0 FOR IsComplete;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE TodoItems DROP CONSTRAINT DF_TodoItems_IsComplete;");
        }
    }
}
