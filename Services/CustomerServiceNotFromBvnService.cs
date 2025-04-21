using Dapper;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.Services
{
    public class CustomerServiceNotFromBvnService
    {
        public async static Task<CustomerDataNotFromBvn> getCustomerRelationService(IDbConnection con,int userid)
        {
            CustomerDataNotFromBvn customerDataNotFromBvn = (await con.QueryAsync<CustomerDataNotFromBvn>("select * from customerdatanotfrombvn where userid=@userid", new { userid = userid })).FirstOrDefault();
           return customerDataNotFromBvn;
        }

        public async static Task<AssetCapitalInsuranceCustomerDataNotFromBvn> getAssetCapitalInsuranceCustomerRelationService(IDbConnection con, int userid,string UserType)
        {
            AssetCapitalInsuranceCustomerDataNotFromBvn customerDataNotFromBvn = (await con.QueryAsync<AssetCapitalInsuranceCustomerDataNotFromBvn>("select * from asset_capital_insurance_custdatanotfrombvn where user_id=@userid and user_type=@UserType", new { userid = userid,UserType= UserType })).FirstOrDefault();
            return customerDataNotFromBvn;
        }

        public static string ReplaceFirstDigit(string number, string newPrefix)
        {
            if (string.IsNullOrEmpty(number) || string.IsNullOrEmpty(newPrefix))
            {
                throw new ArgumentException("Input number and new prefix cannot be null or empty.");
            }
            // Replace the first character with the new prefix
            return newPrefix + number.Substring(1);
        }
    }
}
