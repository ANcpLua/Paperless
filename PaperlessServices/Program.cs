using EasyNetQ;
using Elastic.Clients.Elasticsearch;
using FluentValidation;
using FluentValidation.AspNetCore;
using Minio;
using PaperlessServices.BL;
using PaperlessServices.Mapping;
using PaperlessServices.Tesseract;
using PaperlessServices.Validation;
using PostgreSQL.Module;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("service-appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddControllers();
builder.Services.AddAutoMapper(cfg =>
{
    cfg.AddProfile<ServiceMapping>();
});

builder.Services.AddPostgreSqlServices(builder.Configuration);

builder.Services.AddValidatorsFromAssemblyContaining<BlValidation>();
builder.Services.AddFluentValidationAutoValidation();

var bucketName = builder.Configuration["MinIO:BucketName"];
if (string.IsNullOrEmpty(bucketName))
    throw new InvalidOperationException("MinIO BucketName is not configured. Ensure MinIO:BucketName is set in service-appsettings.json");

builder.Services.AddSingleton<MinioClient>(_ =>
{
    var minioClient = new MinioClient()
        .WithEndpoint(builder.Configuration["MinIO:Endpoint"])
        .WithCredentials(
            builder.Configuration["MinIO:AccessKey"],
            builder.Configuration["MinIO:SecretKey"])
        .WithSSL(false)
        .Build();
    return (MinioClient)minioClient;
});

builder.Services.AddSingleton<IBus>(_ =>
{
    var environment = builder.Environment.EnvironmentName;

    var rabbitFromConnection = builder.Configuration.GetConnectionString("Rabbit");
    var rabbitHost = builder.Configuration["RabbitMQ:Host"];
    var rabbitPort = builder.Configuration["RabbitMQ:Port"];
    var rabbitUser = builder.Configuration["RabbitMQ:Username"];
    var rabbitPass = builder.Configuration["RabbitMQ:Password"];

    var connectionString = environment == "Docker"
        ? rabbitFromConnection
        : $"host={rabbitHost};port={rabbitPort};username={rabbitUser};password={rabbitPass}";

    return RabbitHutch.CreateBus(connectionString);
});

builder.Services.AddSingleton<ElasticsearchClient>(_ =>
{
    var elasticUri = builder.Configuration["Elasticsearch:Url"] ?? "http://localhost:9200";
    var settings = new ElasticsearchClientSettings(new Uri(elasticUri))
        .DefaultIndex("paperless-documents");
    return new ElasticsearchClient(settings);
});

builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IStorageService, StorageService>();
builder.Services.AddScoped<IOcrClient, Ocr>();

builder.Services.AddHostedService<OcrWorkerService>();

var app = builder.Build();

await app.RunAsync();
