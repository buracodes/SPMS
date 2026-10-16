using Application.CQRS.PeriodLock.Requests.Queries;
using FluentValidation;

namespace Application.Validation.PeriodLock
{
    public class GetPeriodLockStatusQueryValidator : AbstractValidator<GetPeriodLockStatusQueryRequest>
    {
        public GetPeriodLockStatusQueryValidator()
        {
            RuleFor(x => x.ScorecardId)
                .NotEmpty().WithMessage("Scorecard is required.");

            RuleFor(x => x.FiscalYearId)
                .NotEmpty().WithMessage("Fiscal year is required.");
        }
    }
}