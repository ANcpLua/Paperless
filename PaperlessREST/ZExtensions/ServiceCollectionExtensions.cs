using System.Text.Json.Serialization;
using Asp.Versioning;
using Elastic.Clients.Elasticsearch;
using FluentValidation;
using Mapster;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Minio;
using SWEN3.Paperless.RabbitMq;

namespace PaperlessREST.ZExtensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPaperlessServices(this IServiceCollection services, IConfiguration config)
    {
        // ----------------------------- Data layer
        services.AddDbContextFactory<DocumentPersistence>((_, opts) =>
        {
            opts.UseNpgsql(config.GetConnectionString("PaperlessDb"), o => o.MapEnum<DocumentStatus>());
        });

        // ----------------------------- Domain services
        // Changed to Singleton as their dependencies are now singletons or handled by factories.
        services.AddSingleton<IDocumentRepository, DocumentRepository>()
            .AddSingleton<IDocumentService, DocumentService>();

        // ----------------------------- Infrastructure (MinIO)
        services.AddOptionsWithValidateOnStart<PaperlessREST.MinioOptions>()
            .BindConfiguration("Storage:Minio")
            .ValidateDataAnnotations();

        services.AddSingleton<IMinioClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<PaperlessREST.MinioOptions>>().Value;
            return new MinioClient()
                .WithEndpoint(opts.Endpoint)
                .WithCredentials(opts.AccessKey, opts.SecretKey)
                .WithSSL(opts.UseSsl)
                .Build();
        });

        // ----------------------------- Infrastructure (Elasticsearch)
        services.AddSingleton(new ElasticsearchClient(
            new ElasticsearchClientSettings(new Uri(config["Elasticsearch:Uri"]!))
                .DefaultIndex(config["Elasticsearch:IndexName"]!)
                .ThrowExceptions()));

        // ----------------------------- Messaging (RabbitMQ)
        services.AddPaperlessRabbitMq(config, includeOcrResultStream: true);

        // ----------------------------- Application services
        services.AddSingleton<IDocumentStorageService, DocumentStorageService>()
                .AddSingleton<IDocumentSearchService, DocumentSearchService>()
                .AddHostedService<OcrResultListener>();

        // ----------------------------- Validation
        services.AddValidatorsFromAssemblyContaining<Program>(ServiceLifetime.Singleton);

        // ----------------------------- OpenAPI
        services.AddOpenApi(o =>
        {
            o.CreateSchemaReferenceId = t => t.Type.IsEnum ? null : OpenApiOptions.CreateDefaultSchemaReferenceId(t);

            o.AddDocumentTransformer((doc, _, _) =>
            {
                doc.Info = new OpenApiInfo
                {
                    Title = "Paperless OCR API",
                    Version = "v1",
                    Description = "API for uploading and processing PDF documents with OCR"
                };
                return Task.CompletedTask;
            });
        });

        // ----------------------------- API versioning
        services.AddApiVersioning(v =>
        {
            v.DefaultApiVersion = new ApiVersion(1, 0);
            v.AssumeDefaultVersionWhenUnspecified = true;
            v.ReportApiVersions = true;
        }).AddApiExplorer(opts =>
        {
            opts.GroupNameFormat = "'v'VVV";
            opts.SubstituteApiVersionInUrl = true;
        });

        return services;
    }
}

public static class ValidationHandlerExtensions
{
    public static IServiceCollection AddOptimizedErrorHandling(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddExceptionHandler<OptimizedExceptionHandler>();
        services.AddProblemDetails();
        services.ConfigureOptions<ProblemDetailsCustomization>();
        services.Configure<TimeOffsetOptions>(_ => { });
        return services;
    }
}

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder ConfigureMiddleware(this IApplicationBuilder app)
    {
        app.UseStaticFiles();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseHttpLogging();
        return app;
    }
}

public static class DependenciesConfig
{
    public static void AddDependencies(this WebApplicationBuilder builder)
    {
        builder.Services.AddHttpLogging(o =>
        {
            o.LoggingFields = HttpLoggingFields.RequestProperties | HttpLoggingFields.RequestHeaders;
        });
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
        });
        builder.Services.Configure<TimeOffsetOptions>(builder.Configuration.GetSection("TimeOffset"));
        builder.Services.AddMapster();
        builder.Services.AddPaperlessServices(builder.Configuration);
        builder.Services.AddOptimizedErrorHandling();
    }
}