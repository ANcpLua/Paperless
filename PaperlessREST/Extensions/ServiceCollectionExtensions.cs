using System.Text.Json.Serialization;
using Asp.Versioning;
using Elastic.Clients.Elasticsearch;
using FluentValidation;
using Mapster;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Minio;
using PaperlessREST.Services;
using SWEN3.Paperless.RabbitMq;

namespace PaperlessREST.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPaperlessServices(
        this IServiceCollection services,
        IConfiguration config)
    {
        // ----------------------------- Messaging
        services.AddPaperlessRabbitMq(config, includeOcrResultStream: true)
            .AddHostedService<OcrResultListener>();

        // ----------------------------- MinIO options + client
        services.AddOptionsWithValidateOnStart<MinioOptions>()
            .BindConfiguration("Storage:Minio")
            .ValidateDataAnnotations();

        // MinioClient is thread-safe and can be registered as a singleton.
        services.AddSingleton<IMinioClient>(sp =>
        {
            var opt = sp.GetRequiredService<IOptions<MinioOptions>>().Value;
            var logger = sp.GetService<ILogger<IMinioClient>>();

            logger?.LogDebug(
                "Creating MinIO client - Endpoint: {Endpoint}, AccessKey: {AccessKey}, Bucket: {Bucket}, SSL: {SSL}",
                opt.Endpoint, opt.AccessKey, opt.BucketName, opt.UseSsl);

            if (string.IsNullOrWhiteSpace(opt.Endpoint))
            {
                throw new InvalidOperationException("MinIO endpoint is not configured");
            }

            return new MinioClient()
                .WithEndpoint(opt.Endpoint)
                .WithCredentials(opt.AccessKey, opt.SecretKey)
                .WithSSL(opt.UseSsl)
                .Build();
        });

        // ----------------------------- Elasticsearch
        services.AddSingleton(new ElasticsearchClient(
            new ElasticsearchClientSettings(new Uri(config["Elasticsearch:Uri"]!))
                .DefaultIndex(config["Elasticsearch:IndexName"]!)
                .ThrowExceptions()));

        // ----------------------------- Data layer
        services.AddDbContextFactory<DocumentPersistence>((_, opts) =>
        {
            opts.UseNpgsql(config.GetConnectionString("PaperlessDb"),
                o => o.MapEnum<DocumentStatus>());
        });

        // ----------------------------- Domain services
        // Changed to Singleton as their dependencies are now singletons or handled by factories.
        services.AddSingleton<IDocumentRepository, DocumentRepository>()
            .AddSingleton<IDocumentStorageService, DocumentStorageService>()
            .AddSingleton<IDocumentSearchService, DocumentSearchService>()
            .AddSingleton<IDocumentService, DocumentService>();

        // ----------------------------- Validation
        services.AddValidatorsFromAssemblyContaining<Program>(ServiceLifetime.Singleton);

        // ----------------------------- OpenAPI
        services.AddOpenApi(o =>
        {
            o.CreateSchemaReferenceId = t =>
                t.Type.IsEnum ? null : OpenApiOptions.CreateDefaultSchemaReferenceId(t);

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
    public static IServiceCollection AddErrorHandlingAndValidation(this IServiceCollection services)
    {
        services.AddValidation();
        services.Configure<TimeOffsetOptions>(o => o.Offset = TimeSpan.Zero);
        services.AddExceptionHandler<OptimizedExceptionHandler>();
        services.AddProblemDetails();
        services.ConfigureOptions<ProblemDetailsCustomization>();
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
        builder.Services.AddErrorHandlingAndValidation();
    }
}