using Application.CQRS.KpiActuals.Requests.Commands;
using Domain.Enums;
using FluentValidation;

namespace Application.Validation.KpiActual
{
    public class EnterActualValueValidator : AbstractValidator<EnterActualValueCommandRequest>
    {
        public EnterActualValueValidator()
        {
            RuleFor(x => x.Dto)
                .NotNull().WithMessage("Actual value data is required.");

            RuleFor(x => x.Dto.KpiId)
                .NotEmpty().WithMessage("KPI is required.");

            RuleFor(x => x.Dto.FiscalPeriodId)
                .NotEmpty().WithMessage("Fiscal period is required.");

            RuleFor(x => x.Dto.DataSource)
                .IsInEnum().WithMessage("Invalid data source.")
                .NotEqual(KpiDataSource.Calculated).WithMessage("Calculated values cannot be entered manually.");

            RuleFor(x => x.Dto.Note)
                .MaximumLength(500).WithMessage("Note must be 500 characters or fewer.");

            RuleFor(x => x.Dto.OverrideReason)
                .MaximumLength(1000).WithMessage("Override reason must be 1000 characters or fewer.");
        }
    }
}
