using System;
using System.Collections.Generic;
using System.Text;

namespace Retailbanking.BL.utils
{
    public class TokenGenericResponse<T>
    {
        public bool Success { get; set; }
        public int Response { get; set; }
        public string ResponseMessage { get; set; }
        public T Data { get; set; }
        public string Message { get; set; }
    }
}
