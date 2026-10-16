using Application.CQRS.Kra.Requests.Commands;
using FluentValidation;

namespace Application.Validation.Kra;

public class CreateKraValidator : AbstractValidator<CreateKraCommandRequest>
{
    public CreateKraValidator()
    {
        RuleFor(x => x.GoalId)
            .NotEmpty().WithMessage("Goal is required.");

        RuleFor(x => x.ScorecardId)
            .NotEmpty().WithMessage("Scorecard is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("KRA name is required.")
            .MaximumLength(200).WithMessage("KRA name must be 200 characters or fewer.");

        RuleFor(x => x.Weight)
            .InclusiveBetween(0m, 100m).WithMessage("KRA weight must be between 0 and 100.");
    }
}
