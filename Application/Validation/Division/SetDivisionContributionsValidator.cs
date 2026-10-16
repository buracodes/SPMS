using Application.CQRS.Division.Requests.Commands;
using FluentValidation;

namespace Application.Validation.Division
{
    public class SetDivisionContributionsValidator : AbstractValidator<SetDivisionContributionsCommandRequest>
    {
        public SetDivisionContributionsValidator()
        {
            RuleFor(x => x.DivisionId).NotEmpty().WithMessage("Division is required.");
            RuleFor(x => x.ScorecardId).NotEmpty().WithMessage("Scorecard is required.");
            RuleFor(x => x.GoalIds).Must(x => x.Distinct().Count() == x.Count)
                .WithMessage("Goal selections must not contain duplicates.");
        }
    }
}
