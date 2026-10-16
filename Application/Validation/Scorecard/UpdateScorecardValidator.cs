using Application.CQRS.Scorecard.Requests.Commands;
using FluentValidation;

namespace Application.Validation.Scorecard;

public class UpdateScorecardValidator : AbstractValidator<UpdateScorecardCommandRequest>
{
    public UpdateScorecardValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Scorecard ID is required.");

        RuleFor(x => x.Name)
            .MaximumLength(200).WithMessage("Scorecard name must be 200 characters or fewer.")
            .When(x => !string.IsNullOrWhiteSpace(x.Name));
    }
}
