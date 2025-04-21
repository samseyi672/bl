using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Retailbanking.BL.IServices;
using Retailbanking.BL.utils;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using RestSharp;
using Microsoft.Extensions.Options;
using iText.Kernel.Geom;

namespace Retailbanking.BL.Services
{
    public class SimplexPortfolioService : ISimplexPortfolioService
    {
        private readonly ILogger<SimplexCustomerService> _logger;
        private readonly SimplexConfig _settings;
        private readonly IGeneric _genServ;

        public SimplexPortfolioService(ILogger<SimplexCustomerService> logger, IOptions<SimplexConfig> options, IGeneric genServ)
        {
            _logger = logger;
            _settings = options.Value;
            _genServ = genServ;
        }

        public async Task<string> baseApiFunction(string token, string xibsapisecret, string uri, object requestobject,string method)
        {
            var header = new Dictionary<string, string>();
            xibsapisecret = SimplexKeyComputation.ComputeApiKey(_settings.APIKey, _settings.APISecret);
            header.TryAdd("x-ibs-api-secret", xibsapisecret);
            header.TryAdd("Authorization", "Bearer " + token);
            _logger.LogInformation("full url "+ _settings.baseurl + uri);
            string response = await _genServ.CallServiceAsyncToString(string.IsNullOrEmpty(method)? Method.GET : Method.POST, _settings.baseurl + uri, requestobject, true, header);
            _logger.LogInformation("api response " + response);
            return response;
        }

        public async Task<GenericResponse2> GetFullProductDetails(string token, string xibsapisecret)
        {
            string response = await baseApiFunction(token, xibsapisecret, "portfolio/full-product-detail",null,null);
            _logger.LogInformation("GetFullProductDetails response " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var ApiResponseDto = JsonConvert.DeserializeObject<ApiResponseDto>(response);
                return new GenericResponse2()
                {
                    data = ApiResponseDto,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> GetPortfolioBalance(string token,string xibsapisecret, int unique_ref)
        {
            var header = new Dictionary<string, string>();
            xibsapisecret = SimplexKeyComputation.ComputeApiKey(_settings.APIKey, _settings.APISecret);
            header.TryAdd("x-ibs-api-secret", xibsapisecret);
            header.TryAdd("Authorization", "Bearer " + token);
            //  string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.baseurl + "client/register", extendedSimplexCustomerRegistration, true, header);
            string response = await _genServ.CallServiceAsyncToString(Method.GET, _settings.baseurl + "portfolio-balance/"+unique_ref,null, true, header);
            _logger.LogInformation("ApiPortfolioBalanceResponse response " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var ApiPortfolioBalanceResponse = JsonConvert.DeserializeObject<ApiPortfolioBalanceResponse>(response);
                return new GenericResponse2()
                {
                    data = ApiPortfolioBalanceResponse,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> GetfixedDepositPortfolioHistories(string token,string xibsapisecret, int Client_unique_ref, string startDate, string endDate, int skip, int pageSize)
        {
            string response = await baseApiFunction(token, xibsapisecret, "portfolio/fixed-deposit/histories/"+Client_unique_ref+$"?startDate={startDate}&endDate={endDate}&skip={skip}&pageSize={pageSize}",null,null);
            _logger.LogInformation("GetfixedDepositPortfolioHistories response " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var ApiPortfolioHistoryResponse = JsonConvert.DeserializeObject<ApiPortfolioHistoryResponse>(response);
                return new GenericResponse2()
                {
                    data = ApiPortfolioHistoryResponse,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> GetPortfolioMutualFundHistory(string token,string xibsapisecret, int Client_unique_ref, int portfolioId, string startDate, string endDate, int skip, int pageSize)
        {
            string response = await baseApiFunction(token, xibsapisecret, "portfolio/mutual-fund/histories/" + Client_unique_ref + $"?portfolioId={portfolioId}&startDate={startDate}&endDate={endDate}&skip={skip}&pageSize={pageSize}", null,null);
            _logger.LogInformation("PortfolioMutualFundResponse response " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var PortfolioMutualFundResponse = JsonConvert.DeserializeObject<PortfolioMutualFundResponse>(response);
                return new GenericResponse2()
                {
                    data = PortfolioMutualFundResponse,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> GetPortfolioWalletHistory(string token,string xibsapisecret, int Client_unique_ref, int portfolioId, string startDate, string endDate, int skip, int pageSize)
        {
            string response = await baseApiFunction(token, xibsapisecret, "portfolio/wallet/histories/" + Client_unique_ref + $"?startDate={startDate}&endDate={endDate}&skip={skip}&pageSize={pageSize}", null,null);
            _logger.LogInformation("PortfolioMutualFundResponse response " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var SimplexWalletHistory = JsonConvert.DeserializeObject<SimplexWalletHistory>(response);
                return new GenericResponse2()
                {
                    data = SimplexWalletHistory,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> FixeddepositSubscription(string token, string xibsapisecret, FixeddepositSubscription fixeddepositSubscription)
        {
            string response = await baseApiFunction(token, xibsapisecret, "fixed-deposit/subscription",fixeddepositSubscription,"post");
            _logger.LogInformation("fixeddepositSubscriptionResponse " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var fixeddepositSubscriptionResponse = JsonConvert.DeserializeObject<FixeddepositSubscriptionResponse>(response);
                return new GenericResponse2()
                {
                    data = fixeddepositSubscriptionResponse,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> MutualFundSubscription(string token, string xibsapisecret, MutualFundSubscription mutualFundSubscription)
        {
            if(mutualFundSubscription.paymentChannel!="wallet")
            {
                mutualFundSubscription.paymentChannel = null;
            }
            string response = await baseApiFunction(token, xibsapisecret, "mutual-fund/subscription", mutualFundSubscription,"post");
            _logger.LogInformation("fixeddepositSubscriptionResponse " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var mutualfundsubscriptionresponse = JsonConvert.DeserializeObject<MutualFundRespponse>(response);
                return new GenericResponse2()
                {
                    data = mutualfundsubscriptionresponse,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> FundCashAccount(string token, string xibsapisecret, FundCashAccount fundCashAccount)
        {
            string response = await baseApiFunction(token, xibsapisecret, "wallet/fund", fundCashAccount, "post");
            _logger.LogInformation("FundCashAccount " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var SimplexGenericResponse = JsonConvert.DeserializeObject<SimplexGenericResponse>(response);
                return new GenericResponse2()
                {
                    data = SimplexGenericResponse,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> CustomerInvestmentSummary(string token, string xibsapisecret, int clientId)
        {
            string response = await baseApiFunction(token, xibsapisecret, "investment-summary/" +clientId, null, null);
            _logger.LogInformation("PortfolioMutualFundResponse response " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var InvestmentSummaryResponse = JsonConvert.DeserializeObject<InvestmentSummaryResponse>(response);
                return new GenericResponse2()
                {
                    data = InvestmentSummaryResponse,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }
    }
}










































































