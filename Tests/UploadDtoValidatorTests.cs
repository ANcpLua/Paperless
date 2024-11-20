using FluentValidation;
using Microsoft.AspNetCore.Http;
using Moq;
using PaperlessREST.Models;
using PaperlessREST.Validation;

namespace Tests;

[TestFixture]
public class UploadDtoValidatorTests
{
    private IValidator<DocumentUploadDto> _validator = null!;

    [SetUp]
    public void Setup()
    {
        _validator = new DocumentUploadDtoValidator();
    }

    [Test]
    public void Validate_ValidUploadDto_Passes()
    {
        // Arrange
        var formFile = CreateMockFormFile("test.pdf", "application/pdf", 1024 * 1024);
        var uploadDto = new DocumentUploadDto
        {
            Title = "Test Document",
            File = formFile
        };

        // Act
        var result = _validator.Validate(uploadDto);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void Validate_InvalidUploadDto_Fails()
    {
        // Arrange
        var formFile = CreateMockFormFile("test.invalid", "application/octet-stream", 11 * 1024 * 1024);
        var uploadDto = new DocumentUploadDto
        {
            Title = "",
            File = formFile
        };

        // Act
        var result = _validator.Validate(uploadDto);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.With.Property("PropertyName").EqualTo("Title"));
            Assert.That(result.Errors, Has.Some.With.Property("PropertyName").EqualTo("File"));
        });
    }

    private static IFormFile CreateMockFormFile(string fileName, string contentType, long length)
    {
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.FileName).Returns(fileName);
        fileMock.Setup(f => f.ContentType).Returns(contentType);
        fileMock.Setup(f => f.Length).Returns(length);
        return fileMock.Object;
    }
}
