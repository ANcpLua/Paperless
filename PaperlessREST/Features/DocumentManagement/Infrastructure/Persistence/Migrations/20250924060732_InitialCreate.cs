using System;
using Microsoft.EntityFrameworkCore.Migrations;
using PaperlessREST.Features.DocumentManagement.Application;

#nullable disable

namespace PaperlessREST.Features.DocumentManagement.Infrastructure.Persistence.Migrations
{

    public partial class InitialCreate : Migration
    {

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:document_status", "completed,failed,pending");

            migrationBuilder.CreateTable(
                name: "documents",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<DocumentStatus>(type: "document_status", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    storage_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content = table.Column<string>(type: "character varying(1000000)", maxLength: 1000000, nullable: true),
                    summary = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: static table =>
                {
                    table.PrimaryKey("PK_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "daily_document_access",
                columns: static table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    log_date = table.Column<DateOnly>(type: "date", nullable: false),
                    access_count = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: static table =>
                {
                    table.PrimaryKey("PK_daily_document_access", x => x.id);
                    table.ForeignKey(
                        name: "FK_daily_document_access_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_daily_document_access_document_id_log_date",
                table: "daily_document_access",
                columns: new[] { "document_id", "log_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_daily_document_access_log_date",
                table: "daily_document_access",
                column: "log_date");

            migrationBuilder.CreateIndex(
                name: "IX_documents_file_name",
                table: "documents",
                column: "file_name");
        }


        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_document_access");

            migrationBuilder.DropTable(
                name: "documents");
        }
    }
}
