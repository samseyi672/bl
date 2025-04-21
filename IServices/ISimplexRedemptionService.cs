using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface ISimplexRedemptionService
    {
        // /mutual-fund/balance
        Task<GenericResponse2>  GetMutualFundBalance(string token,string xibsapisecret, MutualFundBalance MutualFundBalance);
        // /mutual-fund/redemption/details
        Task<GenericResponse2>  GetMutualFundRedemptionDetails(string token,string xibsapisecret, MutualFundRedemption MutualFundRedemption);
        // /mutual-fund/redemption/confirm
        Task<GenericResponse2> GetMutualFundRedemptionConfirm(string token,string xibsapisecret, MutualFundRedemptionConfirm MutualFundRedemptionConfirm);
    }
}


































































































