using Contract;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Moq;
using PaperlessREST.Validation;

namespace Tests.ValidationBL;

[TestFixture]
public class UploadDtoValidatorTests
{
    private IValidator<DocumentDto> _validator = null!;

    [SetUp]
    public void Setup()
    {
        _validator = new DocumentUploadDtoValidator();
    }

    # region Validation Tests
    
    [Test]
    public void Validate_ValidUploadDto_Passes()
    {
        // Arrange
        var formFile = CreateMockFormFile("test.pdf", "application/pdf", 1024 * 1024);
        var uploadDto = new DocumentDto
        {
            Name = "Test Document",
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
        var uploadDto = new DocumentDto
        {
            Name = "",
            File = formFile
        };

        // Act
        var result = _validator.Validate(uploadDto);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.With.Property("PropertyName").EqualTo("Name"));
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
    
    #endregion
}
