using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Paperless Document Management API", Version = "v1" });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

/// Got Controllers.cs now
/// 
// app.MapGet("/documents", () =>
// {
//     var documents = new[]
//     {
//         new { Id = 1, Title = "hardcoded1", FileName = "alexander.pdf", UploadDate = DateTime.UtcNow.AddDays(-1) },
//         new { Id = 2, Title = "hardcoded2", FileName = "stephanie.pdf", UploadDate = DateTime.UtcNow },
//         new { Id = 3, Title = "hardcoded3", FileName = "jasmin.pdf", UploadDate = DateTime.UtcNow }
//     };
//     return Results.Ok(documents);
// })
// .WithName("GetAllDocuments")
// .WithOpenApi();
//
// app.MapGet("/documents/{id}", (int id) =>
// {
//     var document = new { Id = id, Title = $"Document {id}", FileName = $"file{id}.pdf", UploadDate = DateTime.UtcNow };
//     return Results.Ok(document);
// })
// .WithName("GetDocumentById")
// .WithOpenApi();
//
// app.MapPost("/documents", (HttpContext context) =>
// {
//     var mockFile = new FormFile(Stream.Null, 0, 0, "mockFile", "mockFile.pdf");
//     return Results.Created($"/documents/{new Random().Next(1000)}", new { Message = "Mock document uploaded successfully" });
// })
// .WithName("UploadDocument")
// .WithOpenApi();
//
// app.MapPut("/documents/{id}", (int id) =>
// {
//     return Results.Ok(new { Message = $"Document {id} updated successfully" });
// })
// .WithName("UpdateDocument")
// .WithOpenApi();
//
// app.MapDelete("/documents/{id}", (int id) =>
// {
//     return Results.Ok(new { Message = $"Document {id} deleted successfully" });
// })
// .WithName("DeleteDocument")
// .WithOpenApi();
//
// app.MapGet("/documents/search", (string query) =>
// {
//     var searchResults = new[]
//     {
//         new { Id = 1, Title = "Search Result 1", FileName = "result1.pdf", Relevance = 0.95 },
//         new { Id = 2, Title = "Search Result 2", FileName = "result2.pdf", Relevance = 0.85 },
//         new { Id = 3, Title = "Search Result 3", FileName = "result3.pdf", Relevance = 1.00 }
//     };
//     return Results.Ok(searchResults);
// })
// .WithName("SearchDocuments")
// .WithOpenApi();

app.Run();