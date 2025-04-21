using Dapper;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlX.XDevAPI;
using Newtonsoft.Json;
using Org.BouncyCastle.Asn1.Ocsp;
using Org.BouncyCastle.Crypto.Agreement;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using RestSharp;
using Method = RestSharp.Method;

namespace Retailbanking.BL.Services
{
    public class LoanService : ILoanService
    {
        private readonly ILogger<TransferServices> _logger;
        private readonly AppSettings _settings;
        private readonly IGeneric _genServ;
        private readonly IBeneficiary _benServ;
        private readonly IMemoryCache _cache;
        private readonly DapperContext _context;
        private readonly ISmsBLService _smsBLService;

        public LoanService(ILogger<TransferServices> logger, IOptions<AppSettings> options, IGeneric genServ, IBeneficiary benServ, IMemoryCache cache, DapperContext context, ISmsBLService smsBLService)
        {
            _logger = logger;
            _settings = options.Value;
            _genServ = genServ;
            _benServ = benServ;
            _cache = cache;
            _context = context;
            _smsBLService = smsBLService;
        }

        public async Task<GenericResponse> CalculateRetailLoan(RetailLoanCalculator retailLoanCalculator)
        {

            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(retailLoanCalculator.Username, retailLoanCalculator.Session, retailLoanCalculator.ChannelId, con);
                    if (!validateSession)
                        return new TransResponse() { Response = EnumResponse.InvalidSession };
                    RetailLoanCalculatorResponse response = new RetailLoanCalculatorResponse();
                    CultureInfo nigerianCulture = new CultureInfo("en-NG");
                    // string formattedAmount = Request.Amount.ToString("C", nigerianCulture);
                    response.InterestRate = Convert.ToDecimal(_settings.RetailLoanInterestRate);
                    if (retailLoanCalculator.Tenor < 3)
                    {
                        return new GenericResponse() { Success = false, Response = EnumResponse.NotLoanTenor };
                    }
                    if (retailLoanCalculator.Tenor > 12)
                    {
                        return new GenericResponse() { Success = false, Response = EnumResponse.NotLoanTenor };
                    }
                    response.Tenor = retailLoanCalculator.Tenor;
                    response.loanAmount = retailLoanCalculator.loanAmount;
                    response.PrincipalRePayment = Math.Round((retailLoanCalculator.loanAmount / retailLoanCalculator.Tenor), 2);
                    response.MonthlyInterestRePayment = Math.Round((retailLoanCalculator.loanAmount * response.InterestRate), 2);
                    response.TotalInterestRePayment = Math.Round(((response.MonthlyInterestRePayment * retailLoanCalculator.Tenor)), 2);
                    response.TotalExpectedRepayment = Math.Round(retailLoanCalculator.loanAmount + response.TotalInterestRePayment, 2);
                    response.LoanManagementFee = _settings.LoanManagementFee;
                    response.MonthlyRePayment = Math.Round((response.TotalExpectedRepayment / retailLoanCalculator.Tenor), 2);
                    // response.TotalRePayment = Math.Round((response.MonthlyRePayment* retailLoanCalculator.Tenor),2);
                    // response.TotalDisbursementAmount = Math.Round(retailLoanCalculator.loanAmount-(retailLoanCalculator.loanAmount*_settings.LoanManagementFee),2);
                    response.MonthlyInterestRePayment = 0.0m;
                    return new BillPaymentResponse() { Response = EnumResponse.Successful, Success = true, Data = response };
                    //return null;
                }
            }
            catch (Exception ex)
            {
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public Task<GenericResponse> createPublicSectorLoan(PublicSectorLoan publicSectorLoan)
        {
            throw new NotImplementedException();
        }

        public async Task<GenericResponse> createRetailLoan(RetailLoan retailLoan)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(retailLoan.Username, retailLoan.Session, retailLoan.ChannelId, con);
                    if (!validateSession)
                        return new TransResponse() { Response = EnumResponse.InvalidSession };
                    var usr = await _genServ.GetUserbyUsername(retailLoan.Username, con);
                    // var CustomerDetails = await _genServ.GetCustomerbyAccountNo(retailLoan.Account);
                    // var balance = await _genServ.GetAccountbyCustomerId(CustomerDetails.result.customerID);
                    /*
                    await con.ExecuteAsync($@"insert into RETAIL_LOAN(ApplicationServiceNumber,
                        DesiredLoanAmount,Account,LoanPurpose,Employer,EmploymentDate,
                        EmploymentAddress,ResidentialAddress,FirstName,LastName,NextOfRelationShip,
                        NextOfKinEmail,Agreement,NextOfKinPhoneNumber,userid,CreatedOn) 
                        values(@ApplicationServiceNumber,@DesiredLoanAmount,@Account,@LoanPurpose,
                        @Employer,@EmploymentDate,@EmploymentAddress,@ResidentialAddress,@FirstName,@LastName,
                        @NextOfRelationShip,@NextOfKinEmail,@Agreement,@NextOfKinPhoneNumber,@userid,@CreatedOn)", new
                    {
                        ApplicationServiceNumber = retailLoan.ApplicationServiceNumber,
                        DesiredLoanAmount = retailLoan.DesiredLoanAmount,
                        Account = retailLoan.Account,
                        LoanPurpose = retailLoan.LoanPurpose,
                        Employer = retailLoan.Employer,
                        EmploymentDate = "",
                        EmploymentAddress = retailLoan.EmploymentAddress,
                        ResidentialAddress = "",
                        FirstName = "",
                        LastName = "",
                        NextOfRelationShip = "",
                        NextOfKinEmail = "",
                        Agreement = retailLoan.Agreement,
                        NextOfKinPhoneNumber = "",
                        userid = usr.Id,
                        CreatedOn = DateTime.Now
                    });
                    */
                                                            await con.ExecuteAsync(@"
                                            INSERT INTO RETAIL_LOAN (
                                                ApplicationServiceNumber, DesiredLoanAmount, Account, LoanPurpose,
                                                Employer, EmploymentDate, EmploymentAddress, ResidentialAddress, FirstName,
                                                LastName, NextOfRelationShip, NextOfKinEmail, Agreement, NextOfKinPhoneNumber, 
                                                userid, CreatedOn) 
                                            VALUES (
                                                @ApplicationServiceNumber, @DesiredLoanAmount, @Account, @LoanPurpose,
                                                @Employer, @EmploymentDate, @EmploymentAddress, @ResidentialAddress, @FirstName,
                                                @LastName, @NextOfRelationShip, @NextOfKinEmail, @Agreement, 
                                                @NextOfKinPhoneNumber, @userid, @CreatedOn)",
                                        new
                                        {
                                        ApplicationServiceNumber = retailLoan.ApplicationServiceNumber,
                                        DesiredLoanAmount = retailLoan.DesiredLoanAmount,
                                        Account = retailLoan.Account,
                                        LoanPurpose = retailLoan.LoanPurpose,
                                        Employer = retailLoan.Employer,
                                        EmploymentDate = (DateTime?)null, // Use nullable DateTime for EmploymentDate
                                        EmploymentAddress = retailLoan.EmploymentAddress,
                                        ResidentialAddress = (string)null,
                                        FirstName = (string)null,
                                        LastName = (string)null,
                                        NextOfRelationShip = (string)null,
                                        NextOfKinEmail = (string)null,
                                        Agreement = retailLoan.Agreement,
                                        NextOfKinPhoneNumber = (string)null,
                                        userid = usr.Id,
                                        CreatedOn = DateTime.Now
                                        });
                    // send mail 
                    GenericBLServiceHelper genericBLServiceHelper = new GenericBLServiceHelper();
                    CustomerDataNotFromBvn customerDataNotFromBvn = await genericBLServiceHelper.getCustomerData(con, "select PhoneNumber,Email from customerdatanotfrombvn where userid=" + usr.Id);
                    //  balance.balances.
                    //BalanceEnquiryDetails balanceEnquiryDetails = balance.balances.Any() ? balance.balances.ToList().Find(e => e.accountNumber == retailLoan.Account) : null;
                    //string availableBalance = balanceEnquiryDetails != null ? balanceEnquiryDetails.availableBalance.ToString() : "";
                    new Thread(() =>
                    {
                        genericBLServiceHelper.sendMail(_genServ, customerDataNotFromBvn.Email, "TrustBanc Loan",
                        $@"Dear {usr.Firstname.ToUpper()} {usr.LastName}, your retail loan attempt is successful.We shall revert in the shortest possible time");
                    }).Start();
                    new Thread(() =>
                    {
                        genericBLServiceHelper.sendMail(_genServ,_settings.CustomerServiceEmail, "TrustBanc Loan",
                        $@"<p>The customer {usr.Firstname.ToUpper()} {usr.LastName} with a customerid {usr.CustomerId} and Account Number {retailLoan.Account} has booked a retail Loan
                         and below are the details submitted</p>:
                        <p>ApplicationServiceNumber: {retailLoan.ApplicationServiceNumber}</p>
                        <p>DesiredLoanAmount: {retailLoan.DesiredLoanAmount}</p>
                        <p>Account: {retailLoan.Account}</p>
                        <p>LoanPurpose: {retailLoan.LoanPurpose}</p>
                        <p>Employer: {retailLoan.Employer}</p>
                        <p>Email: {customerDataNotFromBvn.Email}</p>
                        <p>Phone Number: {customerDataNotFromBvn.PhoneNumber}</p>
                        <p>Agreement: {retailLoan.Agreement}</p>
                        ");
                    }).Start();
                    return new GenericResponse() { Response = EnumResponse.Successful, Success = true };
                }
            }
            catch (Exception ex)
            {
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> FirstEvaluation(FirstEvaluation firstEvaluation)
        {
            try
            {
                //var session  = await task1;
                var requestobject = new
                {
                    username = _settings.LoanSessionUsername,
                    password = _settings.LoanSessionPassword,
                    ipAddress = "",
                    appId = 0
                };
                var session = await _genServ.CallServiceAsyncToString(RestSharp.Method.POST,_settings.LoanSessionUrl, requestobject, true);
                var settings = new JsonSerializerSettings
                {
                    MissingMemberHandling = MissingMemberHandling.Ignore
                };
                SessionResponse sessionResponse = JsonConvert.DeserializeObject<SessionResponse>(session, settings);
                firstEvaluation.session = sessionResponse.session;
                firstEvaluation.username = _settings.sessionusername;
                if (firstEvaluation.requestType == 1 && firstEvaluation.productType == 3)
                {
                    if (!string.IsNullOrEmpty(firstEvaluation.employeeNumber))
                    {
                        Console.WriteLine("firstEvaluation ..." + JsonConvert.SerializeObject(firstEvaluation));
                        string response = await _genServ.CallServiceAsyncToString(RestSharp.Method.POST, _settings.firstevaluation, firstEvaluation, true);
                        FirstEvaluationResponse firstEvaluationResponse = JsonConvert.DeserializeObject<FirstEvaluationResponse>(response);
                        return new BillPaymentResponse() { Response = firstEvaluationResponse.response == 116 ? EnumResponse.Successful : EnumResponse.NotSuccessful, Data = firstEvaluationResponse, Success = firstEvaluationResponse.success, Message = firstEvaluationResponse.message };

                    }
                    else
                        return new BillPaymentResponse() { Response = EnumResponse.Successful, Success = false, Message = "employeeNumber cannot be empty" };

                }
                else if ((firstEvaluation.requestType == 2 && firstEvaluation.productType == 3) || (firstEvaluation.requestType == 3 && firstEvaluation.productType == 3))
                {
                    if (!string.IsNullOrEmpty(firstEvaluation.nuban) && !string.IsNullOrEmpty(firstEvaluation.employeeNumber) && !string.IsNullOrEmpty(firstEvaluation.bvn))
                    {
                        string response = await _genServ.CallServiceAsyncToString(RestSharp.Method.POST, _settings.firstevaluation, firstEvaluation, true);
                        FirstEvaluationResponse firstEvaluationResponse = JsonConvert.DeserializeObject<FirstEvaluationResponse>(response);
                        return new BillPaymentResponse() { Response = firstEvaluationResponse.response == 116 ? EnumResponse.Successful : EnumResponse.NotSuccessful, Data = firstEvaluationResponse, Success = firstEvaluationResponse.success, Message = firstEvaluationResponse.message };
                    }
                    else
                    {
                        return new BillPaymentResponse() { Response = EnumResponse.Successful, Success = false, Message = "nuban and employeeNumber cannot be empty" };
                    }
                }
                else if (firstEvaluation.requestType == 2 && firstEvaluation.productType != 3)
                {
                    if (!string.IsNullOrEmpty(firstEvaluation.bvn) && !string.IsNullOrEmpty(firstEvaluation.employeeNumber))
                    {
                        string response = await _genServ.CallServiceAsyncToString(RestSharp.Method.POST, _settings.firstevaluation, firstEvaluation, true);
                        FirstEvaluationResponse firstEvaluationResponse = JsonConvert.DeserializeObject<FirstEvaluationResponse>(response);
                        return new BillPaymentResponse() { Response = firstEvaluationResponse.response == 116 ? EnumResponse.Successful : EnumResponse.NotSuccessful, Data = firstEvaluationResponse, Success = firstEvaluationResponse.success, Message = firstEvaluationResponse.message };
                    }
                    else
                    {
                        return new BillPaymentResponse() { Response = EnumResponse.Successful, Success = false, Message = "nuban and employeeNumber cannot be empty" };
                    }
                }
                else if ((firstEvaluation.requestType == 1 && firstEvaluation.productType != 3))
                {
                    if (!string.IsNullOrEmpty(firstEvaluation.bvn))
                    {
                        string response = await _genServ.CallServiceAsyncToString(RestSharp.Method.POST, _settings.firstevaluation, firstEvaluation, true);
                        FirstEvaluationResponse firstEvaluationResponse = JsonConvert.DeserializeObject<FirstEvaluationResponse>(response);
                        return new BillPaymentResponse() { Response = firstEvaluationResponse.response == 116 ? EnumResponse.Successful : EnumResponse.NotSuccessful, Data = firstEvaluationResponse, Success = firstEvaluationResponse.success, Message = firstEvaluationResponse.message };
                    }
                    else
                    {
                        return new BillPaymentResponse() { Response = EnumResponse.Successful, Success = false, Message = "bvn cannot be empty" };
                    }
                }
                return new BillPaymentResponse() { Response = EnumResponse.NotSuccessful, Success = false };
            }
            catch (Exception ex)
            {
                _logger.LogInformation("" + ex.Message);
                return new GenericResponse() { Success = false, Response = EnumResponse.NotSuccessful, Message = ex.Message };
            }
            // return null;
        }

        public async Task<GenericResponse> GetLoanRequestTypes()
        {
            try
            {
                string response = await _genServ.CallServiceAsyncToString(RestSharp.Method.GET, _settings.LoanRequestTypes, null, true);
                List<LoanRequestTypes> loanRequestTypes = JsonConvert.DeserializeObject<List<LoanRequestTypes>>(response);
                return new BillPaymentResponse() { Response = EnumResponse.Successful, Data = loanRequestTypes, Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogInformation("" + ex.Message);
                return new GenericResponse() { Success = false, Response = EnumResponse.NotSuccessful, Message = ex.Message };
            }
            // return null;
        }

        public async Task<GenericResponse> GetLoanTypes()
        {
            try
            {
                string response = await _genServ.CallServiceAsyncToString(RestSharp.Method.GET, _settings.LoanTypes, null, true);

                return new BillPaymentResponse() { Response = EnumResponse.Successful, Data = JsonConvert.DeserializeObject<List<LoanTypesResponse>>(response), Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogInformation("" + ex.Message);
                return new GenericResponse() { Success = false, Response = EnumResponse.NotSuccessful, Message = ex.Message };
            }
            // return null;
        }

        public async Task<GenericResponse> SecondEvaluation(SecondEvaluation secondEvaluation)
        {
            try
            {
                // Console.WriteLine("secondevaluation " + _settings.secondevaluation);
                _logger.LogInformation("secondevaluation " + _settings.secondevaluation);
                string response = await _genServ.CallServiceAsyncToString(RestSharp.Method.POST, _settings.secondevaluation, secondEvaluation, true);
                //  Console.WriteLine("response "+response);
                _logger.LogInformation("response " + response);
                SecondEvaluationResponse secondEvaluationResponse = JsonConvert.DeserializeObject<SecondEvaluationResponse>(response);
                return new BillPaymentResponse() { Response = secondEvaluationResponse.response == 116 ? EnumResponse.Successful : EnumResponse.NotSuccessful, Success = secondEvaluationResponse.success, Data = secondEvaluationResponse, Message = secondEvaluationResponse.responseMessage };
            }
            catch (Exception ex)
            {
                _logger.LogInformation($"{ex.Message}");
                return new GenericResponse() { Success = false, Response = EnumResponse.NotSuccessful, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> SubmitEvaluation(SubmitLoanEvaluation submitLoanEvaluation)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(submitLoanEvaluation.MyUsername, submitLoanEvaluation.MySession, submitLoanEvaluation.ChannelId, con);
                    if (!validateSession)
                        return new TransResponse() { Response = EnumResponse.InvalidSession };
                    var task1 = Task.Run(async () =>
                {
                    var requestobject = new
                    {
                        username = _settings.sessionusername,
                        password = _settings.sessionpassword,
                        ipAddress = "",
                        appId = 0
                    };
                    _logger.LogInformation($"requestobject {requestobject}");
                    string session = await _genServ.CallServiceAsyncToString(RestSharp.Method.POST, _settings.SessionUrl, requestobject, true);
                    return session;
                });
                    var session = await task1;
                    var settings = new JsonSerializerSettings
                    {
                        MissingMemberHandling = MissingMemberHandling.Ignore
                    };
                    _logger.LogInformation("session " + session);
                    SessionResponse sessionResponse = JsonConvert.DeserializeObject<SessionResponse>(session, settings);
                    submitLoanEvaluation.session = sessionResponse.session;
                    submitLoanEvaluation.username = _settings.sessionusername;
                    var requestobject = new
                    {
                        session = submitLoanEvaluation.session,
                        username = submitLoanEvaluation.username,
                        requestId = submitLoanEvaluation.requestId,
                        bvn = submitLoanEvaluation.bvn,
                        accountNumber = submitLoanEvaluation.accountNumber
                    };
                    string response = await _genServ.CallServiceAsyncToString(RestSharp.Method.POST, _settings.SubmitLoanEvaluation, requestobject, true);
                    _logger.LogInformation($"SubmitLoanEvaluationResponse {response}");
                    SubmitLoanEvaluationResponse submitLoanEvaluationResponse = JsonConvert.DeserializeObject<SubmitLoanEvaluationResponse>(response);
                    if (submitLoanEvaluationResponse.success)
                    {
                        // send sms alert 
                        var task2 = Task.Run(async () =>
                        {
                            var gen = await _smsBLService.SendSmsNotificationToCustomer("Loan Submission",
                                _settings.SamplePhoneNumber, $"Your Public Loan Submission of amount is Successful", "Loan");
                        });

                    }
                    //return submitLoanEvaluationResponse;
                    return new BillPaymentResponse() { Response = submitLoanEvaluationResponse.response == 116 ? EnumResponse.Successful : EnumResponse.NotSuccessful, Data = submitLoanEvaluationResponse, Message = submitLoanEvaluationResponse.responseMessage };
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation($"{ex.Message}");
                return new GenericResponse() { Success = false, Response = EnumResponse.NotSuccessful, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> ViewEvaluation(PublicLoanEvaluation publicLoanEvaluation)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(publicLoanEvaluation.cusId, publicLoanEvaluation.MySession, publicLoanEvaluation.ChannelId, con);
                    if (!validateSession)
                        return new TransResponse() { Response = EnumResponse.InvalidSession };
                    var task1 = Task.Run(async () =>
                {
                    var requestobject = new
                    {
                        username = _settings.sessionusername,
                        password = _settings.sessionpassword,
                        ipAddress = "",
                        appId = 0
                    };
                    _logger.LogInformation($"requestobject {requestobject}");
                    string session = await _genServ.CallServiceAsyncToString(RestSharp.Method.POST, _settings.SessionUrl, requestobject, true);
                    return session;
                });
                    var session = await task1;
                    var settings = new JsonSerializerSettings
                    {
                        MissingMemberHandling = MissingMemberHandling.Ignore
                    };
                    SessionResponse sessionResponse = JsonConvert.DeserializeObject<SessionResponse>(session, settings);
                    publicLoanEvaluation.session = sessionResponse.session;
                    publicLoanEvaluation.username = _settings.sessionusername;
                    var requestobject = new
                    {
                        username = publicLoanEvaluation.username,
                        session = publicLoanEvaluation.session,
                        cusId = publicLoanEvaluation.cusId,
                        employeeNumber = publicLoanEvaluation.employeeNumber,
                        startDate = publicLoanEvaluation.startDate,
                        endDate = publicLoanEvaluation.endDate
                    };
                    string response = await _genServ.CallServiceAsyncToString(RestSharp.Method.POST, _settings.publicLoanEvaluation, requestobject, true);
                    _logger.LogInformation($"PublicloanEvaluation {response}");
                    PublicLoanEvaluationResponse publicLoanEvaluationResponse = JsonConvert.DeserializeObject<PublicLoanEvaluationResponse>(response, settings);
                    if(!publicLoanEvaluationResponse.evaluations.Any()) {
                       // List<PublicLoanEvaluationResponse> list = new List<PublicLoanEvaluationResponse>();
                        // return new BillPaymentResponse() { Response = EnumResponse.Successful, Success = true, Data = new { evaluation=Enu)} };
                        publicLoanEvaluationResponse.Success = true;
                        publicLoanEvaluationResponse.Response = EnumResponse.Successful;
                        return publicLoanEvaluationResponse;
                      }
                    return publicLoanEvaluationResponse;
                    //return new BillPaymentResponse() { Response = publicLoanEvaluationResponse.Response, Success = publicLoanEvaluationResponse.Success, Data = publicLoanEvaluationResponse };
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation($"{ex.Message}");
                return new GenericResponse() { Success = false, Response = EnumResponse.NotSuccessful, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> ViewLoanHistory(string session, string userName, int channelId)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var usr = await _genServ.GetUserbyUsername(userName,con);
                    var validateSession = await _genServ.ValidateSession(userName,session,channelId, con);
                    if (!validateSession)
                        return new TransResponse() { Response = EnumResponse.InvalidSession };
                    var resp = await _genServ.CallServiceAsyncToString(Method.GET, $"{_settings.FinedgeUrl}api/Enquiry/FetchCustomerLoan/" + "709221", null, true); // remember to pass the user customerid
                    Console.WriteLine("resp " + resp);
                    GenericResponse2 genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(resp);
                    Console.WriteLine(JsonConvert.SerializeObject(genericResponse2.data));
                    if (genericResponse2.data == null || resp == "")
                    {
                        return new GenericResponse() { Success = true, Response = EnumResponse.NoRecordFound };
                    }
                    CustomerLoanResponse list = JsonConvert.DeserializeObject<CustomerLoanResponse>(JsonConvert.SerializeObject(genericResponse2.data));
                    if (list.Status.Equals("Successful", StringComparison.OrdinalIgnoreCase))
                    {
                        return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = list };
                    }
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.NoRecordFound };
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation($"{ex.Message}");
                return new GenericResponse() { Success = false, Response = EnumResponse.NotSuccessful, Message = ex.Message };
            }
        }

    }
}

































































































