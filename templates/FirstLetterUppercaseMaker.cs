using System;
using System.Collections.Generic;
using System.Text;

namespace Retailbanking.BL.templates
{
    public class FirstLetterUppercaseMaker
    {
        public static string CapitalizeFirstLetter(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input; 
            }
            return char.ToUpper(input[0]) + input.Substring(1);
        }
    }
}
