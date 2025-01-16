using Contract.Logger;
using PaperlessServices.Extensions;
using PostgreSQL.Module;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
       .SetBasePath(Directory.GetCurrentDirectory())
       .AddJsonFile("service-appsettings.json", false)
       .AddJsonFile($"service-appsettings.{builder.Environment.EnvironmentName}.json", true)
       .AddEnvironmentVariables()
       .Build();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddFluentValidationRules();
builder.Services.AddControllers();
builder.Services.AddPostgreSqlServices(builder.Configuration);
builder.Services.AddMinioObjectStorage(builder.Configuration, builder.Environment);
builder.Services.AddRabbitMqMessageBus(builder.Configuration, builder.Environment);
builder.Services.AddElasticSearchEngine(builder.Configuration);
builder.Services.AddDocumentProcessing();
builder.Services.AddTesseractOcr();
builder.Services.AddAutoMapperProfiles();
builder.Services.AddOperationLogging(builder.Environment.EnvironmentName);

var app = builder.Build();

await app.RunAsync();
