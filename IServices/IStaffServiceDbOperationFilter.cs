using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public  interface IStaffServiceDbOperationFilter
    {
        Task<List<string>> GetAuthorizerEmailsAsync(CustomerDataAtInitiationAndApproval customerDataAtInitiationAndApproval);
        Task<List<string>> GetAuthorizerEmailsAsync(List<string> ListOfEmail);
    }
}
