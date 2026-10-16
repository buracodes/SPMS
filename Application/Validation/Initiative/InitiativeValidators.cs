using Application.CQRS.Initiative.Requests.Commands;
using FluentValidation;

namespace Application.Validation.Initiative;

/// <summary>FR-403 progress update validation.</summary>
public class UpdateMilestoneProgressValidator : AbstractValidator<UpdateMilestoneProgressCommandRequest>
{
    public UpdateMilestoneProgressValidator()
    {
        RuleFor(x => x.Dto).NotNull();

        When(x => x.Dto != null, () =>
        {
            RuleFor(x => x.Dto.InitiativeId).NotEqual(Guid.Empty);
            RuleFor(x => x.Dto.MilestoneId).NotEqual(Guid.Empty);

            RuleFor(x => x.Dto.CompletionPercentage)
                .InclusiveBetween(0, 100)
                .WithMessage("Milestone completion must be between 0% and 100%.");

            RuleFor(x => x.Dto.Status)
                .IsInEnum()
                .When(x => x.Dto.Status.HasValue);

            RuleFor(x => x.Dto.Comments)
                .MaximumLength(4000)
                .WithMessage("Progress comments cannot exceed 4000 characters.");
        });
    }
}

/// <summary>FR-405 closure validation.</summary>
public class CloseInitiativeValidator : AbstractValidator<CloseInitiativeCommandRequest>
{
    public CloseInitiativeValidator()
    {
        RuleFor(x => x.Dto).NotNull();

        When(x => x.Dto != null, () =>
        {
            RuleFor(x => x.Dto.InitiativeId).NotEqual(Guid.Empty);

            RuleFor(x => x.Dto.ActualEndDate)
                .NotEqual(default(DateTime))
                .WithMessage("Actual End Date is required.");

            // FR-405: an initiative can only be closed with an actual end date that is already
            // reached or today itself; future-dating the closure is rejected up front.
            RuleFor(x => x.Dto.ActualEndDate)
                .Must(d => d.Date <= DateTime.Today)
                .WithMessage("Actual End Date must be less than or equal to today's date.");

            // FR-405: the outcome summary is optional (bounded so the closure record stays readable).
            RuleFor(x => x.Dto.OutcomeSummary).MaximumLength(4000);

            RuleFor(x => x.Dto.LessonsLearned).MaximumLength(4000);

            // FR-404: final spend may never be negative.
            RuleFor(x => x.Dto.FinalActualExpenditure)
                .GreaterThanOrEqualTo(0m)
                .When(x => x.Dto.FinalActualExpenditure.HasValue)
                .WithMessage("Actual expenditure cannot be negative.");
        });
    }
}

/// <summary>Dependency link validation. Cycle detection needs the graph and lives in the handler.</summary>
public class AddInitiativeDependencyValidator : AbstractValidator<AddInitiativeDependencyCommandRequest>
{
    public AddInitiativeDependencyValidator()
    {
        RuleFor(x => x.InitiativeId).NotEqual(Guid.Empty);
        RuleFor(x => x.DependsOnInitiativeId).NotEqual(Guid.Empty);

        RuleFor(x => x)
            .Must(x => x.InitiativeId != x.DependsOnInitiativeId)
            .WithMessage("An initiative cannot depend on itself.");
    }
}
