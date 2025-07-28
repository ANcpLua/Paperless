using Asp.Versioning;
using Elastic.Clients.Elasticsearch;
using FluentValidation;
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

        services.AddScoped<IMinioClient>(sp =>
        {
            var opt = sp.GetRequiredService<IOptions<MinioOptions>>().Value;
            var logger = sp.GetService<ILogger<IMinioClient>>();
            
            logger?.LogDebug("Creating MinIO client - Endpoint: {Endpoint}, AccessKey: {AccessKey}, Bucket: {Bucket}, SSL: {SSL}", 
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
        services.AddSingleton<IDocumentRepository, DocumentRepository>()
            .AddScoped<IDocumentStorageService, DocumentStorageService>()
            .AddSingleton<IDocumentSearchService, DocumentSearchService>()
            .AddScoped<IDocumentService, DocumentService>();

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