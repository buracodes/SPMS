using Application.CQRS.Auth.Requests.Commands;
using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Validation.Auth
{
    public class ResetDivisionPasswordValidator : AbstractValidator<ResetDivisionPasswordCommandRequest>
    {
        public ResetDivisionPasswordValidator()
        {
            RuleFor(x => x.Dto)
                .NotNull().WithMessage("Password reset data is required.");

            RuleFor(x => x.Dto)
                .ChildRules(dto => dto.RuleFor(d => d.DivisionId).NotEmpty().WithMessage("Division is required."))
                .When(x => x.Dto is not null);

            RuleFor(x => x.Dto)
                .ChildRules(dto => dto.RuleFor(d => d.NewPassword).SatisfyPasswordPolicy(d => d.NewPassword))
                .When(x => x.Dto is not null);

            RuleFor(x => x.Dto)
                .ChildRules(dto => dto.RuleFor(d => d.ConfirmPassword).NotEmpty().WithMessage("Password confirmation is required."))
                .When(x => x.Dto is not null);

            RuleFor(x => x.Dto)
                .Must(d => d == null || d.NewPassword == d.ConfirmPassword)
                .WithMessage("New password and confirmation do not match.")
                .When(x => x.Dto is not null);
        }
    }
}
