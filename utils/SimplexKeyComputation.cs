using iText.Commons.Actions;
using System;
using System.Collections.Generic;
using System.Text;

namespace Retailbanking.BL.utils
{
    public  class SimplexKeyComputation
    {
        public SimplexKeyComputation() { }

        public static string ComputeApiKey(string APIKey,string APISecret)
        {
            string concatValue = APIKey + ":" + APISecret;
            byte[] bytes = ASCIIEncoding.ASCII.GetBytes(concatValue);
            string computedVaue = Convert.ToBase64String(bytes);
            return computedVaue;
        }
    }
}
