using Application.CQRS.RagThreshold.Requests.Commands;
using FluentValidation;

namespace Application.Validation.RagThreshold
{
    public class UpdateRagThresholdsValidator : AbstractValidator<UpdateRagThresholdsCommandRequest>
    {
        public UpdateRagThresholdsValidator()
        {
            RuleFor(x => x.Dto)
                .NotNull().WithMessage("RAG threshold configuration is required.");

            RuleFor(x => x.Dto.Green)
                .InclusiveBetween(0, 100).WithMessage("Green threshold must be between 0 and 100.");

            RuleFor(x => x.Dto.Yellow)
                .InclusiveBetween(0, 100).WithMessage("Yellow threshold must be between 0 and 100.");

            RuleFor(x => x.Dto)
                .Must(d => d.Yellow < d.Green)
                .WithMessage("Yellow threshold must be strictly lower than the Green threshold.");
        }
    }
}
