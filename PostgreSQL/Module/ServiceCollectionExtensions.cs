using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PostgreSQL.Data;
using PostgreSQL.Persistence;

namespace PostgreSQL.Module
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddPostgreSqlServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<PaperlessDbContext>(options =>
                options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));
            services.AddScoped<IDocumentRepository, DocumentRepository>();
            return services;
        }
    }
}
