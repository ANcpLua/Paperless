using Contract.Logger;
using PaperlessREST;
using PaperlessServices.AutoMapper;
using PaperlessServices.BL;
using PaperlessServices.Extensions;
using PaperlessServices.MinIoStorage;
using PostgreSQL.Module;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("rest-appsettings.json", optional: false)
    .AddJsonFile($"rest-appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables()  
    .Build();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.Configure<LoggerFilterOptions>(options => {
    options.AddFilter("Microsoft", LogLevel.Warning);
    options.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
    options.AddFilter("System", LogLevel.Warning);
});
builder.Services.AddControllers();
builder.Services.AddOperationLogging(
    builder.Environment.EnvironmentName);
builder.Services.AddAuthorizationBuilder(); 
builder.Services.AddFluentValidationRules();
builder.Services.AddPostgreSqlServices(builder.Configuration);
builder.Services.AddElasticSearchEngine(builder.Configuration);
builder.Services.AddMinioObjectStorage(builder.Configuration, builder.Environment);
builder.Services.AddRabbitMqMessageBus(builder.Configuration, builder.Environment);
builder.Services.AddSingleton<IMinioStorageService, MinioStorageService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddCors(options => {
    options.AddPolicy("AllowAll", c => c
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader());
});
builder.Services.AddAutoMapper(cfg => cfg.AddProfile<AutoMapperConfig>());
builder.WebHost.UseKestrel(options => { options.ListenAnyIP(8081); });

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors("AllowAll");
app.UseRouting();
app.UseAuthorization();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.MapControllers();

await app.RunAsync();