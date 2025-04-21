using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface IMobileInvestment
    {
        Task<GenericResponse> GetActiveFixedDepositOrHalalaInvestment(string userId);
      //  Task<GenericResponse> GetAUserLiquidatedFixedDepositInvestment(string userId);
      //  Task<GenericResponse> GetAUserUnderProcessingFixedDepositInvestment(string userId);
      //  Task<GenericResponse> GetAUserActiveHalalInvestment(string userId);
       // Task<GenericResponse> GetAUserLiquidatedHalalInvestment(string userId);
       // Task<GenericResponse> GetAUserUnderProcessingHalalInvestment(string userId);
        Task<GenericResponse> GetMutualFundInvestment(string UserName);
        Task<GenericResponse> GetRetailLoan(string userId);
        Task<GenericResponse> GetAllFixedDepositHistory(string username, string investmenttype);
        Task<GenericResponse> GetPublicSectorLoan(string userName);
    }
}
























































































