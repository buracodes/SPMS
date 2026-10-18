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

        // Same FR-302 rule as creation, applied only when a weight change is requested.
        RuleFor(x => x.Weight)
            .GreaterThan(0m).WithMessage("KRA weight must be greater than 0.")
            .LessThanOrEqualTo(100m).WithMessage("KRA weight must be 100 or less.")
            .When(x => x.Weight.HasValue);
    }
}
