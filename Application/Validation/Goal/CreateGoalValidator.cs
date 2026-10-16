using Application.CQRS.Goals.Requests.Commands;
using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Validation.Goal
{
    public class CreateGoalValidator : AbstractValidator<CreateGoalCommandRequest>
    {
        public CreateGoalValidator()
        {
            RuleFor(x => x.Dto.Name)
                .NotEmpty().WithMessage("Goal name is required.");
        }

    }
}
