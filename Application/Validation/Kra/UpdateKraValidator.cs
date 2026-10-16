using Application.CQRS.Kra.Requests.Commands;
using FluentValidation;

namespace Application.Validation.Kra;

public class UpdateKraValidator : AbstractValidator<UpdateKraCommandRequest>
{
    public UpdateKraValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("KRA ID is required.");

        RuleFor(x => x.Name)
            .MaximumLength(200).WithMessage("KRA name must be 200 characters or fewer.")
            .When(x => !string.IsNullOrWhiteSpace(x.Name));
    }
}
