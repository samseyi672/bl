using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using RestSharp;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using Retailbanking.BL.templates;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace Retailbanking.BL.Services
{
    public class FlutterPaymentLink : IFlutterPaymentLink
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
        private readonly IPortfolioService _portfolioService;
        private readonly IGenericAssetCapitalInsuranceCustomerService _genericAssetCapitalInsuranceCustomerService;

        // IOptions<AppSettings> appSettings, IOptions<SimplexConfig> _setting2

        public FlutterPaymentLink(IPortfolioService portfolioService, ILogger<IGenericAssetCapitalInsuranceCustomerService> logger, IOptions<AssetSimplexConfig> settings, DapperContext context, IFileService fileService, ISmsBLService smsBLService, IUserCacheService userCacheService, IRedisStorageService redisStorageService, IGeneric genServ, IMemoryCache cache, IOptions<AppSettings> appSettings, IOptions<SimplexConfig> simplexSettings, IRegistration registrationService, TemplateService templateService, IGenericAssetCapitalInsuranceCustomerService genericAssetCapitalInsuranceCustomerService)
        {
            _logger = logger;
            _settings = settings.Value;
            _context = context;
            _fileService = fileService;
            _smsBLService = smsBLService;
            _userCacheService = userCacheService;
            _redisStorageService = redisStorageService;
            _genServ = genServ;
            _cache = cache;
            _appSettings = appSettings.Value;
            _simplexSettings = simplexSettings.Value;
            _registrationService = registrationService;
            _templateService = templateService;
            _genericAssetCapitalInsuranceCustomerService = genericAssetCapitalInsuranceCustomerService;
            _portfolioService = portfolioService;
        }

        public async Task<GenericResponse2> FundWalletAfterTransaction(string appKey, FundWalletAfterTransaction fundWalletAfterTransaction)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                // await Task.Delay(1000);
                var g = _genericAssetCapitalInsuranceCustomerService.ValidateUserType(fundWalletAfterTransaction.AppType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Message = g.Message };
                }
                if (!await _genServ.ValidateSessionForAssetCapitalInsurance(fundWalletAfterTransaction.Session, fundWalletAfterTransaction.AppType, con))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                if (fundWalletAfterTransaction.PaymentChannelOptionForsubscription != "wallet")
                {
                    return new GenericResponse2() { Response = EnumResponse.PaymentChannelNotAllowed, Success = false, Message = "Payment option shd be wallet or nil" };
                }
                var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(fundWalletAfterTransaction.UserName, fundWalletAfterTransaction.AppType, con);
                if (usr == null)
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.UserNotFound, Message = "User not found" };
                }
                AssetCapitalInsuranceCustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService
                    .getAssetCapitalInsuranceCustomerRelationService(con, (int)usr.id, fundWalletAfterTransaction.AppType);
                SendMailObject sendMailObject = new SendMailObject();
                FundCashAccountDto fundCashAccountDto = new FundCashAccountDto()
                {
                    amount= fundWalletAfterTransaction.Amount,
                    currency=fundWalletAfterTransaction.currency,
                    Session=fundWalletAfterTransaction.Session,
                    UserName=fundWalletAfterTransaction.UserName,
                    UserType=fundWalletAfterTransaction.AppType
                };
                GenericResponse2 genericResponse2 =await _portfolioService.FundCashAccount(fundCashAccountDto,fundWalletAfterTransaction.PaymentReference);
                if(genericResponse2.Success)
                {
                    // send a mail .
                    Task.Run(() =>
                    {
                        // sendMailObject.Email = Users.Email;
                       // sendMailObject.Email = customerDataNotFromBvn != null ? customerDataNotFromBvn.email : usr.email;
                        sendMailObject.Subject = _appSettings.AssetMailSubject + " Wallet Funding";
                        CultureInfo nigerianCulture = new CultureInfo("en-NG");
                        string filepath = Path.Combine(_settings.PartialViews, "walletfunding.html");
                        Console.WriteLine("filepath " + filepath);
                        _logger.LogInformation("filepath " + filepath);
                        var data1 = new
                        {
                            title = "Investment Subscription",
                            firstname = FirstLetterUppercaseMaker.CapitalizeFirstLetter(usr.first_name),
                            lastname = FirstLetterUppercaseMaker.CapitalizeFirstLetter(usr.last_name),
                            year = DateTime.Now.Year,
                            paymentreference=fundWalletAfterTransaction.PaymentReference,
                            amount=fundWalletAfterTransaction.Amount,
                            date = DateTime.Now.ToString()
                        };
                        var email = string.IsNullOrEmpty(customerDataNotFromBvn.email) ? customerDataNotFromBvn?.email : usr.email;
                        sendMailObject.Email = email;
                        string htmlContent = _templateService.RenderScribanTemplate(filepath, data1);
                        _logger.LogInformation("mail sending");
                        sendMailObject.Html = htmlContent;
                        _genServ.SendMail(sendMailObject);
                        _logger.LogInformation("funding mail sent");
                    });
                }
                return genericResponse2;
            }
            catch (Exception ex)
            {
                _logger.LogInformation("exception " + ex.Message);
            }
            return new GenericResponse2() { Response = EnumResponse.PaymentFailed, data = fundWalletAfterTransaction.PaymentReference };
        }
           
    public async Task<GenericResponse2> GetPaymentLink(string AppKey, string AppType, PaymentLinkRequestDto paymentLinkRequestDto,string Session)
        {
            try
            {
                IDictionary<string, string> header = new Dictionary<string, string>();
                using IDbConnection con = _context.CreateConnection();
                if (string.IsNullOrEmpty(AppKey)||string.Equals(AppKey,_appSettings.Appkey,StringComparison.CurrentCultureIgnoreCase))
                {
                    return new GenericResponse2()
                    {
                        Message = "Invalid client key",
                        Response = EnumResponse.InvalidClient,
                        Success = false
                    };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(Session,AppType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                var g = _genericAssetCapitalInsuranceCustomerService.ValidateUserType(AppType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Message = g.Message };
                }
                header.Add("appkey",AppKey);
                string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.flutterwaveurl + "paymentlink/"+AppType,paymentLinkRequestDto, true, header);
                _logger.LogInformation("response " + response);
                var PaymentLinkResponse = JsonConvert.DeserializeObject<PaymentLinkResponse>(response);
                if (PaymentLinkResponse == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }

                return new GenericResponse2()
                {
                    Response = EnumResponse.Successful,
                    data= PaymentLinkResponse,
                    Success = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Message "+ex.Message);
            }
            return new GenericResponse2()
            {
                Message = "No data was found from source",
                Response = EnumResponse.NotDataFound,
                Success = false
            };
        }

        public async Task<GenericResponse2> GetPaymentResponseAfterTransaction(string appKey,GetPaymentResponseAfterTransaction getPaymentResponseAfter)
        {
            try
            {
              using IDbConnection con = _context.CreateConnection();
               // await Task.Delay(1000);
                var g = _genericAssetCapitalInsuranceCustomerService.ValidateUserType(getPaymentResponseAfter.AppType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Message = g.Message };
                }
                if (!await _genServ.ValidateSessionForAssetCapitalInsurance(getPaymentResponseAfter.Session, getPaymentResponseAfter.AppType, con))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                if(getPaymentResponseAfter.PaymentChannelOptionForsubscription!="wallet" && getPaymentResponseAfter.PaymentChannelOptionForsubscription!="nil")
                {
                    return new GenericResponse2() {Response=EnumResponse.PaymentChannelNotAllowed,Success=false,Message="Payment option shd be wallet or nil"};
                }
                var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(getPaymentResponseAfter.UserName, getPaymentResponseAfter.AppType, con);
                if(usr ==null)
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.UserNotFound, Message = "User not found" };
                }
                AssetCapitalInsuranceCustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService
                    .getAssetCapitalInsuranceCustomerRelationService(con, (int)usr.id, getPaymentResponseAfter.AppType);
                SendMailObject sendMailObject = new SendMailObject();
                if (string.Equals(getPaymentResponseAfter.PaymentChannelOptionForsubscription,"wallet",StringComparison.CurrentCultureIgnoreCase))
                {
                    var mutualFundSubscriptionDto = new MutualFundSubscriptionDto()
                    {
                        amount = getPaymentResponseAfter.Amount,
                        currency = getPaymentResponseAfter.currency,
                        product_id = getPaymentResponseAfter.PortfolioId,
                        paymentChannel = getPaymentResponseAfter?.PaymentChannelOptionForsubscription,
                        paymentReference = getPaymentResponseAfter.PaymentReference,
                        Username = getPaymentResponseAfter.UserName
                    };
                    _logger.LogInformation("MutualFund Request " + JsonConvert.SerializeObject(mutualFundSubscriptionDto));
                    var MutualFundResponse = await _portfolioService.MutualFundSubscription(getPaymentResponseAfter.UserName, getPaymentResponseAfter.Session, getPaymentResponseAfter.AppType, mutualFundSubscriptionDto);
                    if(MutualFundResponse.Success)
                    {
                        Task.Run(() =>
                        {
                            // sendMailObject.Email = Users.Email;
                            sendMailObject.Email = customerDataNotFromBvn != null ? customerDataNotFromBvn.email : usr.email;
                            sendMailObject.Subject = _appSettings.AssetMailSubject + " Susbcription";
                            CultureInfo nigerianCulture = new CultureInfo("en-NG");
                            var data = new
                            {
                                title = "Investment Subscription",
                                firstname = FirstLetterUppercaseMaker.CapitalizeFirstLetter(usr.first_name),
                                lastname = FirstLetterUppercaseMaker.CapitalizeFirstLetter(usr.last_name),
                                year = DateTime.Now.Year
                            };
                            string filepath = Path.Combine(_settings.PartialViews, "customersubscriptiontemplate.html");
                            Console.WriteLine("filepath " + filepath);
                            _logger.LogInformation("filepath " + filepath);
                            //string htmlContent = await _templateService.RenderTemplateAsync(filepath, model);
                            string htmlContent = _templateService.RenderScribanTemplate(filepath, data);
                            _logger.LogInformation("mail sending");
                            sendMailObject.Html = htmlContent;
                            _genServ.SendMail(sendMailObject);
                            // send mail to customer service and asset
                            sendMailObject.Email = _appSettings.CustomerServiceEmail;
                            var data1 = new
                            {
                                title = "Investment Subscription",
                                firstname = FirstLetterUppercaseMaker.CapitalizeFirstLetter(usr.first_name),
                                lastname = FirstLetterUppercaseMaker.CapitalizeFirstLetter(usr.last_name),
                                year = DateTime.Now.Year,
                                phonenumber = customerDataNotFromBvn?.phonenumber,
                                email = string.IsNullOrEmpty(customerDataNotFromBvn.email) ? customerDataNotFromBvn?.email : usr.email
                            };
                            string filepath1 = Path.Combine(_settings.PartialViews, "mutualcustomerservicetemplate.html");
                            // Console.WriteLine("filepath " + filepath1);
                            _logger.LogInformation("filepath " + filepath1);
                            sendMailObject.Html = _templateService.RenderScribanTemplate(filepath1, data1);
                            _genServ.SendMail(sendMailObject);
                        });
                    }
                    return MutualFundResponse;
                }
                GenericResponse2 genericResponse2 = await PaymentResponseAfterTransaction(appKey, getPaymentResponseAfter.PaymentReference, getPaymentResponseAfter.PaymentChannelOptionForsubscription,getPaymentResponseAfter.AppType);
                PaymentResponse paymentResponse = (PaymentResponse)genericResponse2?.data;
                if (paymentResponse == null)
                {
                    return new GenericResponse2() { Response = genericResponse2.Response, data = getPaymentResponseAfter.PaymentReference };
                }
                if (paymentResponse.status == "success")
                {
                    // book subscription and send email to customer
                    var mutualFundSubscriptionDto = new MutualFundSubscriptionDto()
                    {
                        amount = paymentResponse.data.amount,
                        currency = paymentResponse.data.currency,
                        product_id = getPaymentResponseAfter.PortfolioId,
                        paymentChannel = getPaymentResponseAfter?.PaymentChannelOptionForsubscription,
                        paymentReference = getPaymentResponseAfter.PaymentReference,
                        Username = getPaymentResponseAfter.UserName
                    };
                    _logger.LogInformation("MutualFund Request " + JsonConvert.SerializeObject(mutualFundSubscriptionDto));
                    var MutualFundResponse = await _portfolioService.MutualFundSubscription(getPaymentResponseAfter.UserName, getPaymentResponseAfter.Session, getPaymentResponseAfter.AppType, mutualFundSubscriptionDto);
                    _logger.LogInformation("MutualFundResponse " + JsonConvert.SerializeObject(MutualFundResponse));
                    if(MutualFundResponse.Success)
                    {
                        if(paymentResponse.data.status=="successful")
                        {
                            Task.Run(() =>
                            {
                                // sendMailObject.Email = Users.Email;
                                sendMailObject.Email = customerDataNotFromBvn != null ? customerDataNotFromBvn.email : usr.email;
                                sendMailObject.Subject = _appSettings.AssetMailSubject + " Susbcription";
                                CultureInfo nigerianCulture = new CultureInfo("en-NG");
                                var data = new
                                {
                                    title = "Investment Subscription",
                                    firstname = FirstLetterUppercaseMaker.CapitalizeFirstLetter(usr.first_name),
                                    lastname = FirstLetterUppercaseMaker.CapitalizeFirstLetter(usr.last_name),
                                    year = DateTime.Now.Year
                                };
                                string filepath = Path.Combine(_settings.PartialViews, "customersubscriptiontemplate.html");
                                Console.WriteLine("filepath " + filepath);
                                _logger.LogInformation("filepath " + filepath);
                                //string htmlContent = await _templateService.RenderTemplateAsync(filepath, model);
                                string htmlContent = _templateService.RenderScribanTemplate(filepath, data);
                                _logger.LogInformation("mail sending");
                                sendMailObject.Html = htmlContent;
                                _genServ.SendMail(sendMailObject);
                                // send mail to customer service and asset
                                sendMailObject.Email = _appSettings.CustomerServiceEmail;
                                var data1 = new
                                {
                                    title = "Investment Subscription",
                                    firstname = FirstLetterUppercaseMaker.CapitalizeFirstLetter(usr.first_name),
                                    lastname = FirstLetterUppercaseMaker.CapitalizeFirstLetter(usr.last_name),
                                    year = DateTime.Now.Year,
                                    phonenumber = customerDataNotFromBvn?.phonenumber,
                                    email = string.IsNullOrEmpty(customerDataNotFromBvn.email) ? customerDataNotFromBvn?.email : usr.email
                                };
                                string filepath1 = Path.Combine(_settings.PartialViews, "mutualcustomerservicetemplate.html");
                                // Console.WriteLine("filepath " + filepath1);
                                _logger.LogInformation("filepath " + filepath1);
                                sendMailObject.Html = _templateService.RenderScribanTemplate(filepath1, data1);
                                _genServ.SendMail(sendMailObject);
                            });
                        }                        
                    }
                    else
                    {
                        if(paymentResponse.data.status=="successful")
                        {
                            Task.Run(() =>
                            {
                                // sendMailObject.Email = Users.Email;
                                sendMailObject.Email = customerDataNotFromBvn != null ? customerDataNotFromBvn.email : usr.email;
                                sendMailObject.Subject = _appSettings.AssetMailSubject + " Susbcription";
                                CultureInfo nigerianCulture = new CultureInfo("en-NG");
                                var data = new
                                {
                                    title = "Investment Subscription",
                                    firstname = FirstLetterUppercaseMaker.CapitalizeFirstLetter(usr.first_name),
                                    lastname = FirstLetterUppercaseMaker.CapitalizeFirstLetter(usr.last_name),
                                    year = DateTime.Now.Year
                                };
                                string filepath = Path.Combine(_settings.PartialViews, "customersubscriptiontemplate.html");
                                Console.WriteLine("filepath " + filepath);
                                _logger.LogInformation("filepath " + filepath);
                                //string htmlContent = await _templateService.RenderTemplateAsync(filepath, model);
                                string htmlContent = _templateService.RenderScribanTemplate(filepath, data);
                                _logger.LogInformation("mail sending");
                                sendMailObject.Html = htmlContent;
                                _genServ.SendMail(sendMailObject);
                                // send mail to customer service and asset
                                sendMailObject.Email = _appSettings.CustomerServiceEmail;
                                var data1 = new
                                {
                                    title = "Investment Subscription",
                                    firstname = FirstLetterUppercaseMaker.CapitalizeFirstLetter(usr.first_name),
                                    lastname = FirstLetterUppercaseMaker.CapitalizeFirstLetter(usr.last_name),
                                    year = DateTime.Now.Year,
                                    phonenumber = customerDataNotFromBvn?.phonenumber,
                                    email = string.IsNullOrEmpty(customerDataNotFromBvn.email) ? customerDataNotFromBvn?.email : usr.email
                                };
                                string filepath1 = Path.Combine(_settings.PartialViews, "customermutualfundemail.html");
                                // Console.WriteLine("filepath " + filepath1);
                                _logger.LogInformation("filepath " + filepath1);
                                sendMailObject.Html = _templateService.RenderScribanTemplate(filepath1, data1);
                                _genServ.SendMail(sendMailObject);
                            });
                        }
                        MutualFundResponse.Response = EnumResponse.TransactionButSubscriptionFailed;
                        return MutualFundResponse;
                    }
                    return MutualFundResponse;               
                }
              if (paymentResponse.status != "success")
                {
                    return new GenericResponse2() { Response = genericResponse2.Response, Success = true, data = getPaymentResponseAfter.PaymentReference };
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation("exception "+ex.Message);
            }
            return new GenericResponse2() { Response = EnumResponse.PaymentFailed, data = getPaymentResponseAfter.PaymentReference };
        }

        public async Task<GenericResponse2> SendExternalPaymentNotification(string Session, string UserName, string UserType,decimal Amount,string PaymentReference,string BankName,string AccountNumber)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                // await Task.Delay(1000);
                var g = _genericAssetCapitalInsuranceCustomerService.ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Message = g.Message };
                }
                if (!await _genServ.ValidateSessionForAssetCapitalInsurance(Session, UserType, con))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(UserName,UserType, con);
                if(usr==null)
                {
                    return new GenericResponse2()
                    {
                        Success = false,
                        Response = EnumResponse.UserNotFound,
                        Message = "user not found"
                    };
                }
                Task.Run(async () =>
                {
                // sendMailObject.Email = Users.Email;
                SendMailObject sendMailObject = new SendMailObject();
                AssetCapitalInsuranceCustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getAssetCapitalInsuranceCustomerRelationService(con, (int)usr.id, UserType);
                sendMailObject.Email = customerDataNotFromBvn != null ? customerDataNotFromBvn.email : usr.email;
                sendMailObject.Subject = _appSettings.AssetMailSubject + " Payment";
                CultureInfo nigerianCulture = new CultureInfo("en-NG");
                var data = new
                {
                    title = "Investment Payment Subscription",
                    firstname = FirstLetterUppercaseMaker.CapitalizeFirstLetter(usr.first_name),
                    lastname = FirstLetterUppercaseMaker.CapitalizeFirstLetter(usr.last_name),
                    year = DateTime.Now.Year,
                    amount=Amount,
                    paymentreference = PaymentReference,
                    date=DateTime.Now.ToString()
                };
                string filepath = Path.Combine(_settings.PartialViews, "paymentnotificationoutsideflutterwave.html");
                Console.WriteLine("filepath " + filepath);
                _logger.LogInformation("filepath " + filepath);
                //string htmlContent = await _templateService.RenderTemplateAsync(filepath, model);
                string htmlContent = _templateService.RenderScribanTemplate(filepath, data);
                _logger.LogInformation("mail sending");
                sendMailObject.Html = htmlContent;
                    _genServ.SendMail(sendMailObject);
                    _logger.LogInformation("mail sent to customer");
                    // send mail to customer service and asset
                    sendMailObject.Email = _appSettings.CustomerServiceEmail;
                var data1 = new
                {
                    title = "Investment Payment Subscription",
                    firstname = FirstLetterUppercaseMaker.CapitalizeFirstLetter(usr.first_name),
                    lastname = FirstLetterUppercaseMaker.CapitalizeFirstLetter(usr.last_name),
                    year = DateTime.Now.Year,
                    date = DateTime.Now.ToString(),
                    phonenumber = customerDataNotFromBvn?.phonenumber,
                    customeremail=customerDataNotFromBvn?.email,
                    amount = Amount,
                    paymentreference = PaymentReference
                };
                string filepath1 = Path.Combine(_settings.PartialViews, "customerpaymentnotification.html");
                // Console.WriteLine("filepath " + filepath1);
                _logger.LogInformation("filepath " + filepath1);
                sendMailObject.Html = _templateService.RenderScribanTemplate(filepath1, data1);
                    _genServ.SendMail(sendMailObject);
                    _logger.LogInformation("mail sent to customer service");
                    //sending to operation
                    _logger.LogInformation("filepath " + filepath1);
                    sendMailObject.Email = _appSettings.OperationEmail;
                    sendMailObject.Html = _templateService.RenderScribanTemplate(filepath1, data1);
                    _genServ.SendMail(sendMailObject);
                    _logger.LogInformation("mail sent to operation");
                });
            }catch (Exception ex)
            {
                _logger.LogInformation("message "+ex.Message);
            }
            return new GenericResponse2() { Response = EnumResponse.Successful, Success = true,Message="mail sent successfully" };
           // throw new Exception("Service failure");
        }

        public async Task<GenericResponse2> VerifyTransaction(string AppKey, string AppType, string TransactionId)
        {
            try
            {
                IDictionary<string, string> header = new Dictionary<string, string>();
                if (string.IsNullOrEmpty(AppKey) || string.Equals(AppKey, _appSettings.Appkey, StringComparison.CurrentCultureIgnoreCase))
                {
                    return new GenericResponse2()
                    {
                        Message = "Invalid client key",
                        Response = EnumResponse.InvalidClient,
                        Success = false
                    };
                }
                var g = _genericAssetCapitalInsuranceCustomerService.ValidateUserType(AppType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Message = g.Message };
                }
                header.Add("appkey", _appSettings.Appkey);
                //header.Add("apptype",AppType);
                string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.flutterwaveurl2 + "verifytransaction/" + AppType+ "/transactionId/" + _appSettings.Appkey, null, true, header);
                _logger.LogInformation("response " + response);
                var paymentResponse = JsonConvert.DeserializeObject<PaymentResponse>(response);
                if (paymentResponse == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }

                return new GenericResponse2()
                {
                    Response = EnumResponse.Successful,
                    data = paymentResponse,
                    Success = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Message " + ex.Message);
            }
            return new GenericResponse2()
            {
                Message = "No data was found from source",
                Response = EnumResponse.NotDataFound,
                Success = false
            };
        }

        private async Task<GenericResponse2> PaymentResponseAfterTransaction(string AppKey, string TransactionReference,string PaymentChannel,string appType)
        {
            try
            {
                IDictionary<string, string> header = new Dictionary<string, string>();
                if (string.IsNullOrEmpty(AppKey) || string.Equals(AppKey, _appSettings.Appkey, StringComparison.CurrentCultureIgnoreCase))
                {
                    return new GenericResponse2()
                    {
                        Message = "Invalid client key",
                        Response = EnumResponse.InvalidClient,
                        Success = false
                    };
                }
                header.Add("appkey",AppKey);
                string response = await _genServ.CallServiceAsyncToString(Method.GET, _settings.flutterwaveurl2 + "api/v1/afterpayment/payment/" + TransactionReference+"/"+ appType, null, true, header);
                _logger.LogInformation("response " + response);
                if(string.IsNullOrEmpty(response))
                {
                    return new GenericResponse2()
                    {
                        Message = "Invalid client key",
                        Response = EnumResponse.NoPaymentFound,
                        Success = false
                    };
                }
                var paymentResponse = JsonConvert.DeserializeObject<PaymentResponse>(response);
                _logger.LogInformation("paymentResponse " + paymentResponse);
                if (paymentResponse == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NoPaymentFound,
                        Success = false
                    };
                }
                if (paymentResponse.data.status != "successful")
                {
                    // book subscription and send email to customer
                    return new GenericResponse2()
                    {
                        Response = EnumResponse.PaymentFailed,
                        data = paymentResponse,
                        Success = true
                    };
                }
                if (paymentResponse.data.status== "successful")
                {
                    return new GenericResponse2()
                    {
                        Response = EnumResponse.Successful,
                        data = paymentResponse,
                        Success = true
                    };
                }
   
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Message " + ex.Message);
            }
            return new GenericResponse2()
            {
                Message = "No data was found from source",
                Response = EnumResponse.NotDataFound,
                Success = false
            };
        }
    }
}

























































