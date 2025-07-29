using System.Globalization;
using System.Text.Json.Serialization;
using Mapster;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.Options;

namespace PaperlessREST.Extensions;

public static class DependenciesConfig
{
    public static void AddDependencies(this WebApplicationBuilder builder)
    {
        // ------------------------------------------------- HTTP logging
        builder.Services.AddHttpLogging(l =>
            l.LoggingFields = HttpLoggingFields.RequestProperties | HttpLoggingFields.RequestHeaders);

        // ------------------------------------------------- System.Text.Json
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
        });

        // ------------------------------------------------- ProblemDetails
        builder.Services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
            {
                // Apply JSON naming policy to validation errors
                if (ctx.ProblemDetails is HttpValidationProblemDetails validation)
                {
                    var jsonOptions = ctx.HttpContext.RequestServices
                        .GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>()
                        .Value.JsonSerializerOptions;
                    if (jsonOptions.PropertyNamingPolicy is { } policy)
                    {
                        validation.Errors = validation.Errors
                            .ToDictionary(
                                kvp => policy.ConvertName(kvp.Key),
                                kvp => kvp.Value);
                    }
                    // Summarise the number of errors
                    ctx.ProblemDetails.Detail = $"Error(s) occurred: {validation.Errors.Values.Sum(v => v.Length)}";
                }

                // Add common extensions
                ctx.ProblemDetails.Extensions.TryAdd("timestamp", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                ctx.ProblemDetails.Extensions.TryAdd("traceId", ctx.HttpContext.TraceIdentifier);
                ctx.ProblemDetails.Instance = $"{ctx.HttpContext.Request.Method} {ctx.HttpContext.Request.Path}";
            };
        });

        // ------------------------------------------------- Core services
        builder.Services.AddMapster();
        builder.Services.AddValidation(); // .NET10 preview feature
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddPaperlessServices(builder.Configuration);
    }
}