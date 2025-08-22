using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static class OcrServicesServiceCollectionAdapter
{
    public static IServiceCollection AddOcrServices(this IServiceCollection services, IConfiguration config)
        => PaperlessServices.OcrServicesExtensions.AddOcrServices(services, config);
}

