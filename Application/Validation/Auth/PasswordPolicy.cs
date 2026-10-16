using FluentValidation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Application.Validation.Auth
{
    public static class PasswordPolicy
    {
        public const int MinimumLength = 8;

        public static readonly string RequirementMessage =
            $"Password must be at least {MinimumLength} characters long and include an uppercase letter, a lowercase letter, a digit and a special character.";

        public static bool IsSatisfiedBy(string? password)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < MinimumLength)
            {
                return false;
            }

            return password.Any(char.IsUpper)
                && password.Any(char.IsLower)
                && password.Any(char.IsDigit)
                && password.Any(c => !char.IsLetterOrDigit(c));
        }
    }

    public static class PasswordPolicyRuleExtensions
    {
        public static IRuleBuilderOptions<T, string> SatisfyPasswordPolicy<T>(
            this IRuleBuilder<T, string> ruleBuilder,
            Func<T, string?> selector)
        {
            return ruleBuilder
                .Must(p => PasswordPolicy.IsSatisfiedBy(p))
                .WithMessage(PasswordPolicy.RequirementMessage);
        }
    }
}
