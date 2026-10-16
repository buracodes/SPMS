using Microsoft.IdentityModel.Tokens.Experimental;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Exceptions
{
    public class ValidationExceptions : ApplicationException
    {
        public IReadOnlyList<ValidationError> Errors { get; }
        public ValidationExceptions(IEnumerable<ValidationError> errors) : base("One or more validation errors occurred.")
        {
            Errors = errors.ToList().AsReadOnly();
        }
    }
    public class ValidationError
    {
        public string PropertyName { get; set; } = string.Empty;
        public string Field { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
