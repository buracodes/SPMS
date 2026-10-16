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
            .InclusiveBetween(0m, 100m).WithMessage("KPI weight must be between 0 and 100.");
    }
}
