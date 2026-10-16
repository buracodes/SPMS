using Application.CQRS.Initiative.Requests.Commands;
using FluentValidation;

namespace Application.Validation.Initiative;

public class UpdateInitiativeValidator : AbstractValidator<UpdateInitiativeCommandRequest>
{
    public UpdateInitiativeValidator()
    {
        RuleFor(x => x.Id).NotEqual(Guid.Empty);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).When(x => x.Name != null);
        RuleFor(x => x.OwnerId).NotEqual(Guid.Empty).When(x => x.OwnerId.HasValue);
        RuleFor(x => x.Priority).IsInEnum().When(x => x.Priority.HasValue);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);

        // FR-404: budget figures may never be negative.
        RuleFor(x => x.Budget).GreaterThanOrEqualTo(0m)
            .When(x => x.Budget.HasValue)
            .WithMessage("Planned budget cannot be negative.");

        RuleFor(x => x.ActualExpenditure).GreaterThanOrEqualTo(0m)
            .When(x => x.ActualExpenditure.HasValue)
            .WithMessage("Actual expenditure cannot be negative.");

        // FR-401: dates are validated against the merged (current + supplied) values by the
        // handler, because a partial update only carries the fields being changed.
        RuleFor(x => x)
            .Must(x => !x.StartDate.HasValue || !x.EndDate.HasValue || x.EndDate.Value.Date >= x.StartDate.Value.Date)
            .WithMessage("Initiative end date cannot be before start date.")
            .WithName(nameof(UpdateInitiativeCommandRequest.EndDate));
    }
}
