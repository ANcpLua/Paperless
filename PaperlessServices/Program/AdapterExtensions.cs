namespace PaperlessServices.Program;

public static class OcrServicesServiceCollectionAdapter
{
    public static IServiceCollection AddOcrServices(this IServiceCollection services, IConfiguration config)
        => OcrServicesExtensions.AddOcrServices(services, config);
}

