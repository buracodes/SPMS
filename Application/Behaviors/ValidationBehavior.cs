using Application.Exceptions;
using FluentValidation;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Behaviors
{
    public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
    {
        private readonly IEnumerable<IValidator<TRequest>> validators;
        public ValidationBehavior(IEnumerable<IValidator<TRequest>> _validators)
        {
            validators = _validators;
        }
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            if (validators.Any())
            {
                var context = new ValidationContext<TRequest>(request);
                var validationResults = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, cancellationToken)));

                var failures = validationResults.SelectMany(r => r.Errors)
                     .Where(f => f != null)
                     .Select(f => new Exceptions.ValidationError
                     {
                         PropertyName = f.PropertyName,
                         Field = NormalizeFieldKey(f.PropertyName),
                         ErrorMessage = f.ErrorMessage
                     }).ToList();

                if (failures.Count != 0)
                {
                    throw new ValidationExceptions(failures);
                }
            }
            return await next();
        }
        private static string NormalizeFieldKey(string propertyName)
        {
            var last = propertyName.Split('.').Last();
            return last.ToLowerInvariant();
        }
    }

}
