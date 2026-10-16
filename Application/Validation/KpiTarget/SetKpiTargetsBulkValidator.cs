using Application.CQRS.KpiTargets.Requests.Commands;
using FluentValidation;

namespace Application.Validation.KpiTarget
{
    /// <summary>
    /// Shape checks for the bulk save only. The per-KPI rules (12 months, annual target,
    /// quarters, non-negative values) belong to the single-KPI command that every item is
    /// routed through, so a violation is reported against that KPI's row instead of
    /// rejecting the whole batch here.
    /// </summary>
    public class SetKpiTargetsBulkValidator : AbstractValidator<SetKpiTargetsBulkCommandRequest>
    {
        public SetKpiTargetsBulkValidator()
        {
            RuleFor(x => x.Dto)
                .NotNull().WithMessage("Target data is required.");

            RuleFor(x => x.Dto.FiscalYearId)
                .NotEmpty().WithMessage("Fiscal year is required.");

            RuleFor(x => x.Dto.Targets)
                .NotNull().WithMessage("At least one KPI target is required.")
                .Must(targets => targets.Count > 0).WithMessage("At least one KPI target is required.")
                .Must(targets => targets.Count <= 500).WithMessage("A maximum of 500 KPIs can be saved in one request.");
        }
    }
}
