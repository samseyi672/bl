using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface IPortfolioService
    {
        Task<GenericResponse2> FixeddepositSubscription(string userName, string session, string userType, FixeddepositSubscriptionDto fixeddepositSubscription);
        Task<GenericResponse2> FundCashAccount(FundCashAccountDto fundCashAccount, string PaymentReference);
        Task<GenericResponse2> FundCashAccount(FundCashAccountDto fundCashAccount);
        Task<GenericResponse2> GetfixedDepositPortfolioHistories(int portfolioId, string userName, string session, string userType, string startDate, string endDate, int skip, int pageSize);
        Task<GenericResponse2> GetFullProductDetails(string session, string userType);
        Task<GenericResponse2> GetPortfolioBalance(string userName, string session, string userType);
        Task<GenericResponse2> GetPortfolioMutualFundHistory(string userName, string session, string userType, int portfolioId, string startDate, string endDate, int skip, int pageSize);
        Task<GenericResponse2> GetPortfolioWalletHistory(string userName, string session, string userType, int portfolioId, string startDate, string endDate, int skip, int pageSize);
        Task<GenericResponse2> MutualFundSubscription(string userName, string session, string userType, MutualFundSubscriptionDto mutualFundSubscription);
        Task<GenericResponse2> CustomerInvestmentSummary(string userName, string session, string userType);
    }
}
