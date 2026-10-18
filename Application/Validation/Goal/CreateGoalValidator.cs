using Application.CQRS.Goals.Requests.Commands;
using FluentValidation;
using System;

namespace Application.Validation.Goal
{
    public class CreateGoalValidator : AbstractValidator<CreateGoalCommandRequest>
    {
        public CreateGoalValidator()
        {
            RuleFor(x => x.Dto)
                .NotNull().WithMessage("Goal data is required.");

            RuleFor(x => x.Dto.ScorecardId)
                .NotEmpty().WithMessage("Scorecard is required.")
                .When(x => x.Dto != null);

            RuleFor(x => x.Dto.Name)
                .NotEmpty().WithMessage("Goal name is required.")
                .When(x => x.Dto != null);

            RuleFor(x => x.Dto.Name)
                .MaximumLength(200).WithMessage("Goal name must be 200 characters or fewer.")
                .When(x => !string.IsNullOrWhiteSpace(x.Dto?.Name));

            // FR-302: a goal weight is a percentage - strictly greater than 0 and at most 100.
            // The scorecard TOTAL cap (sum <= 100) is enforced in the handler under a
            // transaction, because the validator cannot see the database.
            RuleFor(x => x.Dto.Weight)
                .GreaterThan(0m).WithMessage("Goal weight must be greater than 0.")
                .LessThanOrEqualTo(100m).WithMessage("Goal weight must be 100 or less.")
                .When(x => x.Dto != null);
        }

    }
}
