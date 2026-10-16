using Application.CQRS.Division.Requests.Commands;
using FluentValidation;

namespace Application.Validation.Division
{
    public class CreateDivisionValidator : AbstractValidator<CreateDivisionCommandRequest>
    {
        public CreateDivisionValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Division name is required.")
                .MaximumLength(200).WithMessage("Division name must be 200 characters or fewer.");

            RuleFor(x => x.Code)
                .NotEmpty().WithMessage("Division code is required.")
                .MaximumLength(50).WithMessage("Division code must be 50 characters or fewer.");
        }
    }
}
