using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface IRedemptionService
    {
        Task<GenericResponse2> LiquidationServiceRequest(string Session, string BankName, string RedemptionAccount, decimal Amount, string UserName, string UserType,string CustomerName);
    }
}
