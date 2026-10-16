using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Responses
{
    public class ValidationErrorResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public Dictionary<string, string[]>? Errors { get; set; }
    }
}
