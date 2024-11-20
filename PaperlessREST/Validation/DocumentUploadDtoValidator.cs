using Contract;
using FluentValidation;

namespace PaperlessREST.Validation;

public class DocumentUploadDtoValidator : AbstractValidator<DocumentDto>
{
    public DocumentUploadDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Document name cannot be empty.");

        RuleFor(x => x.File)
            .NotNull()
            .WithMessage("File is required.")
            .Must(file => file != null && IsAllowedContentType(file.ContentType))
            .WithMessage("File must be a PDF, PNG, or JPG.");
    }

    private bool IsAllowedContentType(string contentType)
    {
        var allowedContentTypes = new[] { "application/pdf", "image/png", "image/jpeg" };
        return allowedContentTypes.Contains(contentType);
    }
}