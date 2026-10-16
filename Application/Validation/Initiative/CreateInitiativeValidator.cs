using Application.CQRS.Initiative.Requests.Commands;
using FluentValidation;

namespace Application.Validation.Initiative;

public class CreateInitiativeValidator : AbstractValidator<CreateInitiativeCommandRequest>
{
    public CreateInitiativeValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.OwnerId).NotEqual(Guid.Empty);
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.Status).IsInEnum();

        RuleFor(x => x.StartDate).NotNull().WithMessage("Start date is required.");
        RuleFor(x => x.EndDate).NotNull().WithMessage("End date is required.");

        // FR-404: budget figures may never be negative.
        RuleFor(x => x.Budget).GreaterThanOrEqualTo(0m)
            .WithMessage("Planned budget cannot be negative.");

        // FR-401: an end date cannot precede the start date.
        RuleFor(x => x)
            .Must(x => !x.StartDate.HasValue || !x.EndDate.HasValue || x.EndDate.Value.Date >= x.StartDate.Value.Date)
            .WithMessage("Initiative end date cannot be before start date.")
            .WithName(nameof(CreateInitiativeCommandRequest.EndDate));
    }
}
