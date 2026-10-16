using Application.CQRS.Initiative.Requests.Commands;
using FluentValidation;

namespace Application.Validation.Initiative;

/// <summary>FR-402 milestone update validation.</summary>
public class UpdateMilestoneValidator : AbstractValidator<UpdateMilestoneCommandRequest>
{
    public UpdateMilestoneValidator()
    {
        RuleFor(x => x.Id).NotEqual(Guid.Empty);
        RuleFor(x => x.InitiativeId).NotEqual(Guid.Empty);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).When(x => x.Name != null);
        RuleFor(x => x.Description).MaximumLength(2000).When(x => x.Description != null);
        RuleFor(x => x.Comments).MaximumLength(4000).When(x => x.Comments != null);
        RuleFor(x => x.DueDate).NotEmpty().When(x => x.DueDate.HasValue);

        RuleFor(x => x.Weight)
            .InclusiveBetween(0m, 100m)
            .When(x => x.Weight.HasValue)
            .WithMessage("Milestone weight must be between 0 and 100.");

        RuleFor(x => x.CompletionPercentage)
            .InclusiveBetween(0, 100)
            .When(x => x.CompletionPercentage.HasValue)
            .WithMessage("Milestone completion must be between 0% and 100%.");

        RuleFor(x => x.Status)
            .IsInEnum()
            .When(x => x.Status.HasValue);
    }
}
