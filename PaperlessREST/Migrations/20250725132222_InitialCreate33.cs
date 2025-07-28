#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace PaperlessREST.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate33 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "status",
                table: "documents",
                newName: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Status",
                table: "documents",
                newName: "status");
        }
    }
}
