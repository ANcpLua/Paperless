using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PostgreSQL.Data;
using PostgreSQL.Persistence;

namespace PostgreSQL.Module;

public static class ServiceCollectionExtensions
{
    public static void AddPostgreSqlServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        services.AddDbContext<PaperlessDbContext>(options =>
            options.UseNpgsql(connectionString, npgsqlOptions =>
                npgsqlOptions.MigrationsAssembly("PostgreSQL")));

        services.AddScoped<IDocumentRepository, DocumentRepository>();
    }
}