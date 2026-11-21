using Application.CQRS.Division.Requests.Commands;
using FluentValidation;

namespace Application.Validation.Division;

public class EnterDivisionActualValueValidator : AbstractValidator<EnterDivisionActualValueCommandRequest>
{
    public EnterDivisionActualValueValidator()
    {
        RuleFor(x => x.Dto)
            .NotNull().WithMessage("Actual value data is required.");

        RuleFor(x => x.Dto.DivisionId)
            .NotEmpty().WithMessage("Division is required.");

        RuleFor(x => x.Dto.KpiId)
            .NotEmpty().WithMessage("KPI is required.");

        RuleFor(x => x.Dto.FiscalPeriodId)
            .NotEmpty().WithMessage("Fiscal period is required.");

        // DataSource is intentionally absent: the server always records Manual.

        RuleFor(x => x.Dto.Note)
            .MaximumLength(500).WithMessage("Note must be 500 characters or fewer.");

        RuleFor(x => x.Dto.OverrideReason)
            .MaximumLength(1000).WithMessage("Override reason must be 1000 characters or fewer.");
    }
}
