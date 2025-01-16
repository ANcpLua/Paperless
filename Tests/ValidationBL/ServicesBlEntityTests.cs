using FluentValidation;
using FluentValidation.TestHelper;
using PaperlessServices.Entities;
using PaperlessServices.Validation;

namespace Tests.ValidationBL;

[TestFixture]
public class ServicesBlEntityTests
{
    [SetUp]
    public void Setup()
    {
        _validator = new BlValidation();
    }

    private IValidator<BlDocument> _validator = null!;

    [Test]
    public void Validate_ValidDocument_Passes()
    {
        // Arrange
        var document = new BlDocument
        {
            Name = "test.pdf",
            FilePath = "uploads/test.pdf",
            DateUploaded = DateTime.UtcNow.AddMinutes(-1)
        };

        // Act
        var result = _validator.Validate(document);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void Validate_InvalidDocument_Fails()
    {
        // Arrange
        var document = new BlDocument
        {
            Name = "",
            FilePath = "",
            DateUploaded = DateTime.UtcNow.AddDays(1) // Future date
        };

        // Act
        var result = _validator.Validate(document);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.With.Property("PropertyName").EqualTo("Name"));
            Assert.That(result.Errors, Has.Some.With.Property("PropertyName").EqualTo("FilePath"));
            Assert.That(result.Errors, Has.Some.With.Property("PropertyName").EqualTo("DateUploaded"));
        });
    }

    [TestCase("", "'Name' must not be empty.")]
    [TestCase("   ", "'Name' must not be empty.")]
    public async Task Validate_NameValidation_DetectsInvalidInput(string invalidName, string expectedError)
    {
        // Arrange
        var document = new BlDocument
        {
            Id = 1,
            Name = invalidName,
            FilePath = "/valid/path",
            DateUploaded = DateTime.UtcNow
        };

        // Act
        var validationResult = await _validator.TestValidateAsync(document);

        // Assert
        validationResult.ShouldHaveValidationErrorFor(x => x.Name)
                        .WithErrorMessage(expectedError);
    }

    [Test]
    public async Task Validate_NameValidation_AcceptsValidInput()
    {
        // Arrange
        var document = new BlDocument
        {
            Id = 1,
            Name = "Valid Document Name",
            FilePath = "/valid/path",
            DateUploaded = DateTime.UtcNow
        };

        // Act
        var validationResult = await _validator.TestValidateAsync(document);

        // Assert
        validationResult.ShouldNotHaveValidationErrorFor(x => x.Name);
    }

    [TestCase("", "'File Path' must not be empty.")]
    [TestCase("   ", "'File Path' must not be empty.")]
    public async Task Validate_FilePathValidation_DetectsInvalidInput(string invalidPath, string expectedError)
    {
        // Arrange
        var document = new BlDocument
        {
            Id = 1,
            Name = "Valid Document Name",
            FilePath = invalidPath,
            DateUploaded = DateTime.UtcNow
        };

        // Act
        var validationResult = await _validator.TestValidateAsync(document);

        // Assert
        validationResult.ShouldHaveValidationErrorFor(x => x.FilePath)
                        .WithErrorMessage(expectedError);
    }

    [Test]
    public async Task Validate_FilePathValidation_AcceptsValidInput()
    {
        // Arrange
        var document = new BlDocument
        {
            Id = 1,
            Name = "Valid Document Name",
            FilePath = "/valid/file/path.pdf",
            DateUploaded = DateTime.UtcNow
        };

        // Act
        var validationResult = await _validator.TestValidateAsync(document);

        // Assert
        validationResult.ShouldNotHaveValidationErrorFor(x => x.FilePath);
    }

    [Test]
    public async Task Validate_DateUploadedValidation_DetectsFutureDate()
    {
        // Arrange
        var document = new BlDocument
        {
            Id = 1,
            Name = "Valid Document Name",
            FilePath = "/valid/path",
            DateUploaded = DateTime.UtcNow.AddDays(1)
        };

        // Act
        var validationResult = await _validator.TestValidateAsync(document);

        // Assert
        validationResult.ShouldHaveValidationErrorFor(x => x.DateUploaded)
                        .WithErrorMessage("Upload date cannot be in the future.");
    }

    [Test]
    public async Task Validate_DateUploadedValidation_AcceptsPresentDate()
    {
        // Arrange
        var document = new BlDocument
        {
            Id = 1,
            Name = "Valid Document Name",
            FilePath = "/valid/path",
            DateUploaded = DateTime.UtcNow
        };

        // Act
        var validationResult = await _validator.TestValidateAsync(document);

        // Assert
        validationResult.ShouldNotHaveValidationErrorFor(x => x.DateUploaded);
    }
}
