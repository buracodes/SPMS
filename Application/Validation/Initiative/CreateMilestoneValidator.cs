using Application.CQRS.Initiative.Requests.Commands;
using FluentValidation;

namespace Application.Validation.Initiative;

/// <summary>FR-402 milestone creation: bounded weight and completion, valid due date.</summary>
public class CreateMilestoneValidator : AbstractValidator<CreateMilestoneCommandRequest>
{
    public CreateMilestoneValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.InitiativeId).NotEqual(Guid.Empty);
        RuleFor(x => x.DueDate).NotEmpty().WithMessage("A milestone due date is required.");

        RuleFor(x => x.Weight)
            .InclusiveBetween(0m, 100m)
            .WithMessage("Milestone weight must be between 0 and 100.");

        RuleFor(x => x.CompletionPercentage)
            .InclusiveBetween(0, 100)
            .WithMessage("Milestone completion must be between 0% and 100%.");

        // Milestone delivery state is derived from completion, but a supplied value must still be one
        // of the three FRD states — otherwise an out-of-range value would be silently discarded.
        RuleFor(x => x.Status)
            .IsInEnum()
            .When(x => x.Status.HasValue)
            .WithMessage("Milestone status must be Not Started, In Progress or Completed.");
    }
}
