using Application.CQRS.Kpi.Requests.Commands;
using FluentValidation;

namespace Application.Validation.Kpi;

public class CreateKpiValidator : AbstractValidator<CreateKpiCommandRequest>
{
    public CreateKpiValidator()
    {
        RuleFor(x => x.ScorecardId)
            .NotEmpty().WithMessage("Scorecard is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("KPI name is required.")
            .MaximumLength(200).WithMessage("KPI name must be 200 characters or fewer.");

        RuleFor(x => x.Weight)
            .GreaterThan(0m).WithMessage("KPI weight must be greater than 0%.")
            .LessThanOrEqualTo(100m).WithMessage("KPI weight cannot exceed 100%.");

        RuleFor(x => x)
            .Must(x => x.KraId.HasValue ^ x.DivisionGoalId.HasValue)
            .WithMessage("A KPI must belong to exactly one parent: a KRA or a division goal, not both or neither.");
    }
}
