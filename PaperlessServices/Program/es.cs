// ElasticsearchDemo/Services/ElasticsearchInitializer.cs

using System.ComponentModel.DataAnnotations;
using CommunityToolkit.HighPerformance.Helpers;
using Elastic.Clients.Elasticsearch;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace PaperlessServices.Program;

public sealed class ElasticsearchInitializer : IHostedService
{
    private readonly ElasticsearchClient _elastic;
    private readonly IOptions<ElasticsearchOptions> _options;

    public ElasticsearchInitializer(ElasticsearchClient elastic, IOptions<ElasticsearchOptions> options)
    {
        _elastic = elastic;
        _options = options;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var index = _options.Value.IndexName;
        var exists = await _elastic.Indices.ExistsAsync(index, cancellationToken);
        if (!exists.Exists)
        {
            await _elastic.Indices.CreateAsync<dynamic>(index, c => c
                    .Settings(s => s
                        .NumberOfShards(1)
                        .NumberOfReplicas(0)
                        .Analysis(a => a.Analyzers(an => an
                            .Custom("pdf_analyzer",
                                ca => ca.Tokenizer("standard").Filter("lowercase", "stop", "snowball"))
                        ))
                    )
                    .Mappings(m => m.Properties(p => p
                        .Keyword(k => k.Id)
                        .Text(t => t.FileName, td => td.Analyzer("pdf_analyzer"))
                        .Text(t => t.Content, td => td.Analyzer("pdf_analyzer"))
                        .Date(d => d.ProcessedAt)
                        .Completion(cmp => cmp.Suggest)
                    )),
                cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
} // PowerRanger/Ranger.Api/Infrastructure/Validation/DataAnnotationsValidateOptions.cs

public interface IValidateOptionsFactory
{
    IValidateOptions<T> Create<T>() where T : class;
}

public sealed class DataAnnotationsValidateOptionsFactory : IValidateOptionsFactory
{
    public IValidateOptions<T> Create<T>() where T : class => new DataAnnotationsValidateOptions<T>();
}

public sealed class DataAnnotationsValidateOptions<T> : IValidateOptions<T> where T : class
{
    public ValidateOptionsResult Validate(string? name, T options)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(options);
        return Validator.TryValidateObject(options, context, results, validateAllProperties: true)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(string.Join("; ", results.Select(r => r.ErrorMessage)));
    }
}

public class CustomExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problemDetails = new ProblemDetails
        {
            Status = exception is ArgumentException
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status500InternalServerError,
            Title = "An error occurred",
            Type = exception.GetType().Name,
            Detail = exception.Message
        };

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            Exception = exception,
            HttpContext = httpContext,
            ProblemDetails = problemDetails
        });
    }
}