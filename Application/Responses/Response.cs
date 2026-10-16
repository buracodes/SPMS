using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Responses
{
    public class Response<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<string>? Errors { get; set; }
        public T? Data { get; set; }
    }
}
