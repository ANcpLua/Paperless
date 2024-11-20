using FluentValidation;
using PaperlessREST.AutoMapper;
using PaperlessREST.Models;
using PaperlessREST.Services;
using PaperlessREST.Validation;
using PostgreSQL.Entities;
using PostgreSQL.Module;
using PostgreSQL.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSwaggerGen();
builder.Services.AddAutoMapper(typeof(DocumentProfile));

builder.Services.AddScoped<IValidator<Document>, DocumentEntityValidator>();
builder.Services.AddScoped<IValidator<PaperlessREST.DomainModel.Document>, DocumentValidator>();
builder.Services.AddScoped<IValidator<DocumentUploadDto>, DocumentUploadDtoValidator>();

builder.Services.AddScoped<IUploadService, UploadService>();
builder.Services.AddScoped<IGetDocumentService, GetDocumentService>();
builder.Services.AddScoped<IDeleteDocumentService, DeleteDocumentService>();

builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();

builder.Services.AddPostgreSqlServices(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles(); 
app.UseAuthorization();

app.MapControllers();

app.Run();
