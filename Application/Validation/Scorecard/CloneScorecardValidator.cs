using Application.CQRS.Scorecard.Requests.Commands;
using FluentValidation;

namespace Application.Validation.Scorecard
{
    public class CloneScorecardValidator : AbstractValidator<CloneScorecardCommandRequest>
    {
        public CloneScorecardValidator()
        {
            RuleFor(x => x.SourceScorecardId)
                .NotEmpty().WithMessage("Source scorecard is required.");

            RuleFor(x => x.TargetFiscalYearId)
                .NotEmpty().WithMessage("Target fiscal year is required.");

            RuleFor(x => x.NewName)
                .MaximumLength(200).WithMessage("Scorecard name must be 200 characters or fewer.")
                .When(x => !string.IsNullOrWhiteSpace(x.NewName));
        }
    }
}
