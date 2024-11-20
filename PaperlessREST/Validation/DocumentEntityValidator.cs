using FluentValidation;
using PostgreSQL.Entities;

namespace PaperlessREST.Validation;

public class DocumentEntityValidator : AbstractValidator<Document>
{
    public DocumentEntityValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.FilePath)
            .NotEmpty();

        RuleFor(x => x.DateUploaded)
            .NotEmpty()
            .LessThanOrEqualTo(DateTime.UtcNow.AddSeconds(1));
    }
}
