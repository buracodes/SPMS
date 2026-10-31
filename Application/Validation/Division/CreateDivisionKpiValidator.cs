using Application.CQRS.Division.Requests.Commands;
using FluentValidation;

namespace Application.Validation.Division
{
    public class CreateDivisionKpiValidator : AbstractValidator<CreateDivisionKpiCommandRequest>
    {
        public CreateDivisionKpiValidator()
        {
            RuleFor(x => x.DivisionId).NotEmpty().WithMessage("Division is required.");
            RuleFor(x => x.ScorecardId).NotEmpty().WithMessage("Scorecard is required.");
            RuleFor(x => x.DivisionGoalId).NotEmpty().WithMessage("Division goal is required.");

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("KPI name is required.")
                .MaximumLength(200).WithMessage("KPI name must be 200 characters or fewer.");

            RuleFor(x => x.Description).MaximumLength(1000).WithMessage("Description must be 1000 characters or fewer.");
            RuleFor(x => x.Unit).MaximumLength(50).WithMessage("Unit must be 50 characters or fewer.");
            RuleFor(x => x.Formula).MaximumLength(500).WithMessage("Formula must be 500 characters or fewer.");

            RuleFor(x => x.Weight)
                .GreaterThan(0).WithMessage("Weight must be greater than 0.")
                .LessThanOrEqualTo(100).WithMessage("Weight cannot exceed 100%.");

            RuleFor(x => x.ToleranceBandPercent)
                .GreaterThanOrEqualTo(0).WithMessage("Tolerance band cannot be negative.")
                .LessThanOrEqualTo(100).WithMessage("Tolerance band cannot exceed 100%.")
                .When(x => x.ToleranceBandPercent.HasValue);

            RuleForEach(x => x.Targets).ChildRules(target =>
            {
                target.RuleFor(t => t.FiscalPeriodId).NotEmpty().WithMessage("Target period is required.");
            });
        }
    }
}
