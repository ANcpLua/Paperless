using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mapster;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace PaperlessREST.Extensions;

public sealed class TimeOffsetOptions
{
    public TimeSpan Offset { get; init; }
}

public static class DependenciesConfig
{
    public static void AddDependencies(this WebApplicationBuilder builder)
    {
        builder.Services.AddHttpLogging(o =>
            o.LoggingFields = HttpLoggingFields.RequestProperties | HttpLoggingFields.RequestHeaders);

        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
        });

        builder.Services.Configure<TimeOffsetOptions>(builder.Configuration.GetSection("TimeOffset"));

        builder.Services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
            {
                if (ctx.ProblemDetails is HttpValidationProblemDetails validation)
                {
                    var jsonOptions = ctx.HttpContext.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value
                        .JsonSerializerOptions;
                    if (jsonOptions.PropertyNamingPolicy is { } policy)
                    {
                        validation.Errors =
                            validation.Errors.ToDictionary(kvp => policy.ConvertName(kvp.Key), kvp => kvp.Value);
                    }

                    ctx.ProblemDetails.Detail = $"Errors: {validation.Errors.Values.Sum(v => v.Length)}";
                }

                ctx.ProblemDetails.Title ??=
                    ReasonPhrases.GetReasonPhrase(ctx.ProblemDetails.Status ??
                                                  StatusCodes.Status500InternalServerError);
                ctx.ProblemDetails.Instance ??= $"{ctx.HttpContext.Request.Method} {ctx.HttpContext.Request.Path}";

                var nowUtc = DateTimeOffset.UtcNow;
                ctx.ProblemDetails.Extensions.TryAdd("timestamp", nowUtc);

                var offset = ctx.HttpContext.RequestServices.GetRequiredService<IOptions<TimeOffsetOptions>>().Value
                    .Offset;
                if (offset != TimeSpan.Zero)
                {
                    ctx.ProblemDetails.Extensions.TryAdd("offset", offset.ToString());
                    ctx.ProblemDetails.Extensions.TryAdd("timestampLocal", nowUtc.ToOffset(offset));
                }

                var code = MapCode(ctx.Exception, ctx.ProblemDetails.Status);
                if (code is not null) ctx.ProblemDetails.Extensions["code"] = code;

                var activity = ctx.HttpContext.Features.Get<IHttpActivityFeature>()?.Activity ?? Activity.Current;
                var activityId = activity?.Id;
                var traceId = activity?.TraceId.ToString();
                ctx.ProblemDetails.Extensions.TryAdd("traceId", activityId ?? ctx.HttpContext.TraceIdentifier);
                if (!string.IsNullOrEmpty(traceId)) ctx.ProblemDetails.Extensions.TryAdd("traceIdHex", traceId);
                ctx.ProblemDetails.Extensions.TryAdd("requestId", ctx.HttpContext.TraceIdentifier);
                ctx.ProblemDetails.Extensions.TryAdd("endpoint",
                    ctx.HttpContext.GetEndpoint()?.DisplayName ?? "unknown");

                var entry = Assembly.GetEntryAssembly()?.GetName();
                if (entry is not null)
                {
                    ctx.ProblemDetails.Extensions.TryAdd("serviceName", entry.Name);
                    ctx.ProblemDetails.Extensions.TryAdd("serviceVersion", entry.Version?.ToString());
                }

                ctx.ProblemDetails.Type ??= code is not null
                    ? $"urn:problem-type:{code}"
                    : $"urn:problem-status:{ctx.ProblemDetails.Status ?? StatusCodes.Status500InternalServerError}";

                var env = ctx.HttpContext.RequestServices.GetRequiredService<IHostEnvironment>();
                if (env.IsDevelopment() && ctx.Exception is not null)
                {
                    ctx.ProblemDetails.Extensions.TryAdd("exception", new
                    {
                        type = ctx.Exception.GetType().FullName,
                        message = ctx.Exception.Message,
                        stack = ctx.Exception.StackTrace
                    });
                }
            };
        });

        builder.Services.AddMapster();
        builder.Services.AddValidation();
        builder.Services.AddPaperlessServices(builder.Configuration);
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    }

    private static string? MapCode(Exception? ex, int? status)
    {
        if (ex is null)
        {
            return status switch
            {
                StatusCodes.Status400BadRequest => "request.invalid",
                StatusCodes.Status401Unauthorized => "auth.unauthorized",
                StatusCodes.Status404NotFound => "resource.notFound",
                _ => null
            };
        }

        return ex switch
        {
            ArgumentException => "request.invalidArgument",
            JsonException => "request.malformedJson",
            UnauthorizedAccessException => "auth.unauthorized",
            KeyNotFoundException => "resource.notFound",
            NotSupportedException => "request.unsupported",
            TimeoutException => "request.timeout",
            OperationCanceledException => "request.canceled",
            NotImplementedException => "server.notImplemented",
            not null when ex.GetType().Name == "ValidationException" => "request.validationFailed",
            _ => "error.unexpected"
        };
    }
}