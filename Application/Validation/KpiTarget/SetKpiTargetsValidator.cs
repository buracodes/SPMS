using Application.CQRS.KpiTargets.Requests.Commands;
using FluentValidation;

namespace Application.Validation.KpiTarget
{
    public class SetKpiTargetsValidator : AbstractValidator<SetKpiTargetsCommandRequest>
    {
        public SetKpiTargetsValidator()
        {
            RuleFor(x => x.Dto)
                .NotNull().WithMessage("Target data is required.");

            RuleFor(x => x.Dto.KpiId)
                .NotEmpty().WithMessage("KPI is required.");

            RuleFor(x => x.Dto.FiscalYearId)
                .NotEmpty().WithMessage("Fiscal year is required.");

            RuleFor(x => x.Dto.MonthlyTargets)
                .NotNull().WithMessage("Monthly targets are required.")
                .Must(targets => targets.Count == 12)
                .WithMessage("Monthly targets for all 12 months are required.");

            RuleForEach(x => x.Dto.MonthlyTargets)
                .GreaterThanOrEqualTo(0)
                .WithMessage("Monthly target must be zero or greater.");

            RuleFor(x => x.Dto.AnnualTarget)
                .NotNull().WithMessage("Annual target is required.")
                .GreaterThanOrEqualTo(0)
                .WithMessage("Annual target must be zero or greater.");

            RuleFor(x => x.Dto.QuarterlyTargets)
                .Must(targets => targets == null || targets.Count == 4)
                .WithMessage("Quarterly targets, when provided, must cover all 4 quarters.");

            RuleForEach(x => x.Dto.QuarterlyTargets)
                .GreaterThanOrEqualTo(0)
                .WithMessage("Quarterly target must be zero or greater.");
        }
    }
}
