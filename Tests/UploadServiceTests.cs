using AutoMapper;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using PaperlessREST.AutoMapper;
using PaperlessREST.Models;
using PaperlessREST.Services;
using PaperlessREST.Validation;
using PostgreSQL.Persistence;

namespace Tests;

[TestFixture]
public class UploadServiceTests
{
    private Mock<IDocumentRepository> _mockRepo = null!;
    private IMapper _mapper = null!;
    private Mock<ILogger<UploadService>> _mockLogger = null!;
    private UploadService _service = null!;
    private IValidator<PaperlessREST.DomainModel.Document> _validator = null!;

    [SetUp]
    public void Setup()
    {
        _mockRepo = new Mock<IDocumentRepository>();
        _mockLogger = new Mock<ILogger<UploadService>>();

        var mapperConfig = new MapperConfiguration(cfg => { cfg.AddProfile<DocumentProfile>(); });
        _mapper = mapperConfig.CreateMapper();

        _validator = new DocumentValidator();

        _service = new UploadService(
            _mockRepo.Object,
            _validator,
            _mapper,
            _mockLogger.Object);
    }

    [Test]
    public async Task Upload_ValidDocument_ReturnsDocumentDto()
    {
        // Arrange
        var formFile = CreateMockFormFile("test.pdf", "application/pdf", 1024 * 1024);
        var uploadDto = new DocumentUploadDto
        {
            Title = "test.pdf",
            File = formFile
        };

        _mockRepo.Setup(r => r.Upload(It.IsAny<PostgreSQL.Entities.Document>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((PostgreSQL.Entities.Document doc, CancellationToken _) => new PostgreSQL.Entities.Document
                 {
                     Id = 1,
                     Name = doc.Name,
                     FilePath = doc.FilePath,
                     DateUploaded = doc.DateUploaded
                 });

        // Act
        var result = await _service.Upload(uploadDto);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Is.EqualTo("test.pdf"));
        _mockRepo.Verify(r => r.Upload(It.IsAny<PostgreSQL.Entities.Document>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public Task Upload_InvalidDocument_ThrowsValidationException() {
        // Arrange
        var formFile = CreateMockFormFile("test.invalid", "application/octet-stream", 11 * 1024 * 1024);
        var uploadDto = new DocumentUploadDto {
            Title = "",
            File = formFile
        };

        // Act & Assert
        var ex = Assert.ThrowsAsync<ValidationException>(() => _service.Upload(uploadDto));
        Assert.That(ex.Message, Is.EqualTo("Validation failed: \r\n -- Name: 'Name' darf nicht leer sein. Severity: Error"));
        return Task.CompletedTask;
    }

    private static IFormFile CreateMockFormFile(string fileName, string contentType, long length)
    {
        var content = new byte[] { 0x12, 0x34, 0x56, 0x78 };
        var stream = new MemoryStream(content);

        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.FileName).Returns(fileName);
        fileMock.Setup(f => f.ContentType).Returns(contentType);
        fileMock.Setup(f => f.Length).Returns(length);
        fileMock.Setup(f => f.OpenReadStream()).Returns(stream);
        fileMock.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        return fileMock.Object;
    }
}
