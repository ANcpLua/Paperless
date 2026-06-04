#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace PaperlessREST.Features.DocumentManagement.Infrastructure.Persistence.Migrations
{

    public partial class AddSummaryGeneratedAt : Migration
    {

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "summary_generated_at",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);
        }


        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "summary_generated_at",
                table: "documents");
        }
    }
}
