using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using Dapper;
using System.Linq;
using System.Data;
using System;
using System.Threading.Tasks;
using RestSharp;
using MySql.Data.MySqlClient;
using Retailbanking.Common.DbObj;
using System.Collections.Generic;

namespace Retailbanking.BL.Services
{
    public class AirtimeBillsServices : IAirtimeBills
    {
        private readonly ILogger<AirtimeBillsServices> _logger;
        private readonly AppSettings _settings;
        private readonly SmtpDetails smtpDetails;
        private readonly IGeneric _genServ;
        private readonly IMemoryCache _cache;
        private readonly IBeneficiary _benServ;
        private readonly DapperContext _context;

        public AirtimeBillsServices(ILogger<AirtimeBillsServices> logger, IOptions<AppSettings> options, IOptions<SmtpDetails> options1, IGeneric genServ, IBeneficiary benServ, IMemoryCache memoryCache, DapperContext context)
        {
            _logger = logger;
            _settings = options.Value;
            smtpDetails = options1.Value;
            _genServ = genServ;
            _cache = memoryCache;
            _benServ = benServ;
            _context = context;
        }

        public async Task<AirtimeCodes> GetAirtimeCodes()
        {
            try
            {
                var myData = new List<GetAirtimeCode>();
                if (!_cache.TryGetValue(CacheKeys.NetworkCodes, out myData))
                {
                    // Key not in cache, so get data.
                    myData = await _genServ.CallServiceAsync<List<GetAirtimeCode>>(Method.GET, $"{_settings.AirtimeUrl}api/airtime/GetNetwork", null);
                    // Set cache options.
                    var cacheEntryOptions = new MemoryCacheEntryOptions()
                        // Keep in cache for this time, reset time if accessed.
                        .SetSlidingExpiration(TimeSpan.FromDays(1));

                    // Save data in cache.
                    _cache.Set(CacheKeys.NetworkCodes, myData, cacheEntryOptions);
                }
                return new AirtimeCodes() { Codes = myData, Success = myData.Any(), Response = myData.Any() ? EnumResponse.Successful : EnumResponse.NotSuccessful };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new AirtimeCodes() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<List<GetProducts>> GetBillerProducts(long BillerId) => await _genServ.CallServiceAsync<List<GetProducts>>(Method.GET, $"{_settings.AirtimeUrl}api/bills/GetProduct/{BillerId}", null);

        public async Task<List<GetList>> GetBillers(long CategoryId) => await _genServ.CallServiceAsync<List<GetList>>(Method.GET, $"{_settings.AirtimeUrl}api/bills/GetBillerbyCategory/{CategoryId}", null);

        public async Task<List<GetList>> GetCategories() => await _genServ.CallServiceAsync<List<GetList>>(Method.GET, $"{_settings.AirtimeUrl}api/bills/GetBillCategory", null);

        public async Task<AirtimeBillsLimit> GetAirtimeBillsLimit(string PhoneNumber, int ChannelId)
        {
            try
            {
                if (string.IsNullOrEmpty(PhoneNumber))
                    return new AirtimeBillsLimit();

                using (IDbConnection con = _context.CreateConnection())
                {
                    var chkphon = await _genServ.CheckIfUserIsLoggedIn(PhoneNumber, 1, con);
                    if (!chkphon)
                        return new AirtimeBillsLimit();

                    var getUser = await _genServ.GetUserbyPhone(PhoneNumber, con);
                    if (getUser == null)
                        return new AirtimeBillsLimit();

                    return await _genServ.GetAirtimeBillsLimit(getUser.Id, con);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new AirtimeBillsLimit();
            }
        }

        public async Task<TransResponse> MakeAirtime(AirtimeRequest Request)
        {
            try
            {
                if (string.IsNullOrEmpty(Request.TransactionRef) || Request.Amount == 0)
                    return new TransResponse() { Success = false, Response = EnumResponse.TransError };

                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Request.Username, Request.Session, Request.ChannelId, con);
                    if (!validateSession)
                        return new TransResponse() { Response = EnumResponse.InvalidSession };

                    var chktrn = await con.QueryAsync<long>($"select id from airtime where transaction_ref =@trnref", new { trnref = Request.TransactionRef });
                    if (chktrn.Any())
                        return new TransResponse() { Response = EnumResponse.DuplicateRecoundsFound };

                    var channels = await _genServ.GetClientCredentials();
                    //if (!channels.Any(x => x.Ch == ChannelId))
                    //    return new TransResponse() { Response = EnumResponse.ChannelError };

                    var usr = await _genServ.GetUserbyUsername(Request.Username, con);
                    if (Request.SaveBeneficiary)
                    {
                        var savebn = new BeneficiaryModel()
                        {
                            Name = "",
                            Value = Request.Receiver,
                            ServiceName = Request.Network,
                            Code = Request.Networkcode,
                            BeneficiaryType = 2
                        };
                        await _benServ.SaveBeneficiary(usr.Id, savebn, con, BeneficiaryType.Airtime);
                    };

                    string sql = $@"insert into airtime (user_id, source_account, transaction_ref,networkname, networkcode, receiver, amount,channel_id,session, createdon)
                        values ({usr.Id},@sour,@trnRef,@ntname,@ntcode,@rec,{Request.Amount},{Request.ChannelId},@sess,sysdate())";
                    await con.ExecuteAsync(sql, new
                    {
                        sour = Request.SourceAccountNo,
                        trnRef = Request.TransactionRef,
                        ntname = Request.Network,
                        ntcde = Request.Networkcode,
                        rec = Request.Receiver,
                        sess = Request.Session
                    });

                    var gettrans = await con.QueryAsync<long>($"select id from airtime where transaction_ref =@trnref", new { trnref = Request.TransactionRef });
                    long transId = gettrans.FirstOrDefault();
                    var accts = await _genServ.GetAccountbyCustomerId(usr.CustomerId);

                    if (!accts.success || !accts.balances.Any(x => x.accountNumber == Request.SourceAccountNo))
                    {
                        await UpdateTrans(transId, con, false, "", EnumResponse.SuspectedFraud.ToString(), "");
                        return new TransResponse() { Response = EnumResponse.SuspectedFraud };
                    }

                    var usrDev = await _genServ.GetActiveMobileDevice(usr.Id, con);
                    if (Request.ChannelId == 1 && Request.DeviceId != usrDev.Device)
                    {
                        await UpdateTrans(transId, con, false, "", EnumResponse.DeviceNotRegistered.ToString(), "");
                        return new TransResponse() { Response = EnumResponse.DeviceNotRegistered };
                    }

                    var transPin = await _genServ.GetUserCredential(CredentialType.TransactionPin, usr.Id, con);
                    string enterpin = _genServ.EncryptString(Request.TransPin);
                    if (enterpin != transPin)
                    {
                        await UpdateTrans(transId, con, false, "", EnumResponse.InvalidTransactionPin.ToString(), "");
                        return new TransResponse() { Response = EnumResponse.InvalidTransactionPin };
                    }

                    var getUserlimits = await _genServ.GetUserLimits(usr.Id, con, BeneficiaryType.Airtime);
                    if (getUserlimits.AvailableLimit < (decimal)Request.Amount)
                    {
                        await UpdateTrans(transId, con, false, "", EnumResponse.DailyLimitExceed.ToString(), "");
                        return new TransResponse() { Response = EnumResponse.DailyLimitExceed };
                    }

                    var req = new CreditSwitchRequest()
                    {
                        Amount = Request.Amount,
                        ClientKey = _settings.AirtimeKey,
                        NetworkCode = Request.Networkcode,
                        NetworkName = Request.Network,
                        Receiver = Request.Receiver,
                        SourceAccount = Request.SourceAccountNo,
                        TransRef = Request.TransactionRef
                    };

                    var resp = await _genServ.CallServiceAsync<CreditSwitchResponse>(Method.POST, $"{_settings.AirtimeUrl}api/airtime/BuyAirtime", req, true);
                    await UpdateTrans(transId, con, true, resp.PostingId, resp.Message, resp.PostingId);
                    return new TransResponse()
                    {
                        Success = resp.Success,
                        TransId = resp.PostingId,
                        Response = resp.Success ? EnumResponse.Successful : EnumResponse.NotSuccessful,
                        Message = resp.Message
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new TransResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<TransResponse> MakeBillPayment(BillsRequest Request)
        {
            try
            {
                if (string.IsNullOrEmpty(Request.TransactionRef) || Request.Amount == 0)
                    return new TransResponse() { Success = false, Response = EnumResponse.TransError };

                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Request.Username, Request.Session, Request.ChannelId, con);
                    if (!validateSession)
                        return new TransResponse() { Response = EnumResponse.InvalidSession };

                    var chktrn = await con.QueryAsync<long>($"select id from bills where transaction_ref =@trnref", new { trnref = Request.TransactionRef });
                    if (chktrn.Any())
                        return new TransResponse() { Response = EnumResponse.DuplicateRecoundsFound };

                    var channels = await _genServ.GetClientCredentials();
                    if (!channels.Any(x => x.ClientId == Request.ChannelId))
                        return new TransResponse() { Response = EnumResponse.ChannelError };

                    var usr = await _genServ.GetUserbyUsername(Request.Username, con);
                    var getProdDetails = await _genServ.CallServiceAsync<GetBillerDetails>(Method.GET, $"{_settings.AirtimeUrl}api/bills/GetBillerDetails/{Request.ProductId}", null);

                    if (Request.SaveBeneficiary)
                    {
                        var savebn = new BeneficiaryModel()
                        {
                            Name = "",
                            Value = Request.Reference,
                            ServiceName = getProdDetails.Category,
                            Code = getProdDetails.ProductCode,
                            BeneficiaryType = 3,
                            BillerProduct = getProdDetails.Product,
                            BillerService = getProdDetails.Biller
                        };
                        await _benServ.SaveBeneficiary(usr.Id, savebn, con, BeneficiaryType.Bills);
                    };

                    string sql = $@"insert into bills (user_id, source_account, transaction_ref,productid, category,biller, product, ReferenceValue, amount,channel_id,session, createdon)
                        values ({usr.Id},@sour,@trnRef,{Request.ProductId},@cat,@biller,@prod,@refnum,{Request.Amount},{Request.ChannelId},@sess,sysdate())";
                    await con.ExecuteAsync(sql, new
                    {
                        sour = Request.SourceAccountNo,
                        trnRef = Request.TransactionRef,
                        cat = getProdDetails.Category,
                        biller = getProdDetails.Biller,
                        prod = getProdDetails.Product,
                        refnum = Request.Reference,
                        sess = Request.Session
                    });

                    var gettrans = await con.QueryAsync<long>($"select id from bills where transaction_ref =@trnref", new { trnref = Request.TransactionRef });
                    long transId = gettrans.FirstOrDefault();
                    var accts = await _genServ.GetAccountbyCustomerId(usr.CustomerId);

                    if (!accts.success || !accts.balances.Any(x => x.accountNumber == Request.SourceAccountNo))
                    {
                        await UpdateTrans(transId, con, false, "", EnumResponse.SuspectedFraud.ToString(), "", false);
                        return new TransResponse() { Response = EnumResponse.SuspectedFraud };
                    }

                    var usrDev = await _genServ.GetActiveMobileDevice(usr.Id, con);
                    if (Request.ChannelId == 1 && Request.DeviceId != usrDev.Device)
                    {
                        await UpdateTrans(transId, con, false, "", EnumResponse.DeviceNotRegistered.ToString(), "", false);
                        return new TransResponse() { Response = EnumResponse.DeviceNotRegistered };
                    }

                    var transPin = await _genServ.GetUserCredential(CredentialType.TransactionPin, usr.Id, con);
                    string enterpin = _genServ.EncryptString(Request.TransPin);
                    if (enterpin != transPin)
                    {
                        await UpdateTrans(transId, con, false, "", EnumResponse.InvalidTransactionPin.ToString(), "", false);
                        return new TransResponse() { Response = EnumResponse.InvalidTransactionPin };
                    }

                    var getUserlimits = await _genServ.GetUserLimits(usr.Id, con, BeneficiaryType.Bills);
                    if (getUserlimits.AvailableLimit < (decimal)Request.Amount)
                    {
                        await UpdateTrans(transId, con, false, "", EnumResponse.DailyLimitExceed.ToString(), "", false);
                        return new TransResponse() { Response = EnumResponse.DailyLimitExceed };
                    }

                    var req = new MakePaymentforBill()
                    {
                        Amount = Request.Amount,
                        ClientKey = _settings.AirtimeKey,
                        Reference = Request.Reference,
                        ProductId = Request.ProductId,
                        Charge = Request.Charge,
                        VAT = Request.Vat,
                        SourceAccount = Request.SourceAccountNo,
                        TransRef = Request.TransactionRef
                    };

                    var resp = await _genServ.CallServiceAsync<CreditSwitchResponse>(Method.POST, $"{_settings.AirtimeUrl}api/bills/MakePaymentforBills", req, true);
                    await UpdateTrans(transId, con, true, resp.PostingId, resp.Message, resp.PostingId, false);
                    return new TransResponse()
                    {
                        Success = resp.Success,
                        TransId = resp.PostingId,
                        Response = resp.Success ? EnumResponse.Successful : EnumResponse.NotSuccessful,
                        Message = resp.Message
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new TransResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<FetchDetailsResponse> ValidateReference(FetchDetails Request)
        {
            try
            {
                if (string.IsNullOrEmpty(Request.ReferenceNumber))
                    return new FetchDetailsResponse() { Response = EnumResponse.TransError };

                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Request.Username, Request.Session, Request.ChannelId, con);
                    if (!validateSession)
                        return new FetchDetailsResponse() { Response = EnumResponse.InvalidSession };

                    var resp = await _genServ.CallServiceAsync<FetchDetailsResponse>(Method.POST, $"{_settings.AirtimeUrl}api/bills/ValidateDetails", Request, true);
                    if (resp == null)
                        return new FetchDetailsResponse() { Response = EnumResponse.SystemError };
                    resp.Response = EnumResponse.Successful;
                    return resp;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new FetchDetailsResponse() { Response = EnumResponse.SystemError, ReferenceNumber = Request.ReferenceNumber };
            }
        }

        private async Task UpdateTrans(long Id, IDbConnection con, bool Success, string ResponseCode, string ResponseMessage, string TransId, bool Airtime = true)
        {
            try
            {
                string sql = $"update {(Airtime ? "airtime" : "bills")} set success = {(Success ? 1 : 0)}, responsecode = @rspcode,responsemessage = @rspmsg, PostingId= @trns where id = {Id}";
                await con.ExecuteAsync(sql, new { rspcode = ResponseCode, rspmsg = ResponseMessage, trns = TransId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
            }
        }
    }
}
