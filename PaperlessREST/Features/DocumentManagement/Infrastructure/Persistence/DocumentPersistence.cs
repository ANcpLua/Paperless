namespace PaperlessREST.Features.DocumentManagement.Infrastructure.Persistence;

public sealed class DocumentPersistence(DbContextOptions<DocumentPersistence> options) : DbContext(options)
{
	public DbSet<DocumentEntity> Documents => Set<DocumentEntity>();
	public DbSet<DailyDocumentAccess> DailyDocumentAccesses => Set<DailyDocumentAccess>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<DocumentEntity>(static e =>
		{
			e.ToTable("documents");
			e.Property(x => x.Id).HasColumnName("id");
			e.Property(x => x.FileName).HasMaxLength(255).HasColumnName("file_name");
			e.Property(x => x.StoragePath).HasMaxLength(500).HasColumnName("storage_path");
			e.Property(x => x.Status).HasColumnName("status").HasColumnType("document_status");
			e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP").HasColumnName("created_at");
			e.Property(x => x.Content).HasMaxLength(1_000_000).HasColumnName("content");
			e.Property(x => x.Summary).HasMaxLength(5000).HasColumnName("summary");
			e.Property(x => x.ProcessedAt).HasColumnName("processed_at");
			e.Property(x => x.SummaryGeneratedAt).HasColumnName("summary_generated_at");
			e.HasIndex(x => x.FileName);
		});

		modelBuilder.Entity<DailyDocumentAccess>(static e =>
		{
			e.ToTable("daily_document_access");
			e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
			e.Property(x => x.DocumentId).HasColumnName("document_id");
			e.Property(x => x.LogDate).HasColumnName("log_date");
			e.Property(x => x.AccessCount).HasColumnName("access_count");
			e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

			e.HasOne<DocumentEntity>()
				.WithMany()
				.HasForeignKey(x => x.DocumentId)
				.OnDelete(DeleteBehavior.Cascade);

			e.HasIndex(x => new { x.DocumentId, x.LogDate }).IsUnique();
			e.HasIndex(x => x.LogDate);
		});
	}
}

[ExcludeFromCodeCoverage(Justification =
	"Design-time EF migration factory - only invoked by dotnet-ef CLI tooling, never at runtime")]
public sealed class DocumentPersistenceFactory : IDesignTimeDbContextFactory<DocumentPersistence>
{
	public DocumentPersistence CreateDbContext(string[] args)
	{
		Env.Load(".env");

		var cs = Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__PAPERLESSDB")
		         ?? throw new InvalidOperationException("CONNECTIONSTRINGS__PAPERLESSDB not set");

		var dataSource = new NpgsqlDataSourceBuilder(cs)
			.MapEnum<DocumentStatus>("document_status")
			.Build();

		var opts = new DbContextOptionsBuilder<DocumentPersistence>()
			.UseNpgsql(dataSource, static o => o.MapEnum<DocumentStatus>("document_status"))
			.Options;

		return new DocumentPersistence(opts);
	}
}

[ExcludeFromCodeCoverage(Justification =
	"EF entity - pure data container with no logic; persistence tested via integration tests")]
public sealed class DocumentEntity
{
	public required Guid Id { get; set; }
	public required string FileName { get; set; }
	public required DocumentStatus Status { get; set; }
	public required DateTimeOffset CreatedAt { get; set; }
	public required string StoragePath { get; set; }
	public string? Content { get; set; }
	public string? Summary { get; set; }
	public DateTimeOffset? ProcessedAt { get; set; }
	public DateTimeOffset? SummaryGeneratedAt { get; set; }
}

[ExcludeFromCodeCoverage(Justification =
	"EF entity - pure data container with no logic; persistence tested via integration tests")]
public sealed class DailyDocumentAccess
{
	public Guid Id { get; set; } // Database-generated, not set by application code
	public required Guid DocumentId { get; set; }
	public required DateOnly LogDate { get; set; }
	public required long AccessCount { get; set; }
	public DateTimeOffset UpdatedAt { get; set; }
}
