using Microsoft.OpenApi.Models;
// using Microsoft.EntityFrameworkCore;
// using AutoMapper; https://medium.com/@supino0017/automapper-for-object-mapping-in-net-8-5b20a034de8c
// using Nest; https://www.elastic.co/guide/en/elasticsearch/client/net-api/current/examples.html
// using RabbitMQ.Client; https://rabbitmq.github.io/rabbitmq-dotnet-client/api/RabbitMQ.Client.html
// using FluentValidation; k kenn auswendig, aber ist für validation, siehe https://docs.fluentvalidation.net/en/latest/custom-validators.html
// using FluentValidation.AspNetCore; https://docs.fluentvalidation.net/en/latest/aspnet.html
// using Serilog; // aop für logging und error handling lektoren fragen im code review, ob per attribute einfacher [BL],[DAL] etc. machen erspart boilerplate

//  siehe AOP example im wasm(UI) ist die browserconsole  https://github.com/ANcpLua/TourPlanner/blob/main/API/AOP/ApiMethodDecorator.cs

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Paperless Document Management API", Version = "v1" });
});

// These services are commented out for now, but will be uncommented and configured in future sprints
// builder.Services.AddDbContext<ApplicationDbContext>(options =>
//     options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
// builder.Services.AddAutoMapper(typeof(Program));
// builder.Services.AddElasticsearch(builder.Configuration);
// builder.Services.AddRabbitMQ(builder.Configuration);
// builder.Services.AddFluentValidationAutoValidation();
// builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
// builder.Host.UseSerilog((context, configuration) => 
//     configuration.ReadFrom.Configuration(context.Configuration));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// READ: Get all documents
app.MapGet("/documents", () =>
{
    var documents = new[]
    {
        new { Id = 1, Title = "hardcoded1", FileName = "alexander.pdf", UploadDate = DateTime.UtcNow.AddDays(-1) },
        new { Id = 2, Title = "hardcoded2", FileName = "stephanie.pdf", UploadDate = DateTime.UtcNow },
        new { Id = 3, Title = "hardcoded3", FileName = "jasmin.pdf", UploadDate = DateTime.UtcNow }
    };
    return Results.Ok(documents);
})
.WithName("GetAllDocuments")
.WithOpenApi();

// READ: Get a specific document by ID
app.MapGet("/documents/{id}", (int id) =>
{
    var document = new { Id = id, Title = $"Document {id}", FileName = $"file{id}.pdf", UploadDate = DateTime.UtcNow };
    return Results.Ok(document);
})
.WithName("GetDocumentById")
.WithOpenApi();

app.MapPost("/documents", (HttpContext context) =>
{
    var mockFile = new FormFile(Stream.Null, 0, 0, "mockFile", "mockFile.pdf");
    // In future: Save document to storage, index in Elasticsearch
    return Results.Created($"/documents/{new Random().Next(1000)}", new { Message = "Mock document uploaded successfully" });
})
.WithName("UploadDocument")
.WithOpenApi();

// UPDATE: Update a document's metadata
app.MapPut("/documents/{id}", (int id) =>
{
    // In future: Update document metadata, re-index in Elasticsearch
    return Results.Ok(new { Message = $"Document {id} updated successfully" });
})
.WithName("UpdateDocument")
.WithOpenApi();

// DELETE: Delete a document
app.MapDelete("/documents/{id}", (int id) =>
{
    // In future: Delete document from storage and Elasticsearch index
    return Results.Ok(new { Message = $"Document {id} deleted successfully" });
})
.WithName("DeleteDocument")
.WithOpenApi();

// SEARCH: Full-text search (to be implemented with Elasticsearch in future sprints)
app.MapGet("/documents/search", (string query) =>
{
    var searchResults = new[]
    {
        new { Id = 1, Title = "Search Result 1", FileName = "result1.pdf", Relevance = 0.95 },
        new { Id = 2, Title = "Search Result 2", FileName = "result2.pdf", Relevance = 0.85 },
        new { Id = 3, Title = "Search Result 3", FileName = "result3.pdf", Relevance = 1.00 }
    };
    return Results.Ok(searchResults);
})
.WithName("SearchDocuments")
.WithOpenApi();

app.Run();
 
