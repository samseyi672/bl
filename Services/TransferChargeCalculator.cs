using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Retailbanking.BL.Services
{
    public class TransferChargeCalculator
    {
        public static decimal? CalculateCharge(decimal amount, string nipCharges)
        {
            string[] nipChargesArray = nipCharges.Split(';');
            foreach (string s in nipChargesArray)
            {
                string[] rangeAndCharge = s.Split(':');
                decimal charge = decimal.Parse(rangeAndCharge[1], CultureInfo.InvariantCulture);
                string[] priceRange = rangeAndCharge[0].Split('-');
                decimal lowerRange = decimal.Parse(priceRange[0], CultureInfo.InvariantCulture);
                decimal? upperRange;
                // Check if upper range is "$" indicating no upper limit
                if (priceRange[1] == "$")
                {
                    upperRange = null; // No upper limit
                }
                else
                {
                    upperRange = decimal.Parse(priceRange[1], CultureInfo.InvariantCulture);
                }
                if (amount > lowerRange && (upperRange == null || amount < upperRange))
                {
                    return charge;
                }
            }
            return null;
        }
    }
}
