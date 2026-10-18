using Application.CQRS.Goals.Requests.Commands;
using FluentValidation;

namespace Application.Validation.Goal;

public class UpdateGoalValidator : AbstractValidator<UpdateGoalCommandRequest>
{
    public UpdateGoalValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Goal ID is required.");

        RuleFor(x => x.Name)
            .MaximumLength(200).WithMessage("Goal name must be 200 characters or fewer.")
            .When(x => !string.IsNullOrWhiteSpace(x.Name));

        // Partial update: the cap only applies when a weight is submitted. Same range
        // rule as create - the sum cap lives in the handler transaction (ADR-009).
        RuleFor(x => x.Weight)
            .GreaterThan(0m).WithMessage("Goal weight must be greater than 0.")
            .LessThanOrEqualTo(100m).WithMessage("Goal weight must be 100 or less.")
            .When(x => x.Weight.HasValue);
    }
}
