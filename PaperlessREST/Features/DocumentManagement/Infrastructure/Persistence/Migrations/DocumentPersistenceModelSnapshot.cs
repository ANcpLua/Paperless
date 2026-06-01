#nullable disable

using Microsoft.EntityFrameworkCore.Infrastructure;

namespace PaperlessREST.Features.DocumentManagement.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DocumentPersistence))]
internal class DocumentPersistenceModelSnapshot : ModelSnapshot
{
	protected override void BuildModel(ModelBuilder modelBuilder)
	{
#pragma warning disable 612, 618
		modelBuilder
			.HasAnnotation("ProductVersion", "10.0.0-rc.1.25451.107")
			.HasAnnotation("Relational:MaxIdentifierLength", 63);

		modelBuilder.HasPostgresEnum("document_status", [
			"completed", "failed", "pending"
		]);
		modelBuilder.UseIdentityByDefaultColumns();

		modelBuilder.Entity("PaperlessREST.DAL.DailyDocumentAccess", static b =>
		{
			b.Property<Guid>("Id")
				.ValueGeneratedOnAdd()
				.HasColumnType("uuid")
				.HasColumnName("id")
				.HasDefaultValueSql("gen_random_uuid()");

			b.Property<int>("AccessCount")
				.HasColumnType("integer")
				.HasColumnName("access_count");

			b.Property<Guid>("DocumentId")
				.HasColumnType("uuid")
				.HasColumnName("document_id");

			b.Property<DateOnly>("LogDate")
				.HasColumnType("date")
				.HasColumnName("log_date");

			b.Property<DateTimeOffset>("UpdatedAt")
				.ValueGeneratedOnAdd()
				.HasColumnType("timestamp with time zone")
				.HasColumnName("updated_at")
				.HasDefaultValueSql("CURRENT_TIMESTAMP");

			b.HasKey("Id");

			b.HasIndex("LogDate");

			b.HasIndex("DocumentId", "LogDate")
				.IsUnique();

			b.ToTable("daily_document_access", (string)null);
		});

		modelBuilder.Entity("PaperlessREST.DAL.DocumentEntity", static b =>
		{
			b.Property<Guid>("Id")
				.ValueGeneratedOnAdd()
				.HasColumnType("uuid")
				.HasColumnName("id");

			b.Property<string>("Content")
				.HasMaxLength(1000000)
				.HasColumnType("character varying(1000000)")
				.HasColumnName("content");

			b.Property<DateTimeOffset>("CreatedAt")
				.ValueGeneratedOnAdd()
				.HasColumnType("timestamp with time zone")
				.HasColumnName("created_at")
				.HasDefaultValueSql("CURRENT_TIMESTAMP");

			b.Property<string>("FileName")
				.IsRequired()
				.HasMaxLength(255)
				.HasColumnType("character varying(255)")
				.HasColumnName("file_name");

			b.Property<DateTimeOffset?>("ProcessedAt")
				.HasColumnType("timestamp with time zone")
				.HasColumnName("processed_at");

			b.Property<DocumentStatus>("Status")
				.HasColumnType("document_status")
				.HasColumnName("status");

			b.Property<string>("StoragePath")
				.IsRequired()
				.HasMaxLength(500)
				.HasColumnType("character varying(500)")
				.HasColumnName("storage_path");

			b.Property<string>("Summary")
				.HasMaxLength(5000)
				.HasColumnType("character varying(5000)")
				.HasColumnName("summary");

			b.Property<DateTimeOffset?>("SummaryGeneratedAt")
				.HasColumnType("timestamp with time zone")
				.HasColumnName("summary_generated_at");

			b.HasKey("Id");

			b.HasIndex("FileName");

			b.ToTable("documents", (string)null);
		});

		modelBuilder.Entity("PaperlessREST.DAL.DailyDocumentAccess", static b =>
		{
			b.HasOne("PaperlessREST.DAL.DocumentEntity", null)
				.WithMany()
				.HasForeignKey("DocumentId")
				.OnDelete(DeleteBehavior.Cascade)
				.IsRequired();
		});
#pragma warning restore 612, 618
	}
}
