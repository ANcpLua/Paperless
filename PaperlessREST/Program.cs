using EasyNetQ;
using Elastic.Clients.Elasticsearch;
using FluentValidation;
using FluentValidation.AspNetCore;
using Minio;
using PaperlessREST.Validation;
using PaperlessServices.BL;
using PaperlessServices.Mapping;
using PaperlessServices.Tesseract;
using PostgreSQL.Module;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("rest-appsettings.json", optional: false, reloadOnChange: true);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddPostgreSqlServices(builder.Configuration);
builder.Services.AddSingleton<IBus>(_ =>
{
    var connectionString = builder.Configuration.GetConnectionString("RabbitMQ");
    return RabbitHutch.CreateBus(connectionString);
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", corsPolicyBuilder =>
        corsPolicyBuilder
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader());
});

ConfigureServices(builder.Services, builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors("AllowAll");
app.UseRouting();
app.UseAuthorization();

app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
    endpoints.MapFallbackToFile("/index.html");
});

await app.RunAsync();

static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
{
    // MinIO
    services.AddSingleton<MinioClient>(_ =>
        (MinioClient)new MinioClient()
            .WithEndpoint(configuration["MinIO:Endpoint"])
            .WithCredentials(
                configuration["MinIO:AccessKey"],
                configuration["MinIO:SecretKey"])
            .WithSSL(false)
            .Build());

    // Elasticsearch 
    services.AddSingleton<ElasticsearchClient>(_ =>
    {
        var elasticUri = configuration.GetConnectionString("ElasticSearch") ?? "http://localhost:9200";
        var settings = new ElasticsearchClientSettings(new Uri(elasticUri))
            .DefaultIndex("paperless-documents")  
            .EnableDebugMode();
        return new ElasticsearchClient(settings);
    });

    services.AddScoped<IDocumentService, DocumentService>();
    services.AddSingleton<IStorageService, StorageService>();
    services.AddScoped<OcrWorkerService>();
    services.Configure<OcrOptions>(configuration.GetSection(OcrOptions.Ocr));
    services.AddSingleton<IOcrClient, Ocr>();
    services.AddValidatorsFromAssemblyContaining<DocumentUploadDtoValidator>();
    services.AddFluentValidationAutoValidation();
    services.AddAutoMapper(cfg => {
        cfg.AddProfile<ServiceMapping>();
    });
}