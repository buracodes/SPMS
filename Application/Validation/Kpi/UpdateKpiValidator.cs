using Application.CQRS.Kpi.Requests.Commands;
using FluentValidation;

namespace Application.Validation.Kpi;

public class UpdateKpiValidator : AbstractValidator<UpdateKpiCommandRequest>
{
    public UpdateKpiValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("KPI ID is required.");

        RuleFor(x => x.Name)
            .MaximumLength(200).WithMessage("KPI name must be 200 characters or fewer.")
            .When(x => !string.IsNullOrWhiteSpace(x.Name));

        RuleFor(x => x.Weight)
            .GreaterThan(0m).WithMessage("KPI weight must be greater than 0%.")
            .LessThanOrEqualTo(100m).WithMessage("KPI weight cannot exceed 100%.")
            .When(x => x.Weight.HasValue);

        RuleFor(x => x)
            .Must(x => !(x.OwnerId.HasValue && x.DivisionOwnerId.HasValue))
            .WithMessage("A KPI can be owned by a user or a division, not both.");
    }
}
