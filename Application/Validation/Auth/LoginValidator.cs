using Application.CQRS.Auth.Requests.Commands;
using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Validation.Auth
{
    public class LoginValidator : AbstractValidator<LoginCommandRequest>
    {
        public LoginValidator()
        {
            RuleFor(x => x.Dto)
                .NotNull().WithMessage("Login data is required.");
            RuleFor(x => x.Dto)
                .ChildRules(dto => dto.RuleFor(d => d.Username).NotEmpty().WithMessage("Username is required."))
                .When(x => x.Dto is not null);
            RuleFor(x => x.Dto)
                .ChildRules(dto => dto.RuleFor(d => d.Password).NotEmpty().WithMessage("Password is required."))
                .When(x => x.Dto is not null);
        }

    }
}