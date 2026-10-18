using Application.CQRS.Kra.Requests.Commands;
using FluentValidation;

namespace Application.Validation.Kra;

public class CreateKraValidator : AbstractValidator<CreateKraCommandRequest>
{
    public CreateKraValidator()
    {
        RuleFor(x => x.GoalId)
            .NotEmpty().WithMessage("Goal is required.");

        RuleFor(x => x.ScorecardId)
            .NotEmpty().WithMessage("Scorecard is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("KRA name is required.")
            .MaximumLength(200).WithMessage("KRA name must be 200 characters or fewer.");

        // FR-302: a KRA weight is a percentage - strictly greater than 0 and at most 100.
        // The goal TOTAL cap (sum <= 100) is enforced in the handler under a transaction,
        // because the validator cannot see the database.
        RuleFor(x => x.Weight)
            .GreaterThan(0m).WithMessage("KRA weight must be greater than 0.")
            .LessThanOrEqualTo(100m).WithMessage("KRA weight must be 100 or less.");
    }
}
