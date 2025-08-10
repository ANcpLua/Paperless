using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;
using Npgsql.NameTranslation;

namespace PaperlessREST;

public class DocumentPersistenceDesignTimeFactory : IDesignTimeDbContextFactory<DocumentPersistence>
{
    public DocumentPersistence CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();
        
        var optionsBuilder = new DbContextOptionsBuilder<DocumentPersistence>();
        var connectionString = configuration.GetConnectionString("PaperlessDb");
        
        // Configure the data source with enum mapping
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.MapEnum<DocumentStatus>("document_status", new NpgsqlSnakeCaseNameTranslator());
        var dataSource = dataSourceBuilder.Build();
        
        optionsBuilder.UseNpgsql(dataSource);
        
        return new DocumentPersistence(optionsBuilder.Options);
    }
}