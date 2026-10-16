using Application.CQRS.Division.Requests.Commands;
using FluentValidation;

namespace Application.Validation.Division
{
    public class CreateDivisionGoalValidator : AbstractValidator<CreateDivisionGoalCommandRequest>
    {
        public CreateDivisionGoalValidator()
        {
            RuleFor(x => x.DivisionId).NotEmpty().WithMessage("Division is required.");
            RuleFor(x => x.ScorecardId).NotEmpty().WithMessage("Scorecard is required.");
            RuleFor(x => x.ParentGoalId).NotEmpty().WithMessage("Parent corporate goal is required.");

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Division goal name is required.")
                .MaximumLength(200).WithMessage("Division goal name must be 200 characters or fewer.");

            RuleFor(x => x.Description).MaximumLength(1000).WithMessage("Description must be 1000 characters or fewer.");
            RuleFor(x => x.ContributionDescription).MaximumLength(2000).WithMessage("Contribution description must be 2000 characters or fewer.");

            RuleFor(x => x.Weight)
                .GreaterThanOrEqualTo(0).WithMessage("Weight cannot be negative.")
                .LessThanOrEqualTo(100).WithMessage("Weight cannot exceed 100%.");
        }
    }
}
