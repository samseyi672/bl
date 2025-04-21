using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public  interface ISimplexPortfolioService
    {

        //URI: /portfolio/full-product-detail
        //Response-ApiResponseDto
        Task<GenericResponse2> GetFullProductDetails(string token,string xibsapisecret);//get 

        //URI:/portfolio-balance/:unique_ref
        //Response -ApiPortfolioBalanceResponse
        Task<GenericResponse2> GetPortfolioBalance(string token,string xibsapisecret, int unique_ref);//get 

        //URI: /portfolio/fixed-deposit / histories /:unique_ref
        // Response =ApiPortfolioHistoryResponse
        Task<GenericResponse2> GetfixedDepositPortfolioHistories(string token,string xibsapisecret, int Client_unique_ref, string startDate, string endDate, int skip, int pageSize);//get 

        //URI:/portfolio/mutual-fund/histories/:unique_ref
        //Response - PortfolioMutualFundResponse
        Task<GenericResponse2> GetPortfolioMutualFundHistory(string token,string xibsapisecret, int Client_unique_ref, int portfolioId, string startDate, string endDate, int skip, int pageSize); //get

        //URI: /portfolio/wallet/histories/:unique_ref
        //Response - PortfolioWalletHistoryResponse
        Task<GenericResponse2> GetPortfolioWalletHistory(string token,string xibsapisecret, int Client_unique_ref, int portfolioId, string startDate, string endDate, int skip, int pageSize); //get
        Task<GenericResponse2> FixeddepositSubscription(string token, string xibsapisecret, FixeddepositSubscription fixeddepositSubscription);
        Task<GenericResponse2> MutualFundSubscription(string token, string xibsapisecret, MutualFundSubscription mutualFundSubscription);
        Task<GenericResponse2> FundCashAccount(string token, string xibsapisecret, FundCashAccount fundCashAccount);
        Task<GenericResponse2> CustomerInvestmentSummary(string token, string xibsapisecret, int clientId);
    }
}
