using Application.CQRS.Division.Requests.Commands;
using FluentValidation;

namespace Application.Validation.Division
{
    public class UpdateDivisionGoalValidator : AbstractValidator<UpdateDivisionGoalCommandRequest>
    {
        public UpdateDivisionGoalValidator()
        {
            RuleFor(x => x.DivisionGoalId).NotEmpty().WithMessage("Division goal is required.");

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Division goal name is required.")
                .MaximumLength(200).WithMessage("Division goal name must be 200 characters or fewer.")
                .When(x => x.Name != null);

            RuleFor(x => x.Description)
                .MaximumLength(1000).WithMessage("Description must be 1000 characters or fewer.")
                .When(x => x.Description != null);

            RuleFor(x => x.ContributionDescription)
                .MaximumLength(2000).WithMessage("Contribution description must be 2000 characters or fewer.")
                .When(x => x.ContributionDescription != null);

            RuleFor(x => x.Weight)
                .GreaterThanOrEqualTo(0).WithMessage("Weight cannot be negative.")
                .LessThanOrEqualTo(100).WithMessage("Weight cannot exceed 100%.")
                .When(x => x.Weight.HasValue);
        }
    }
}
