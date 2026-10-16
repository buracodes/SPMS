using Application.CQRS.Auth.Requests.Commands;
using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Validation.Auth
{
    public class RefreshTokenValidator : AbstractValidator<RefreshTokenCommandRequest>
    {
        public RefreshTokenValidator()
        {
            RuleFor(x => x.Dto)
                .NotNull().WithMessage("Refresh token data is required.");
            RuleFor(x => x.Dto.RefreshToken)
                .NotEmpty().WithMessage("Refresh token is required.");
        }

    }
}