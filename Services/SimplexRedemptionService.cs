using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using RestSharp;
using Retailbanking.BL.IServices;
using Retailbanking.BL.utils;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.Services
{
    public class SimplexRedemptionService : ISimplexRedemptionService
    {

        private readonly ILogger<SimplexRedemptionService> _logger;
        private readonly SimplexConfig _settings;
        private readonly IGeneric _genServ;

        public SimplexRedemptionService(ILogger<SimplexRedemptionService> logger, IOptions<SimplexConfig> options, IGeneric genServ)
        {
            _logger = logger;
            _settings = options.Value;
            _genServ = genServ;
        }

        public async Task<string> baseApiFunction(string token, string xibsapisecret, string uri, object requestobject, string method = null)
        {
            var header = new Dictionary<string, string>();
            xibsapisecret = SimplexKeyComputation.ComputeApiKey(_settings.APIKey, _settings.APISecret);
            header.TryAdd("x-ibs-api-secret", xibsapisecret);
            header.TryAdd("Authorization", "Bearer " + token);
            string response = await _genServ.CallServiceAsyncToString(method == null ? Method.POST : Method.GET, _settings.baseurl + uri, requestobject, true, header);
            _logger.LogInformation("token response " + response);
            return response;
        }

        public async Task<GenericResponse2> GetMutualFundBalance(string token, string xibsapisecret, MutualFundBalance MutualFundBalance)
        {
            string response = await baseApiFunction(token, xibsapisecret, "mutual-fund/balance",MutualFundBalance);
            _logger.LogInformation("GetMutualFundBalance " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var SimplexMutualFundBalance = JsonConvert.DeserializeObject<SimplexMutualFundBalance>(response);
                return new GenericResponse2()
                {
                    data = SimplexMutualFundBalance,
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

        public async Task<GenericResponse2> GetMutualFundRedemptionConfirm(string token, string xibsapisecret, MutualFundRedemptionConfirm MutualFundRedemptionConfirm)
        {
            string response = await baseApiFunction(token, xibsapisecret, "mutual-fund/redemption/confirm", MutualFundRedemptionConfirm);
            _logger.LogInformation("SimplexMutualFundRedemptionConfirmResponse " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var SimplexMutualFundRedemptionConfirmResponse = JsonConvert.DeserializeObject<SimplexMutualFundRedemptionConfirmResponse>(response);
                return new GenericResponse2()
                {
                    data = SimplexMutualFundRedemptionConfirmResponse,
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

        public async Task<GenericResponse2> GetMutualFundRedemptionDetails(string token, string xibsapisecret, MutualFundRedemption MutualFundRedemption)
        {
            string response = await baseApiFunction(token, xibsapisecret, "mutual-fund/redemption/details", MutualFundRedemption);
            _logger.LogInformation("SimplexMutualFundRedemptionConfirmResponse " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var MutualFundRedemptionResponse = JsonConvert.DeserializeObject<MutualFundRedemptionResponse>(response);
                return new GenericResponse2()
                {
                    data = MutualFundRedemptionResponse,
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































































