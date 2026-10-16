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
    }
}
