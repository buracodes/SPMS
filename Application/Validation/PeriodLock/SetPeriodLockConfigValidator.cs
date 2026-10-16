using Application.CQRS.PeriodLock.Requests.Commands;
using FluentValidation;

namespace Application.Validation.PeriodLock
{
    public class SetPeriodLockConfigValidator : AbstractValidator<SetPeriodLockConfigCommandRequest>
    {
        public SetPeriodLockConfigValidator()
        {
            RuleFor(x => x.Dto)
                .NotNull().WithMessage("Period lock configuration is required.");

            RuleFor(x => x.Dto.ScorecardId)
                .NotEmpty().WithMessage("Scorecard is required.");

            RuleFor(x => x.Dto.FiscalPeriodId)
                .NotEmpty().WithMessage("Fiscal period is required.");

            When(x => x.Dto.EntryOpenDay.HasValue, () =>
            {
                RuleFor(x => x.Dto.EntryOpenDay)
                    .InclusiveBetween(1, 31).WithMessage("Entry open day must be between 1 and 31.");
            });

            When(x => x.Dto.CutoffDay.HasValue, () =>
            {
                RuleFor(x => x.Dto.CutoffDay)
                    .InclusiveBetween(1, 31).WithMessage("Cutoff day must be between 1 and 31.");
            });

            When(x => x.Dto.ReminderDaysBeforeCutoff.HasValue, () =>
            {
                RuleFor(x => x.Dto.ReminderDaysBeforeCutoff)
                    .InclusiveBetween(0, 30).WithMessage("Reminder days before cutoff must be between 0 and 30.");
            });

            RuleFor(x => x.Dto)
                .Must(d => !(d.Lock && d.Unlock))
                .WithMessage("A period cannot be locked and unlocked in the same request.");
        }
    }
}
