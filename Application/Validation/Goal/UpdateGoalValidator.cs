using Application.CQRS.Goals.Requests.Commands;
using FluentValidation;

namespace Application.Validation.Goal;

public class UpdateGoalValidator : AbstractValidator<UpdateGoalCommandRequest>
{
    public UpdateGoalValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Goal ID is required.");

        RuleFor(x => x.Name)
            .MaximumLength(200).WithMessage("Goal name must be 200 characters or fewer.")
            .When(x => !string.IsNullOrWhiteSpace(x.Name));
    }
}
