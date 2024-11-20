using PostgreSQL.Entities;
using Microsoft.EntityFrameworkCore;

namespace PostgreSQL.Data;

public class PaperlessDbContext : DbContext
{
    public PaperlessDbContext(DbContextOptions<PaperlessDbContext> options) 
        : base(options)
    {
    }

    public DbSet<Document> Documents { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.ToTable("Documents");

            entity.Property(e => e.Id)
                  .HasColumnName("Id")
                  .IsRequired();

            entity.Property(e => e.Name)
                  .HasColumnName("Name")
                  .IsRequired();

            entity.Property(e => e.DateUploaded)
                  .HasColumnName("DateUploaded")
                  .IsRequired();

            entity.Property(e => e.FilePath)
                  .HasColumnName("FilePath")
                  .IsRequired();
        });

        base.OnModelCreating(modelBuilder);
    }
}
