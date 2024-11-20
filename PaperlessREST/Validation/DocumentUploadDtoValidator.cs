using FluentValidation;
using PaperlessREST.Models;

namespace PaperlessREST.Validation;

public class DocumentUploadDtoValidator : AbstractValidator<DocumentUploadDto>
{
    private static readonly string[] AllowedFileTypes = { ".pdf", ".doc", ".docx", ".txt" };
    private const int MaxFileSizeInMb = 10;

    public DocumentUploadDtoValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.File)
            .NotNull()
            .Must(f => f.Length <= MaxFileSizeInMb * 1024 * 1024)
            .WithMessage($"File size must be less than {MaxFileSizeInMb}MB")
            .Must(f => AllowedFileTypes.Any(t =>
                Path.GetExtension(f.FileName).Equals(t, StringComparison.OrdinalIgnoreCase)))
            .WithMessage($"File type must be one of: {string.Join(", ", AllowedFileTypes)}");
    }
}
