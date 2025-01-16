using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Contract.Logger;

public static class LoggerExtensions
{
    public static void AddOperationLogging(this IServiceCollection services, string environment)
    {
        services.AddScoped<IOperationLogger>(sp =>
            new OperationLogger(
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<OperationLogger>(),
                environment
            )
        );
    }
}