using System.Text.Json.Serialization;
using Mapster;
using Microsoft.AspNetCore.Http.Json;
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
        builder.Services.AddProblemDetails(opts =>
        {
            opts.CustomizeProblemDetails = ctx =>
            {
                ctx.ProblemDetails.Extensions["timestamp"] = DateTimeOffset.UtcNow;

                if (ctx.ProblemDetails is not HttpValidationProblemDetails { Errors.Count: > 0 } validation)
                    return;

                var policy = ctx.HttpContext.RequestServices
                    .GetRequiredService<IOptions<JsonOptions>>()
                    .Value.SerializerOptions.PropertyNamingPolicy;

                if (policy is null) return;

                validation.Errors = validation.Errors.ToDictionary(
                    kvp => string.Join('.', kvp.Key.Split('.').Select(policy.ConvertName)),
                    kvp => kvp.Value);
            };
        });

        // ------------------------------------------------- Core services
        builder.Services.AddMapster();
        builder.Services.AddValidation(); // .NET10 preview feature
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddPaperlessServices(builder.Configuration);
    }
}