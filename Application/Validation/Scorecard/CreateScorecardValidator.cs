using Application.CQRS.Scorecard.Requests.Commands;
using FluentValidation;

namespace Application.Validation.Scorecard;

public class CreateScorecardValidator : AbstractValidator<CreateScorecardCommandRequest>
{
    public CreateScorecardValidator()
    {
        RuleFor(x => x.FiscalYearId)
            .NotEmpty().WithMessage("Fiscal year is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Scorecard name is required.")
            .MaximumLength(200).WithMessage("Scorecard name must be 200 characters or fewer.");
    }
}
