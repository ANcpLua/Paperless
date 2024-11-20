using FluentValidation;
using PaperlessREST.DomainModel;

namespace PaperlessREST.Validation;

public class DocumentValidator : AbstractValidator<Document>
{
    public DocumentValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.FilePath)
            .NotEmpty();

        RuleFor(x => x.DateUploaded)
            .NotEmpty()
            .Must(date => date <= DateTime.UtcNow.AddSeconds(1))
            .WithMessage("Upload date cannot be in the future");
    }
}
