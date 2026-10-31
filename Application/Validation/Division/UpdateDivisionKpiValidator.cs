using Application.CQRS.Division.Requests.Commands;
using FluentValidation;

namespace Application.Validation.Division
{
    public class UpdateDivisionKpiValidator : AbstractValidator<UpdateDivisionKpiCommandRequest>
    {
        public UpdateDivisionKpiValidator()
        {
            RuleFor(x => x.DivisionKpiId).NotEmpty().WithMessage("Division KPI is required.");

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("KPI name is required.")
                .MaximumLength(200).WithMessage("KPI name must be 200 characters or fewer.")
                .When(x => x.Name != null);

            RuleFor(x => x.Description)
                .MaximumLength(1000).WithMessage("Description must be 1000 characters or fewer.")
                .When(x => x.Description != null);

            RuleFor(x => x.Unit)
                .MaximumLength(50).WithMessage("Unit must be 50 characters or fewer.")
                .When(x => x.Unit != null);

            RuleFor(x => x.Formula)
                .MaximumLength(500).WithMessage("Formula must be 500 characters or fewer.")
                .When(x => x.Formula != null);

            RuleFor(x => x.Weight)
                .GreaterThan(0).WithMessage("Weight must be greater than 0.")
                .LessThanOrEqualTo(100).WithMessage("Weight cannot exceed 100%.")
                .When(x => x.Weight.HasValue);

            RuleFor(x => x.ToleranceBandPercent)
                .GreaterThanOrEqualTo(0).WithMessage("Tolerance band cannot be negative.")
                .LessThanOrEqualTo(100).WithMessage("Tolerance band cannot exceed 100%.")
                .When(x => x.ToleranceBandPercent.HasValue);
        }
    }
}
