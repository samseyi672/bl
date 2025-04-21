using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.Services
{
    public class UssdServices : IUssd
    {
        private readonly ILogger<UssdServices> _logger;
        private readonly AppSettings _settings;
        private readonly IGeneric _genServ;
        private readonly ITransfer _trnServ;
        private readonly IMemoryCache _cache;
        private readonly DapperContext _context;

        public UssdServices(ILogger<UssdServices> logger, IOptions<AppSettings> options, IGeneric genServ, ITransfer trnServ, IMemoryCache memoryCache, DapperContext context)
        {
            _logger = logger;
            _settings = options.Value;
            _genServ = genServ;
            _cache = memoryCache;
            _context = context;
            _trnServ = trnServ;
        }

        public async Task<GenericLoginResponse> StartUssd(StartUssdRequest Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var usr = await _genServ.GetUserbyPhone(Request.PhoneNumber, con);
                    if (usr == null)
                    {
                        await _genServ.InsertLogs(0, "", Request.PhoneNumber, Request.Network, $"Ussd Phone Number - {EnumResponse.PhoneNotRegistered} on {Request.PhoneNumber}", con);
                        return new GenericLoginResponse() { Response = EnumResponse.PhoneNotRegistered };
                    }

                    if (usr.Status == 2)
                    {
                        await _genServ.InsertLogs(usr.Id, "", Request.PhoneNumber, Request.Network, $"{EnumResponse.InActiveProfile} on {Request.PhoneNumber}", con);
                        return new GenericLoginResponse() { Response = EnumResponse.InActiveProfile };
                    }

                    string sess = _genServ.GetSession();
                    await _genServ.SetUserSession(usr.Id, sess, 2, con);
                    await _genServ.InsertLogs(usr.Id, sess, Request.PhoneNumber, Request.Network, $"Login Successful", con);
                    return new GenericLoginResponse()
                    {
                        SessionID = sess,
                        Response = EnumResponse.Successful,
                        Success = true
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericLoginResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<FetchAccounts> GetBalance(GenericUssdTransRequest Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var usr = await _genServ.GetUserbyPhone(Request.PhoneNumber, con);
                    if (usr == null)
                    {
                        await _genServ.InsertLogs(0, "", Request.PhoneNumber, Request.Network, $"Ussd Phone Number - {EnumResponse.PhoneNotRegistered} on {Request.PhoneNumber}", con);
                        return new FetchAccounts() { Response = EnumResponse.PhoneNotRegistered };
                    }

                    var validateSession = await _genServ.ValidateSession(usr.Id, Request.Session, 2, con);
                    if (!validateSession)
                    {
                        await _genServ.InsertLogs(usr.Id, Request.Session, Request.PhoneNumber, Request.Network, $"{EnumResponse.InActiveProfile} on {Request.PhoneNumber}", con);
                        return new FetchAccounts() { Response = EnumResponse.InvalidSession };
                    }

                    var credTin = await _genServ.GetUserCredential(CredentialType.TransactionPin, usr.Id, con);
                    string enterpass = _genServ.EncryptString(Request.TransPin);
                    if (credTin != enterpass)
                    {
                        await _genServ.InsertLogs(usr.Id, Request.Session, Request.PhoneNumber, Request.Network, $"{EnumResponse.InvalidTransactionPin} on {Request.PhoneNumber}", con);
                        return new FetchAccounts() { Response = EnumResponse.InvalidTransactionPin };
                    }

                    var accts = new List<AccountDetails>();
                    var resp = await _genServ.GetAccountbyCustomerId(usr.CustomerId);

                    if (resp.success && resp.balances.Any())
                    {
                        foreach (var n in resp.balances)
                            accts.Add(new AccountDetails()
                            {
                                AccountClass = n.productname,
                                AccountNumber = n.accountNumber,
                                AvailableBalance = Math.Round(n.availableBalance, 2),
                                LedgerBalance = Math.Round(n.totalBalance, 2)
                            });

                        await _genServ.InsertLogs(usr.Id, Request.Session, Request.PhoneNumber, Request.Network, $"{EnumResponse.Successful} on {Request.PhoneNumber}", con);
                        return new FetchAccounts()
                        {
                            Accounts = accts,
                            Response = EnumResponse.Successful,
                            Success = true,
                            //AirtimeBills = await _genServ.GetAirtimeBillsLimit(usr.RequestReference, con)
                        };
                    }

                    return new FetchAccounts() { Response = EnumResponse.NoAccountExist };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new FetchAccounts() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> Transfer(UssdTransferRequest Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var usr = await _genServ.GetUserbyPhone(Request.PhoneNumber, con);
                    var validateSession = await _genServ.ValidateSession(usr.Id, Request.Session, 2, con);
                    if (!validateSession)
                    {
                        await _genServ.InsertLogs(usr.Id, Request.Session, Request.PhoneNumber, Request.Network, $"{EnumResponse.InActiveProfile} on {Request.PhoneNumber}", con);
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    }

                    var resp = await _genServ.GetAccountbyCustomerId(usr.CustomerId);
                    if (!resp.success)
                        return new GenericResponse() { Response = EnumResponse.NoAccountExist };

                    var request = new TransferRequestSingle()
                    {
                        Amount = Request.Amount,
                        ChannelId = 2,
                        DestinationAccountNo = Request.AccountNumber,
                        DestinationBankCode = Request.BankCode,
                        Session = Request.Session,
                        TransactionRef = Request.TransRef,
                        TransPin = Request.TransPin,
                        Narration = "USSD Transfer Request",
                        SourceAccountNo = resp.balances?.FirstOrDefault().accountNumber
                    };
                    var transResponse = await _trnServ.TransferFunds(request, con, usr);
                    return new GenericResponse() { Response = transResponse.Response, Success = transResponse.Success };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericNameEnquiryReponse> NameEnquiry(NameEnquiry Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var usr = await _genServ.GetUserbyPhone(Request.PhoneNumber, con);
                    var validateSession = await _genServ.ValidateSession(usr.Id, Request.Session, 2, con);
                    if (!validateSession)
                    {
                        await _genServ.InsertLogs(usr.Id, Request.Session, Request.PhoneNumber, Request.Network, $"{EnumResponse.InActiveProfile} on {Request.PhoneNumber}", con);
                        return new GenericNameEnquiryReponse() { Response = EnumResponse.InvalidSession };
                    }

                    if (string.IsNullOrEmpty(Request.BankCode))
                        Request.BankCode = _settings.TrustBancBankCode;
                    var validate = await _genServ.ValidateNumberOnly(Request.AccountNumber, Request.BankCode);
                    return new GenericNameEnquiryReponse()
                    {
                        Success = validate.Success,
                        AccountName = validate.AccountName,
                        Response = validate.Success ? EnumResponse.Successful : EnumResponse.InvalidAccount
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericNameEnquiryReponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }
    }
}
