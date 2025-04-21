using iText.Kernel.Geom;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlX.XDevAPI;
using Newtonsoft.Json;
using RestSharp;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using Retailbanking.Common.DbObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.Services
{
    public class PortfolioService : IPortfolioService
    {
        private readonly ILogger<IGenericAssetCapitalInsuranceCustomerService> _logger;
        private readonly AssetSimplexConfig _settings;
        private readonly DapperContext _context;
        private readonly IFileService _fileService;
        private readonly ISmsBLService _smsBLService;
        private readonly IUserCacheService _userCacheService;
        private readonly IRedisStorageService _redisStorageService;
        private readonly IGeneric _genServ;
        private readonly IMemoryCache _cache;
        private readonly AppSettings _appSettings;
        private readonly SimplexConfig _simplexSettings;
        private readonly IRegistration _registrationService;
        private readonly TemplateService _templateService;
        private readonly IGenericAssetCapitalInsuranceCustomerService _genericAssetCapitalInsuranceCustomerService;

        public PortfolioService(IGenericAssetCapitalInsuranceCustomerService genericAssetCapitalInsuranceCustomerService, TemplateService templateService, IRegistration registration, ILogger<IGenericAssetCapitalInsuranceCustomerService> logger, IOptions<AppSettings> appSettings, IOptions<SimplexConfig> _setting2, IOptions<AssetSimplexConfig> settings, DapperContext context, IFileService fileService, ISmsBLService smsBLService, IUserCacheService userCacheService, IRedisStorageService redisStorageService, IGeneric generic)
        {
            _logger = logger;
            _settings = settings.Value;
            _simplexSettings = _setting2.Value;
            _appSettings = appSettings.Value;
            _context = context;
            _fileService = fileService;
            _smsBLService = smsBLService;
            _userCacheService = userCacheService;
            _redisStorageService = redisStorageService;
            _genServ = generic;
            _registrationService = registration;
            _templateService = templateService;
            _genericAssetCapitalInsuranceCustomerService = genericAssetCapitalInsuranceCustomerService;
        }

        public async Task<GenericResponse2> CustomerInvestmentSummary(string UserName, string Session, string UserType)
        {

            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = _genericAssetCapitalInsuranceCustomerService.ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Success = g.Success, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(Session, UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await _genericAssetCapitalInsuranceCustomerService.GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await _genericAssetCapitalInsuranceCustomerService.SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not avaialble" };
                }
                var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(UserName, UserType, con);
                if (usr == null)
                {
                    return new GenericResponse2() { Response = EnumResponse.UserNotFound };
                }
                if (usr.client_unique_ref == 0)
                {
                    return new GenericResponse2() { Response = EnumResponse.ClientIdNotFound, Message = "Your Client id is not found.Please contact the admin" };
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                string response = await _genServ.CallServiceAsyncToString(Method.GET, _settings.middlewarecustomerurl + "api/Portfolio/InvestmentSummary/"+usr.client_unique_ref,null, true, header);
                _logger.LogInformation("response " + response);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (genericResponse2 == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    InvestmentSummaryResponse ApiResponseDto = JsonConvert.DeserializeObject<InvestmentSummaryResponse>(JsonConvert.SerializeObject(genericResponse2?.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = ApiResponseDto, Message = "data collection successful from source" };

                }
                if (!genericResponse2.Success)
                {
                    InvestmentSummaryResponse ApiResponseDto = JsonConvert.DeserializeObject<InvestmentSummaryResponse>((string)(genericResponse2?.data));
                    return new GenericResponse2() { Response = EnumResponse.NotSuccessful, Success = false, data = ApiResponseDto, Message = "data collection successful from source" };
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            return genericResponse2;
        }
        // FixeddepositSubscriptionResponse
        public async Task<GenericResponse2> FixeddepositSubscription(string UserName, string session, string UserType, FixeddepositSubscriptionDto fixeddepositSubscription)
        {
            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = _genericAssetCapitalInsuranceCustomerService.ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Success = g.Success, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(session, UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await _genericAssetCapitalInsuranceCustomerService.GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await _genericAssetCapitalInsuranceCustomerService.SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not avaialble" };
                }
                var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(fixeddepositSubscription.UserName, UserType, con);
                if (usr == null)
                {
                    return new GenericResponse2() { Response = EnumResponse.UserNotFound };
                }
                if (usr.client_unique_ref == 0)
                {
                    return new GenericResponse2() { Response = EnumResponse.ClientIdNotFound, Message = "Your Client id is not found.Please contact the admin" };
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                //NGN,USD,GBP
                if (fixeddepositSubscription.currency != "NGN" &&
                    fixeddepositSubscription.currency != "USD" &&
                    fixeddepositSubscription.currency != "GBP"
                 )
                {
                    return new GenericResponse2()
                    {
                        Response = EnumResponse.InvalidCurrencyFormat,
                        Message = "One of NGN,USD,GBP is expected"
                    };
                }
                var fixedDepositDto = new FixeddepositSubscription()
                {
                    currency = fixeddepositSubscription.currency,
                    amount=fixeddepositSubscription.amount,
                    product_id=fixeddepositSubscription.product_id,
                    showInterest = fixeddepositSubscription.showInterest,
                    tenor=fixeddepositSubscription.tenor,
                    client_unique_ref= (int)usr.client_unique_ref
                };
                string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.middlewarecustomerurl + "api/Portfolio/FixedDepositSubscription", fixedDepositDto, true, header);
                _logger.LogInformation("response " + response);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (genericResponse2 == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    FixeddepositSubscriptionResponse ApiResponseDto = JsonConvert.DeserializeObject<FixeddepositSubscriptionResponse>(JsonConvert.SerializeObject(genericResponse2.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = ApiResponseDto, Message = "data collection successful from source" };

                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            return genericResponse2;
        }

        public async Task<GenericResponse2> FundCashAccount(FundCashAccountDto fundCashAccount,string PaymentReference)
        {
            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = _genericAssetCapitalInsuranceCustomerService.ValidateUserType(fundCashAccount.UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Success = g.Success, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(fundCashAccount.Session, fundCashAccount.UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await _genericAssetCapitalInsuranceCustomerService.GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await _genericAssetCapitalInsuranceCustomerService.SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not avaialble" };
                }
                var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(fundCashAccount.UserName, fundCashAccount.UserType, con);
                if (usr == null)
                {
                    return new GenericResponse2() { Response = EnumResponse.UserNotFound };
                }
                if (usr.client_unique_ref == 0)
                {
                    return new GenericResponse2() { Response = EnumResponse.ClientIdNotFound, Message = "Your Client id is not found.Please contact the admin" };
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                //NGN,USD,GBP
                if (fundCashAccount.currency != "NGN" &&
                    fundCashAccount.currency != "USD" &&
                    fundCashAccount.currency != "GBP"
                 )
                {
                    return new GenericResponse2()
                    {
                        Response = EnumResponse.InvalidCurrencyFormat,
                        Message = "One of NGN,USD,GBP is expected"
                    };
                }
                FundCashAccount fundCashAccount1 = new FundCashAccount()
                {
                    client_unique_ref = (int)usr.client_unique_ref,
                    amount = fundCashAccount.amount,
                    currency = fundCashAccount.currency,
                    paymentReference = PaymentReference
                };
                string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.middlewarecustomerurl + "api/Portfolio/FundCashAccount", fundCashAccount1, true, header);
                _logger.LogInformation("response " + response);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (genericResponse2 == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    SimplexGenericResponse ApiResponseDto = JsonConvert.DeserializeObject<SimplexGenericResponse>(JsonConvert.SerializeObject(genericResponse2?.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = ApiResponseDto, Message = "data collection successful from source" };

                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            return genericResponse2;
        }

        public async Task<GenericResponse2> FundCashAccount(FundCashAccountDto fundCashAccount)
        {
            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = _genericAssetCapitalInsuranceCustomerService.ValidateUserType(fundCashAccount.UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Success = g.Success, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(fundCashAccount.Session, fundCashAccount.UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await _genericAssetCapitalInsuranceCustomerService.GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await _genericAssetCapitalInsuranceCustomerService.SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not avaialble" };
                }
                var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(fundCashAccount.UserName,fundCashAccount.UserType, con);
                if (usr == null)
                {
                    return new GenericResponse2() { Response = EnumResponse.UserNotFound };
                }
                if (usr.client_unique_ref == 0)
                {
                    return new GenericResponse2() { Response = EnumResponse.ClientIdNotFound, Message = "Your Client id is not found.Please contact the admin" };
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                //NGN,USD,GBP
                if (fundCashAccount.currency != "NGN" &&
                    fundCashAccount.currency != "USD" &&
                    fundCashAccount.currency != "GBP"
                 )
                {
                    return new GenericResponse2()
                    {
                        Response = EnumResponse.InvalidCurrencyFormat,
                        Message = "One of NGN,USD,GBP is expected"
                    };
                }
                FundCashAccount fundCashAccount1 = new FundCashAccount()
                {
                    client_unique_ref = (int)usr.client_unique_ref,
                    amount = fundCashAccount.amount,
                    currency = fundCashAccount.currency,
                    paymentReference= await _genServ.GenerateUnitID(20)
                };
                string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.middlewarecustomerurl + "api/Portfolio/FundCashAccount", fundCashAccount1, true, header);
                _logger.LogInformation("response " + response);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (genericResponse2 == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    SimplexGenericResponse ApiResponseDto = JsonConvert.DeserializeObject<SimplexGenericResponse>(JsonConvert.SerializeObject(genericResponse2?.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = ApiResponseDto, Message = "data collection successful from source" };

                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            return genericResponse2;
        }

        public async Task<GenericResponse2> GetfixedDepositPortfolioHistories(int portfolioId, string UserName, string session, string UserType, string startDate, string endDate, int skip, int pageSize)
        {
            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = _genericAssetCapitalInsuranceCustomerService.ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Success = g.Success, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(session, UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await _genericAssetCapitalInsuranceCustomerService.GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await _genericAssetCapitalInsuranceCustomerService.SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not avaialble" };
                }
                var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(UserName, UserType, con);
                if (usr == null)
                {
                    return new GenericResponse2() { Response = EnumResponse.UserNotFound };
                }
                if (usr.client_unique_ref == 0)
                {
                    return new GenericResponse2() { Response = EnumResponse.ClientIdNotFound, Message = "Your Client id is not found.Please contact the admin" };
                }
                if(pageSize==0)
                {
                    pageSize = 10;// set as 10 by default
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                string response = await _genServ.CallServiceAsyncToString(Method.GET, _settings.middlewarecustomerurl +$"api/Portfolio/GetfixedDepositPortfolioHistories/{usr.client_unique_ref}/{startDate}/{endDate}/{skip}/{pageSize}", "", true, header);
                _logger.LogInformation("response " + response);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (genericResponse2 == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    ApiPortfolioHistoryResponse ApiResponseDto = JsonConvert.DeserializeObject<ApiPortfolioHistoryResponse>(JsonConvert.SerializeObject(genericResponse2.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = ApiResponseDto, Message = "data collection successful from source" };

                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            return genericResponse2;
        }

        public async Task<GenericResponse2> GetFullProductDetails(string session, string UserType)
        {
            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = _genericAssetCapitalInsuranceCustomerService.ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Success = g.Success, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(session, UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await _genericAssetCapitalInsuranceCustomerService.GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await _genericAssetCapitalInsuranceCustomerService.SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not avaialble" };
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                string response = await _genServ.CallServiceAsyncToString(Method.GET, _settings.middlewarecustomerurl + "api/Portfolio/GetFullProductDetails", "", true, header);
                _logger.LogInformation("response " + response);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (genericResponse2 == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    ApiResponseDto ApiResponseDto = JsonConvert.DeserializeObject<ApiResponseDto>(JsonConvert.SerializeObject(genericResponse2.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = ApiResponseDto, Message = "data collection successful from source" };

                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            return genericResponse2;
        }

        public async Task<GenericResponse2> GetPortfolioBalance(string UserName, string session, string UserType)
        {
            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = _genericAssetCapitalInsuranceCustomerService.ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Success = g.Success, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(session, UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await _genericAssetCapitalInsuranceCustomerService.GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await _genericAssetCapitalInsuranceCustomerService.SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not avaialble" };
                }
                var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(UserName,UserType,con);
                if (usr==null)
                {
                    return new GenericResponse2() {Response=EnumResponse.UserNotFound};
                }
                if (usr.client_unique_ref==0)
                {
                    return new GenericResponse2() { Response = EnumResponse.ClientIdNotFound,Message="Your Client id is not found.Please contact the admin" };
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                string response = await _genServ.CallServiceAsyncToString(Method.GET, _settings.middlewarecustomerurl + "api/Portfolio/GetPortfolioBalance/"+usr.client_unique_ref, "", true, header);
                _logger.LogInformation("response " + response);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (genericResponse2 == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    ApiPortfolioBalanceResponse ApiResponseDto = JsonConvert.DeserializeObject<ApiPortfolioBalanceResponse>(JsonConvert.SerializeObject(genericResponse2.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = ApiResponseDto, Message = "data collection successful from source" };

                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            return genericResponse2;
        }

        public async Task<GenericResponse2> GetPortfolioMutualFundHistory(string UserName, string session, string UserType, int portfolioId, string startDate, string endDate, int skip, int pageSize)
        {
            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = _genericAssetCapitalInsuranceCustomerService.ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Success = g.Success, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(session, UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await _genericAssetCapitalInsuranceCustomerService.GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await _genericAssetCapitalInsuranceCustomerService.SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not avaialble" };
                }
                var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(UserName, UserType, con);
                if (usr == null)
                {
                    return new GenericResponse2() { Response = EnumResponse.UserNotFound };
                }
                if (usr.client_unique_ref == 0)
                {
                    return new GenericResponse2() { Response = EnumResponse.ClientIdNotFound, Message = "Your Client id is not found.Please contact the admin" };
                }
                if (pageSize == 0)
                {
                    pageSize = 10;// set as 10 by default
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                string response = await _genServ.CallServiceAsyncToString(Method.GET, _settings.middlewarecustomerurl + $"api/Portfolio/GetPortfolioMutualFundHistory/{usr.client_unique_ref}/{portfolioId}/{skip}/{pageSize}?startDate="+startDate+"&endDate="+endDate, null, true, header);
                _logger.LogInformation("response " + response);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (genericResponse2 == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    PortfolioMutualFundResponse ApiResponseDto = JsonConvert.DeserializeObject<PortfolioMutualFundResponse>(JsonConvert.SerializeObject(genericResponse2.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = ApiResponseDto, Message = "data collection successful from source" };

                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            return genericResponse2;
        }

        public async Task<GenericResponse2> GetPortfolioWalletHistory(string UserName, string session, string UserType, int portfolioId, string startDate, string endDate, int skip, int pageSize)
        {
            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = _genericAssetCapitalInsuranceCustomerService.ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Success = g.Success, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(session, UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await _genericAssetCapitalInsuranceCustomerService.GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await _genericAssetCapitalInsuranceCustomerService.SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not avaialble" };
                }
                var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(UserName, UserType, con);
                if (usr == null)
                {
                    return new GenericResponse2() { Response = EnumResponse.UserNotFound };
                }
                if (usr.client_unique_ref == 0)
                {
                    return new GenericResponse2() { Response = EnumResponse.ClientIdNotFound, Message = "Your Client id is not found.Please contact the admin" };
                }
                if (pageSize == 0)
                {
                    pageSize = 10;// set as 10 by default
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                string response = await _genServ.CallServiceAsyncToString(Method.GET, $"{_settings.middlewarecustomerurl}api/Portfolio/GetPortfolioWalletHistory/{usr.client_unique_ref}?startDate={startDate}&endDate={endDate}&skip={skip}&pageSize={pageSize}",null, true, header);
                _logger.LogInformation("response " + response);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (genericResponse2 == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    SimplexWalletHistory ApiResponseDto = JsonConvert.DeserializeObject<SimplexWalletHistory>(JsonConvert.SerializeObject(genericResponse2.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = ApiResponseDto, Message = "data collection successful from source" };
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            return genericResponse2;
        }

        //FixeddepositSubscriptionResponse
        public async Task<GenericResponse2> MutualFundSubscription(string userName, string session, string UserType, MutualFundSubscriptionDto mutualFundSubscription)
        {
            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = _genericAssetCapitalInsuranceCustomerService.ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Success = g.Success, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(session, UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await _genericAssetCapitalInsuranceCustomerService.GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await _genericAssetCapitalInsuranceCustomerService.SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not avaialble" };
                }
                var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(mutualFundSubscription.Username, UserType, con);
                if (usr == null)
                {
                    return new GenericResponse2() { Response = EnumResponse.UserNotFound };
                }
                if (usr.client_unique_ref == 0)
                {
                    return new GenericResponse2() { Response = EnumResponse.ClientIdNotFound, Message = "Your Client id is not found.Please contact the admin" };
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                //NGN,USD,GBP
                if (mutualFundSubscription.currency != "NGN" &&
                    mutualFundSubscription.currency != "USD" &&
                    mutualFundSubscription.currency != "GBP"
                 )
                {
                    return new GenericResponse2()
                    {
                        Response = EnumResponse.InvalidCurrencyFormat,
                        Message = "One of NGN,USD,GBP is expected"
                    };
                }
                var mutualFundDto = new MutualFundSubscription()
                {
                    client_unique_ref= (int)usr.client_unique_ref,
                    product_id=mutualFundSubscription.product_id,
                    amount=mutualFundSubscription.amount,
                    currency=mutualFundSubscription.currency,
                    paymentChannel=mutualFundSubscription.paymentChannel,
                    paymentReference=mutualFundSubscription.paymentReference
                };   
                string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.middlewarecustomerurl + "api/Portfolio/MutualFundSubscription",mutualFundDto, true, header);
                _logger.LogInformation("response " + response);

                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (genericResponse2 == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    MutualFundRespponse ApiResponseDto = JsonConvert.DeserializeObject<MutualFundRespponse>(JsonConvert.SerializeObject(genericResponse2?.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = ApiResponseDto, Message = "data collection successful from source" };

                }
                if (!genericResponse2.Success)
                {
                    MutualFundRespponse ApiResponseDto = JsonConvert.DeserializeObject<MutualFundRespponse>((string)(genericResponse2?.data));
                    return new GenericResponse2() { Response = EnumResponse.NotSuccessful, Success = false, data = ApiResponseDto, Message = "data collection successful from source" };
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            return genericResponse2;
        }
    }
}





























































