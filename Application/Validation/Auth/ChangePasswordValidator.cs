using Application.CQRS.Auth.Requests.Commands;
using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Validation.Auth
{
    public class ChangePasswordValidator : AbstractValidator<ChangePasswordCommandRequest>
    {
        public ChangePasswordValidator()
        {
            RuleFor(x => x.Dto)
                .NotNull().WithMessage("Password change data is required.");

            RuleFor(x => x.Dto)
                .ChildRules(dto => dto.RuleFor(d => d.CurrentPassword).NotEmpty().WithMessage("Current password is required."))
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
