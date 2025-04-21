using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using MySqlX.XDevAPI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Asn1.Ocsp;
using Org.BouncyCastle.Ocsp;
using RestSharp;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using Retailbanking.Common.DbObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using static Retailbanking.BL.Services.TransferServices;

namespace Retailbanking.BL.Services
{
    public class TransferServices : ITransfer
    {
        private readonly ILogger<TransferServices> _logger;
        private readonly AppSettings _settings;
        private readonly IGeneric _genServ;
        private readonly IBeneficiary _benServ;
        private readonly IMemoryCache _cache;
        private readonly DapperContext _context;
        private const string Seed = "373373373373";
        private const int NubanLength = 10;
        private const int SerialNumLength = 9;
        private string error;
        private readonly ISmsBLService _smsBLService;
        private readonly INotification _notification;
        private readonly Tier1AccountLimitInfo _tier1AccountLimitInfo;
        private readonly Tier2AccountLimitInfo _tier2AccountLimitInfo;
        private readonly Tier3AccountLimitInfo _tier3AccountLimitInfo;
        private readonly AccountChannelLimit _accountChannelLimit;

        public TransferServices(IOptions<AccountChannelLimit> accountChannelLimit, IOptions<Tier3AccountLimitInfo> tier3AccountLimitInfo, IOptions<Tier2AccountLimitInfo> tier2AccountLimitInfo, IOptions<Tier1AccountLimitInfo> tier1AccountLimitInfo, ILogger<TransferServices> logger, IOptions<AppSettings> options, IGeneric genServ, IMemoryCache memoryCache, IBeneficiary benServ, DapperContext context, ISmsBLService smsBLService, INotification notification)
        {
            _logger = logger;
            _settings = options.Value;
            _genServ = genServ;
            _cache = memoryCache;
            _benServ = benServ;
            _context = context;
            _smsBLService = smsBLService;
            _notification = notification;
            _tier1AccountLimitInfo = tier1AccountLimitInfo.Value;
            _tier2AccountLimitInfo = tier2AccountLimitInfo.Value;
            _tier3AccountLimitInfo = tier3AccountLimitInfo.Value;
            _accountChannelLimit = accountChannelLimit.Value;
        }

        public async Task<GetTransactionStatus> GetCustomerInterBankTransactionusStatusForOutwardInService(
         string transRef, string username, int channelId, string session)
        {
            try
            {
                var url = $"{_settings.AccessUrl}AccessOutward/GetTransactionStatusV2/{transRef}";
                var getTransactionStatus = await _genServ.CallServiceAsync<GetTransactionStatus>(Method.GET, url, null, true, null);
                _logger.LogInformation("response getTransactionStatus " +JsonConvert.SerializeObject(getTransactionStatus));
                if (getTransactionStatus != null)
                {
                    getTransactionStatus.StatusRemark = GetStatusRemark(getTransactionStatus.Status);
                }
                _logger.LogInformation("getTransactionStatus " + JsonConvert.SerializeObject(getTransactionStatus));
                return getTransactionStatus;
            }
            catch (Exception ex)
            {
                var errorMessage = $"{ex.Message} {ex.StackTrace}";
                _logger.LogError(errorMessage);
                Console.WriteLine(errorMessage);
                return null;
            }
        }

        /// <summary>
        /// Retrieves the status remark based on the transaction status code.
        /// </summary>
        private string GetStatusRemark(int status)
        {
            //_ => "Unknown status"
            _logger.LogInformation("Remark status " + status);
           // 5 => "Retry/Reversal in progress",
            return status switch
            {
                2 => "Transaction Submitted Successfully",
                4 => "Transaction successful",
                5 => "Transaction processing",
                6 => "Transaction processing",
                7 => "Transaction reversed",
                8 => "Error - Suspected Duplicate, To Be Reversed",
                9 => "Reversed Based on Suspected Duplicate",
                10 => "Single Limit Exceeded",
                11 => "Daily Limit Exceeded",
                _ => "Transaction Submitted Successfully"
            };
        }

        private TransResponse GetInterBankStatusResponse(GetTransactionStatus status)
        {
            _logger.LogInformation("status ...."+status.Status);
           // _ => new TransResponse() { Response = EnumResponse.TransactionError, Message = "Unknown status" }
            return status.Status switch
            {
                2 => new TransResponse() {Response=EnumResponse.InterBankResponseStatus2 ,Message="Transaction Submitted Successfully"},
                4 => new TransResponse() { Response = EnumResponse.InterBankResponseStatus4, Message = "Transaction successful" },
                5 => new TransResponse() { Response = EnumResponse.InterBankResponseStatus5, Message = "Retry/Reversal in progress" },
                6 => new TransResponse() {Response=EnumResponse.InterBankResponseStatus6 ,Message = "Transaction processing" },
                7 => new TransResponse() {Response=EnumResponse.InterBankResponseStatus7, Message="Transaction reversed" },
                8 => new TransResponse() { Response = EnumResponse.InterBankResponseStatus8, Message = "Error - Suspected Duplicate, To Be Reversed"},
                9 => new TransResponse() { Response = EnumResponse.InterBankResponseStatus9, Message = "Reversed Based on Suspected Duplicate"},
                10 => new TransResponse() { Response=EnumResponse.InterBankResponseStatus10,Message = "Single Limit Exceeded"},
                11 => new TransResponse() { Response=EnumResponse.InterBankResponseStatus11,Message = "Daily Limit Exceeded" },
                _ => new TransResponse() {Response=EnumResponse.TransactionError, Message = "Transaction Submitted Successfully" }
            };
        }

        public async Task<GenericResponse2> GetCustomerInterBankTransactionusStatusForOutward(string transRef, string Username, int ChannelId,string Session)
        {
            // GetTransactionStatusV2 / TRST - INT - 034619027497074
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Username,Session,ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse2() { Response = EnumResponse.InvalidSession };
                    GetTransactionStatus getTransactionStatus = await _genServ.CallServiceAsync<GetTransactionStatus>(Method.GET, $"{_settings.AccessUrl}AccessOutward/GetTransactionStatusV2/"+transRef, null, true, null);
                    
                    if (getTransactionStatus != null)
                    {
                        if (getTransactionStatus.Status == 2)
                        {
                           // getTransactionStatus.StatusRemark = "Account debited.Transaction Submitted Suucessfully";
                            getTransactionStatus.StatusRemark = "Transaction processing";
                        }
                        else if (getTransactionStatus.Status == 4)
                        {
                            getTransactionStatus.StatusRemark = "Transaction successful";
                            //update transaction status in transfer 
                            /*
                             var TransactionStatus = (await con.QueryAsync<int?>("SELECT Success from transfer where Transaction_Ref=@Transaction_Ref", new { Transaction_Ref=transRef})).FirstOrDefault();
                             if (TransactionStatus.HasValue&&TransactionStatus==0)
                             {
                                 await con.ExecuteAsync("update transfer set Success=1 where Transaction_Ref=@Transaction_Ref",new {Transaction_Ref=transRef});
                             }
                             */
                            await con.ExecuteAsync("UPDATE transfer SET Success = 1,ResponseMessage=@ResponseMessage WHERE Transaction_Ref = @Transaction_Ref AND Success = 0", new { Transaction_Ref = transRef, ResponseMessage=getTransactionStatus.StatusRemark});
                        }
                        else if (getTransactionStatus.Status == 5)
                        {
                            //getTransactionStatus.StatusRemark = "Account debited but credit failed.Retry/Reversal in progress";
                            getTransactionStatus.StatusRemark = "Transaction processing";
                        }
                        else if (getTransactionStatus.Status == 6)
                        {
                            getTransactionStatus.StatusRemark = "Transaction processing";
                        }
                        else if (getTransactionStatus.Status == 0)
                        {
                            getTransactionStatus.StatusRemark = "Transaction processing";
                        }
                        else if (getTransactionStatus.Status == 12)
                        {
                            getTransactionStatus.StatusRemark = "Transaction failed";
                        }
                        else if (getTransactionStatus.Status == 7)
                        {
                            getTransactionStatus.StatusRemark = "Transaction revered";
                        }
                        else if (getTransactionStatus.Status == 8)
                        {
                            getTransactionStatus.StatusRemark = "Error - Suspected Duplicate, To Be Reversed";
                        }
                        else if (getTransactionStatus.Status == 9)
                        {
                            getTransactionStatus.StatusRemark = "Reversed Based on Suspected Duplicate";
                        }
                        else if (getTransactionStatus.Status == 10)
                        {
                            getTransactionStatus.StatusRemark = "Single Limit Exceeded";
                        }
                        else if (getTransactionStatus.Status == 11)
                        {
                            getTransactionStatus.StatusRemark = "'Daily Limit Exceeded'";
                        }
                        return new GenericResponse2()
                        {
                            Response = EnumResponse.Successful,
                            Success = true,
                            data = getTransactionStatus
                        };
                    }
                    else
                    {
                        return new GenericResponse2()
                        {
                            Response = EnumResponse.NotSuccessful,
                        };
                    }

                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                Console.WriteLine(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }
        public async Task<GetChargeResponse> GetCharge(string ClientKey, GetChargeRequest Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Request.Username, Request.Session, Request.ChannelId, con);
                    if (!validateSession)
                        return new GetChargeResponse() { Response = EnumResponse.InvalidSession };
                    return await GetCharge(Request);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GetChargeResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GetChargeResponse> GetCharge(GetChargeRequest Request)
        {
            try
            {
                if (Request.DestinationBankCode == _settings.TrustBancBankCode)
                    return new GetChargeResponse() { Charge = 0, Response = EnumResponse.Successful, Success = true };

                var charges = await GetAlCharges();
                decimal charge = 0, totalcharge = 0;

                foreach (var n in charges.OrderBy(x => x.MaxAmount))
                    if (n.MaxAmount > Request.Amount)
                    {
                        charge = n.Charge;
                        break;
                    }

                totalcharge = charge + (charge * _settings.Vat / 100); // pls check
                return new GetChargeResponse() { Charge = totalcharge, Response = EnumResponse.Successful, Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GetChargeResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        private async Task<List<ChargesTable>> GetAlCharges()
        {
            try
            {
                var myData = new List<ChargesTable>();
                if (!_cache.TryGetValue(CacheKeys.Charges, out myData))
                {
                    // Key not in cache, so get data.
                    myData = await _genServ.CallServiceAsync<List<ChargesTable>>(Method.GET, $"{_settings.AccessUrl}AccessOutward/GetCharges", null);

                    // Set cache options.
                    var cacheEntryOptions = new MemoryCacheEntryOptions()
                        // Keep in cache for this time, reset time if accessed.
                        .SetSlidingExpiration(TimeSpan.FromDays(1));

                    // Save data in cache.
                    _cache.Set(CacheKeys.Charges, myData, cacheEntryOptions);
                }
                return myData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new List<ChargesTable>();
            }
        }


        public int GenerateCheckDigit(string serialNumber, string bankCode)
        {
            if (serialNumber.Length > SerialNumLength)
            {
                throw new Exception($"Serial number should be at most {SerialNumLength}-digits long.");
            }
            serialNumber = serialNumber.PadLeft(SerialNumLength, '0');
            string cipher = bankCode + serialNumber;
            int sum = 0;
            for (int i = 0; i < (cipher.Length - 1); i++)
            {
                sum += (cipher[i] - '0') * (Seed[i] - '0');
            }
            sum %= 10;
            int checkDigit = 10 - sum;
            checkDigit = checkDigit == 10 ? 0 : checkDigit;
            return checkDigit;
        }

        public async Task<BankList> GetPossibleBanks(string ClientKey, string AccountNumber)
        {
            try
            {
                // call api here first to get suggested banks
               var SuggestedBankResponse  =  await _genServ.CallServiceAsyncToString(Method.GET,_settings.SuggestedBankUrl+AccountNumber, "",true);
               GenericResponse2 SuggestedBank = JsonConvert.DeserializeObject<GenericResponse2>(SuggestedBankResponse);
                string json = JsonConvert.SerializeObject(SuggestedBank.data);
                List<Bank> banks = JsonConvert.DeserializeObject<List<Bank>>(json);
                var suggestedresult = new List<Banks>();
                if(banks.Any()) {
                    banks.ForEach(element =>
                    {
                        suggestedresult.Add(new Banks() { Bankcode = element.code, BankName = element.name });
                    });
                    Console.WriteLine("suggestedresult " + suggestedresult);
                    return new BankList() { Banks = suggestedresult, Response = EnumResponse.Successful, Success = suggestedresult.Any() };
                }
                if (string.IsNullOrEmpty(AccountNumber))
                    return new BankList() { Response = EnumResponse.InvalidDetails };

                AccountNumber = AccountNumber.Trim();

                var result = new List<Banks>();
                var chkTb = await _genServ.GetCustomerbyAccountNo(AccountNumber);
                if (chkTb.success)
                    result.Add(new Banks() { Bankcode = _settings.TrustBancBankCode, BankName = _settings.TrustBancBankName });

                var bnks = await _genServ.GetBanks();

                Console.WriteLine("bnks " + bnks);
                if (!bnks.Any())
                    return new BankList() { Banks = result, Response = EnumResponse.Successful, Success = result.Any() };
                // foreach (var n in bnks.Where(x => !string.IsNullOrEmpty(x.CbnCode)).OrderBy(p => p.Bankname))
                //foreach (var n in bnks)
                foreach (var n in bnks)
                {
                    Console.WriteLine("checking BankCode {0}", n.BankCode);
                    if (n.BankCode == _settings.TrustBancBankCode)
                        continue;

                    int lastdigit = GetLastDigit(AccountNumber, n.BankCode);
                    string serialNumber = AccountNumber.Substring(0, 9);
                    //int lastdigit2  = GenerateCheckDigit(serialNumber, n.BankCode);
                    if (lastdigit == 99)
                        continue;
                    /*
                    if (int.Parse(SourceAccount[SourceAccount.Length - 1].ToString()) == lastdigit2)
                        Console.WriteLine("{0} {1}",lastdigit2, int.Parse(SourceAccount[SourceAccount.Length - 1].ToString()));
                        result.Add(new Banks()
                        {
                            Bankcode = n.BankCode,
                            BankName = n.Bankname.ToUpper()
                        });
                    */

                    if (int.Parse(AccountNumber[AccountNumber.Length - 1].ToString()) == lastdigit)
                    {
                        Console.WriteLine("{0} {1} {2}", lastdigit, int.Parse(AccountNumber[AccountNumber.Length - 1].ToString()), int.Parse(AccountNumber[AccountNumber.Length - 1].ToString()) == lastdigit);
                        result.Add(new Banks()
                        {
                            Bankcode = n.BankCode,
                            BankName = n.Bankname.ToUpper()
                        });
                    }
                }
                // checking
                return new BankList() { Banks = result, Response = EnumResponse.Successful, Success = result.Any() };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new BankList() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public int GetLastDigit(string accountnumber, string bankcode)
        {
            Console.WriteLine($"bankcode {bankcode}");
            try
            {
                int total = int.Parse(bankcode.Substring(0, 1)) * 3 + int.Parse(bankcode.Substring(1, 1)) * 7 + int.Parse(bankcode.Substring(2, 1)) * 3 + int.Parse(accountnumber.Substring(0, 1)) * 3 + int.Parse(accountnumber.Substring(1, 1)) * 7 + int.Parse(accountnumber.Substring(2, 1)) * 3 + int.Parse(accountnumber.Substring(3, 1)) * 3 + int.Parse(accountnumber.Substring(4, 1)) * 7 + int.Parse(accountnumber.Substring(5, 1)) * 3 + int.Parse(accountnumber.Substring(6, 1)) * 3 + int.Parse(accountnumber.Substring(7, 1)) * 7 + int.Parse(accountnumber.Substring(8, 1)) * 3;

                int remainder = total % 10;
                if (remainder == 0)
                    return 0;

                return 10 - remainder;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return 99;
            }
        }

        public Task<List<double>> GetTopAmountSent(string ClientKey, TopAmount request)
        {
            throw new NotImplementedException();
        }

        public async Task<TransResponse> TransferFunds(string ClientKey, TransferRequestSingle Request)
        {
            try
            {          
                if (Request.SourceAccountNo.Trim() == Request.DestinationAccountNo.Trim() && Request.DestinationBankCode.Trim() == _settings.TrustBancBankCode.Trim())
                    return new TransResponse() { Success = false, Response = EnumResponse.SameAccountErrors };

                if (string.IsNullOrEmpty(Request.TransactionRef) || Request.Amount == 0)
                    return new TransResponse() { Success = false, Response = EnumResponse.TransError };

                using (IDbConnection con = _context.CreateConnection())
                {

                    // Fetch the transaction creation time
                    string timequery = $@"SELECT 
                                            TIMESTAMPDIFF(SECOND, createdon, NOW()) AS timeDifferenceInSeconds
                                        FROM 
                                            transfer
                                        WHERE 
                                            Source_Account = @Source_Account 
                                            AND Amount = @Amount 
                                            AND Destination_Account = @Destination_Account 
                                            AND Success = 1
                                        ORDER BY 
                                            createdon DESC 
                                        LIMIT 1;
                                        ";
                    var transactionCreatedOn = (await con.QueryAsync<int?>(
                     timequery,
                     new { Source_Account = Request.SourceAccountNo, Amount = Request.Amount, Destination_Account = Request.DestinationAccountNo }
                 )).FirstOrDefault();
                    // Check if the transaction exists and the time difference
                    _logger.LogInformation($"transactionCreatedOn.HasValue {transactionCreatedOn.HasValue}");
                    if (transactionCreatedOn.HasValue)
                    {
                        // Calculate the time difference
                        var timeDifference = transactionCreatedOn.Value;
                        _logger.LogInformation($"timeDifference.TotalMinutes {timeDifference}");
                        _logger.LogInformation($"_settings.TransTimeInterval {double.Parse(_settings.TransTimeInterval)}");
                        _logger.LogInformation($"Time difference {timeDifference <= double.Parse(_settings.TransTimeInterval)}");
                        if (timeDifference < int.Parse(_settings.TransTimeInterval))
                        {
                            // Transaction is within 120 seconds
                            _logger.LogInformation("Transaction is within the allowed time window.");
                            return new TransResponse() { Response = EnumResponse.TransactionNotPermittedWithInterval };
                        }
                    }
                    //doing trans validation
                    // using IDbConnection con = _context.CreateConnection();
                    string result = await _genServ.CheckIfUserIdExistInTransfer(Request.Username, con);
                    _logger.LogInformation("user check " + result);
                    if (result != null) {
                        // if (!Request.TransPin.Equals(_settings.MutualFundPin,StringComparison.CurrentCultureIgnoreCase)) { 
                        TransLimitValidation genericResponse2 = null;
                        if(!Request.DestinationBankCode.Equals(_settings.TrustBancBankCode,StringComparison.CurrentCultureIgnoreCase)) {
                         genericResponse2 = await CheckCustomerDailyandSingleTransactionLimit(ClientKey, Request.Username,
                             Request.Session, Request.ChannelId, Request.Amount, Request.SourceAccountNo);
                          }
                        _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                        _logger.LogInformation("genericResponse2.genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                        if (genericResponse2 != null)
                        {
                            if (!genericResponse2.limitstatus)
                            {
                                return new TransResponse() { Response = genericResponse2.genericResponse2.Response };
                            }
                        }
                    }
                  // }
                    var validateSession = await _genServ.ValidateSession(Request.Username, Request.Session, Request.ChannelId, con);
                    if (!validateSession)
                        return new TransResponse() { Response = EnumResponse.InvalidSession };

                    var usr = await _genServ.GetUserbyUsername(Request.Username, con);
                    if (usr == null)
                        return new TransResponse() { Response = EnumResponse.UserNotFound };

                    return await TransferFunds(Request, con, usr);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new TransResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<TransResponse> TransferFunds(TransferRequestSingle Request, IDbConnection con, Users usr)
        {
            try
            {
                _logger.LogInformation($"usr {usr}");
                Request.TransactionRef = _genServ.StringNumbersOnly(Request.TransactionRef);

                var chktrn = await con.QueryAsync<long>($"select id from transfer where transaction_ref =@trnref", new { trnref = Request.TransactionRef });
                if (chktrn.Any())
                    return new TransResponse() { Response = EnumResponse.DuplicateRecoundsFound };

                var channels = await _genServ.GetClientCredentials();
                if (!channels.Any(x => x.ClientId == Request.ChannelId))
                    return new TransResponse() { Response = EnumResponse.ChannelError };

                var validt = await ValidateNumber(new ValidateAccountNumber() { DestinationAccount = Request.DestinationAccountNo, DestinationBankCode = Request.DestinationBankCode, Username = Request.Username }, con);

                _logger.LogInformation("validation details - " + JsonConvert.SerializeObject(validt));
                if (!validt.Success)
                    return new TransResponse() { Response = EnumResponse.InvalidBeneficiary };

                Request.Narration = _genServ.RemoveSpecialCharacters(Request.Narration);
                var chargRequest = new GetChargeRequest()
                {
                    Amount = Request.Amount,
                    DestinationAccount = Request.DestinationAccountNo,
                    DestinationBankCode = Request.DestinationBankCode,
                    Username = Request.Username,
                    Session = Request.Session
                };
                var charge = await GetCharge(chargRequest);
                //debit for charge 
                string bankname = _settings.TrustBancBankName;
               // _logger.LogInformation("Request.DestinationBankCode " + Request.DestinationBankCode);
                //_logger.LogInformation("_settings.TrustBancBankCode " + _settings.TrustBancBankCode);
              //  _logger.LogInformation("Request.DestinationBankCode != _settings.TrustBancBankCode " + (Request.DestinationBankCode != _settings.TrustBancBankCode));
                if (Request.DestinationBankCode != _settings.TrustBancBankCode)
                {
                    var bnks = await _genServ.GetBanks();
                    _logger.LogInformation($"banks {JsonConvert.SerializeObject(bnks)}");
                    bankname = bnks.FirstOrDefault(x => x?.BankCode == Request.DestinationBankCode)?.Bankname;
                }
               // _logger.LogInformation("Request.SaveBeneficiary ..." + Request.SaveBeneficiary);
               // _logger.LogInformation("bankname ..." + bankname);
                if (Request.SaveBeneficiary)
                {
                    var savebn = new BeneficiaryModel()
                    {
                        Name = validt.AccountName,
                        Value = Request.DestinationAccountNo,
                        ServiceName = bankname,
                        Code = Request.DestinationBankCode,
                        BeneficiaryType = 1
                    };
                    await _benServ.SaveBeneficiary(usr.Id, savebn, con, BeneficiaryType.Transfer);
                };
                _logger.LogInformation("saving into transfer table ...");
                string sql = $@"INSERT INTO transfer (User_Id,Source_Account,Transaction_Ref,Destination_AccountName,Destination_Account,
                        Destination_BankCode,Destination_BankName,Amount,Charge,Narration,Channel_Id, Session,CreatedOn,DeviceId) values 
                        ({usr.Id},@sour,@trnsRef,@destname,@destacct,@destcode,@destbank,{Request.Amount},{charge.Charge},@narr,{Request.ChannelId},@sess,sysdate(),@devId)";
                Request.TransactionRef = Request.DestinationBankCode.Equals(_settings.TrustBancBankCode,StringComparison.CurrentCultureIgnoreCase) ? "TRST-INTRA-" + GenerateRequestID(5)+_genServ.RemoveSpecialCharacters(Request.TransactionRef)+ GenerateRequestID(5): "TRST-INT-" + GenerateRequestID(5) + _genServ.RemoveSpecialCharacters(Request.TransactionRef)+ GenerateRequestID(5);
                await con.ExecuteAsync(sql, new
                {
                    sour = Request.SourceAccountNo,
                    trnsRef = Request.TransactionRef,
                    destname = validt.AccountName,
                    destacct = Request.DestinationAccountNo,
                    destcode = Request.DestinationBankCode,
                    destbank = bankname,
                    narr = Request.Narration,
                    sess = Request.Session,
                    devId = Request.DeviceId
                });
               // _logger.LogInformation("saved into transfer table ...");
                var gettrans = await con.QueryAsync<long>($"select id from transfer where transaction_ref =@trnref", new { trnref = Request.TransactionRef });
                long transId = gettrans.FirstOrDefault();
                var accts = await _genServ.GetAccountbyCustomerId(usr.CustomerId);
                _logger.LogInformation("accts.... " + JsonConvert.SerializeObject(accts));
               // _logger.LogInformation("accts.success.... " + accts.success + JsonConvert.SerializeObject(accts.balances));
                // Console.WriteLine($"transId {transId} accts {JsonConvert.SerializeObject(accts)}");
                if (!accts.success || !accts.balances.Any(x => x.accountNumber == Request.SourceAccountNo))
                {
                    await UpdateTrans(transId, con, false, "", EnumResponse.SuspectedFraud.ToString(), "");
                    return new TransResponse() { Response = EnumResponse.SuspectedFraud };
                }
                //check balance 
               // decimal TheAccountBalance =accts.balances.Where(e=>e.accountNumber.Equals(Request.DestinationAccountNo,StringComparison.CurrentCultureIgnoreCase)).ElementAtOrDefault(0).availableBalance;
                decimal TheAccountBalance = accts.balances
                        .Where(e => e.accountNumber.Equals(Request.SourceAccountNo, StringComparison.CurrentCultureIgnoreCase))
                        .FirstOrDefault()?.availableBalance ?? 0;
                /*
                if ((accts.balances.ElementAtOrDefault(0).availableBalance < (Request.Amount + charge.Charge))) {
                    return new TransResponse() { Response = EnumResponse.InsufficientBalance, Message = "Insufficient Balance" };
                }
                */
                _logger.LogInformation("TheAccountBalance " + TheAccountBalance);
                if ((TheAccountBalance < (Request.Amount + charge.Charge)))
                {
                    return new TransResponse() { Response = EnumResponse.InsufficientBalance, Message = "Insufficient Balance" };
                }
                //  var usrDev = await _genServ.GetActiveMobileDevice(usr.Id, con);
                var usrDev = await _genServ.GetListOfActiveMobileDevice(usr.Id, con);
               // _logger.LogInformation(" usrDev " + JsonConvert.SerializeObject(usrDev));
                var mydevice = usrDev.Find(e => {
                   // _logger.LogInformation("Device "+e.Device+" "+ "Request.DeviceId " + Request.DeviceId);
                   // _logger.LogInformation("e.Device.Equals(Request.DeviceId); " + e.Device.Equals(Request.DeviceId));
                    return e.Device.Equals(Request.DeviceId,StringComparison.OrdinalIgnoreCase);
                    });
                _logger.LogInformation(" mydevice " + mydevice);
                if (_settings.CheckDevice == "y" && Request.ChannelId == 1 && mydevice == null)
                {
                    await UpdateTrans(transId, con, false, "", EnumResponse.DeviceNotRegistered.ToString(), "");
                    return new TransResponse() { Response = EnumResponse.DeviceNotRegistered };
                }
                if (!Request.TransPin.Equals(_settings.MutualFundPin, StringComparison.OrdinalIgnoreCase)) {
                    //var transPin = await _genServ.GetUserCredential(CredentialType.TransactionPin, usr.Id, con);
                    var UsrCredential = await _genServ.GetUserCredentialForTrans(CredentialType.TransactionPin, usr.Id, con);
                   // Console.WriteLine(" transPin " + UsrCredential.credential);
                    if (UsrCredential!=null)
                    {
                        if (UsrCredential.temporarypin == "y")
                        {
                            return new TransResponse() { Response = EnumResponse.TemporaryPin };
                        }
                    }
                    else
                    {
                        return new TransResponse() { Response = EnumResponse.TransactionPinNotFound };
                    }
                    string enterpin = _genServ.EncryptString(Request.TransPin);
                  //  _logger.LogInformation("Pin " + String.Equals(enterpin,UsrCredential.credential));
                    if (enterpin != UsrCredential.credential)
                    {                    
                        await UpdateTrans(transId, con, false, "", EnumResponse.InvalidTransactionPin.ToString(), "");
                        await _genServ.InsertLogs(usr.Id, Request.Session, Request.DeviceId, "", $"{EnumResponse.InvalidTransactionPin} on {Request.Username}", con);
                        await con.ExecuteAsync("delete from transfer where Transaction_Ref=@Transaction_Ref",new { Transaction_Ref =Request.TransactionRef});
                        return new TransResponse() { Response = EnumResponse.InvalidTransactionPin };
                    }
                }
                /*
                if (validt.AvailableLimit < (Request.Amount + charge.Charge))
                {
                    await UpdateTrans(transId, con, false, "", EnumResponse.DailyLimitExceed.ToString(), "");
                    return new TransResponse() { Response = EnumResponse.DailyLimitExceed };
                }
                */
                _logger.LogInformation("posting TransferRequestSingle ....." + JsonConvert.SerializeObject(Request));
                return await PostingTransaction(Request, con, transId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new TransResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }
        private static Mutex _mutex = new Mutex();
        private static string _logintoken;
        public string GenerateRequestID(int length)
        {
            string characters = "0123456789";
            StringBuilder randomString = new StringBuilder(length);
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                byte[] randomByte = new byte[1];
                for (int i = 0; i < length; i++)
                {
                    rng.GetBytes(randomByte);
                    int randomIndex = randomByte[0] % characters.Length;
                    randomString.Append(characters[randomIndex]);
                }
            }
            return randomString.ToString();
        }

        private async Task sendNotificationToCustomer(TransferRequestSingle Request,string AccountNumber,IDbConnection con,string AlertType)
        {
           // con.Open();
            _logger.LogInformation("sending push notification for credit alert ...");
            FinedgeSearchBvn finedgeSearchBvn = await _genServ.GetCustomerbyAccountNo(AccountNumber);
            string Customerid = finedgeSearchBvn.result.customerID;
            string destUsername = (await con.QueryAsync<string>(
                   "select Username from users where CustomerId=@customerid",
                  new { customerid = Customerid }
                 )).FirstOrDefault();
            var usr = await _genServ.GetUserbyUsername(destUsername, con);
            var user_id = usr.Id;
            _logger.LogInformation("destUsername " + destUsername + " user id " + user_id);
            if (user_id != 0)
            {
                //var istokenExists = await con.QueryAsync<>("select DeviceToken from mobiledevice where userid=@userid",new {userid=user_id});
                _logger.LogInformation($"DeviceId for {Request.Username} is {Request.DeviceId}");
                FinedgeSearchBvn finedgeSearchBvn1 = await _genServ.GetCustomerbyAccountNo(Request.SourceAccountNo);
                var usertoken = await con.QueryAsync<string>(
                    $"select DeviceToken from mobiledevice where UserId=@userid and DeviceToken is not null",
                    new { userid = user_id }
                );
                _logger.LogInformation($"DeviceId for {Request.Username} {Request.DeviceId} is {usertoken.Any()}");
                if (usertoken.Any())
                {
                    foreach (var token in usertoken)
                    {
                        _logger.LogInformation($"DeviceId for {Request.Username} {Request.DeviceId} is {string.IsNullOrEmpty(token)}");
                        if (!string.IsNullOrEmpty(token))
                        {
                            Console.WriteLine("sending notification with token "+token);
                            await _notification.SendNotificationAsync(
                              token,
                              AlertType,
                              $"{Request.DestinationAccountNo} has been credited with NGN{Request.Amount.ToString("N2", CultureInfo.InvariantCulture)} from {finedgeSearchBvn1.result.firstname.ToUpper()} {finedgeSearchBvn1.result.lastname.ToUpper()}"
                          );
                        }
                    }
                   // con.Close();
                }             
            }
        }

        private async Task<TransResponse> PostingTransaction(TransferRequestSingle Request, IDbConnection con, long transId)
        {
            try
            {
                CultureInfo nigerianCulture = new CultureInfo("en-NG");
                string formattedAmount = Request.Amount.ToString("C", nigerianCulture);
                var usr = await _genServ.GetUserbyUsername(Request.Username, con);               
                if (Request.DestinationBankCode == _settings.TrustBancBankCode)
                {
                    // _genServ.GetC
                    var CustDetails = await _genServ.GetCustomerbyAccountNo(Request.SourceAccountNo);
                    string IntraNarration = CustDetails.result.firstname.ToUpper() + " " + CustDetails.result.lastname.ToUpper() + "/" + Request.Narration;
                    var request = new PostInternal()
                    {
                        payamount = Request.Amount.ToString(),
                        tellerno = _settings.FinedgeKey,
                        creditAcct = Request.DestinationAccountNo,
                        narration = "FT/" +IntraNarration,
                        debitAcct = Request.SourceAccountNo,
                        transactionReference = Request.TransactionRef
                    };
                    _logger.LogInformation("PostInternal " + JsonConvert.SerializeObject(request));
                    var resp = await _genServ.CallServiceAsync<PostingResponse>(Method.POST, $"{_settings.FinedgeUrl}api/Posting/IntraAccountToAccountPosting", request, true);
                    _logger.LogInformation($"resp2 ({(resp != null ? JsonConvert.SerializeObject(resp) : "")})");

                    if (resp != null)
                    {
                        await UpdateTrans(transId, con, resp.success, resp.message,
                                          resp.success ? EnumResponse.Successful.ToString() : EnumResponse.NotSuccessful.ToString(),
                                          resp.processingID);

                        if (resp.success)
                        {
                            var BankName = (await con.QueryAsync<string>($"select Destination_BankName from transfer where transid=@TransId", new { TransId = resp.processingID })).FirstOrDefault();
                            await con.ExecuteAsync("update transfer set Narration=@narr,Success=1 where Transaction_Ref=@Transaction_Ref", new { narr = "FT/" + IntraNarration, Transaction_Ref = Request.TransactionRef });
                            TransactionListRequest transactionListRequest = new TransactionListRequest
                            {
                                TransID = resp.processingID,
                                Source_Account = Request.SourceAccountNo,
                                AccountNumber = Request.SourceAccountNo,
                                Amount = formattedAmount,
                                Narration = "FT/" + IntraNarration,
                                Destination_Account = Request.DestinationAccountNo,
                                Destination_AccountName = Request.DestinationAccountName,
                                Destination_BankName = BankName,
                                CreatedOn = resp.Resp == null ? resp.postDate : DateTime.Now.ToString()
                            };

                            // Run background operations                          
                            Task.Run(async () =>
                            {
                                try
                                {
                                    await RunBackgroundOperations(Request, con, transactionListRequest, formattedAmount, resp.processingID);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "An error occurred while running background operations.");
                                }
                            });

                            return new TransResponse()
                            {
                                Response = EnumResponse.Successful,
                                Success = true,
                                TransId = resp.processingID,
                                Message = resp.message,
                                TranAmt = resp.TranAmt,
                                DestinationAccountName = Request.DestinationAccountName,
                                SourceAccountNo = Request.SourceAccountNo,
                                DestinationAccountNo = Request.DestinationAccountNo,
                                Narration = Request.Narration
                            };
                        }
                        else
                        {
                            _logger.LogInformation("Error response " + JsonConvert.SerializeObject(resp));
                            return new TransResponse()
                            {
                                Response = EnumResponse.NotSuccessful,
                                Success = false,
                                TransId = resp?.processingID,
                                Message = resp?.message,
                                TranAmt = resp?.TranAmt,
                                DestinationAccountName = Request.DestinationAccountName,
                                SourceAccountNo = Request.SourceAccountNo,
                                DestinationAccountNo = Request.DestinationAccountNo,
                                Narration = null
                            };
                        }
                    }
                }
                else {
                    string InterNarration = $"{usr.Firstname.ToUpper()}  {usr.LastName.ToUpper()}/{Request.Narration}";
                    var req = new PostExternal()
                    {
                        amount = Request.Amount,
                        beneficiaryAccount = Request.DestinationAccountNo,
                        beneficiaryBank = Request.DestinationBankCode,
                        clientKey = _settings.FinedgeKey,
                        narration = InterNarration,
                        sourceAccount = Request.SourceAccountNo,
                        transRef = Request.TransactionRef
                    };
                    await con.ExecuteAsync("update transfer set Narration=@narr where Transaction_Ref=@Transaction_Ref", new { narr = "FT/" + InterNarration, Transaction_Ref = Request.TransactionRef });
                    var resp2 = await PostExternal(req);
                    _genServ.LogRequestResponse(" ", "transaction resp2 ", JsonConvert.SerializeObject(resp2));
                    if (resp2.success)
                        await UpdateTrans(transId, con, true, resp2.message, EnumResponse.Successful.ToString(), resp2.processingID);
                    else {
                        // go and check the status and decide here 
                        GetTransactionStatus getTransactionStatus = await
                           GetCustomerInterBankTransactionusStatusForOutwardInService(req.transRef, Request.Username, Request.ChannelId, Request.Session);
                        TransResponse transResponse = GetInterBankStatusResponse(getTransactionStatus);
                        _logger.LogInformation("getTransactionStatus" + JsonConvert.SerializeObject(getTransactionStatus));
                        _logger.LogInformation(" transResponse " + JsonConvert.SerializeObject(transResponse));
                        string Destination_BankName = (await con.QueryAsync<string>($"select Destination_BankName from transfer where transid='{resp2.processingID}'")).FirstOrDefault();
                        CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)usr.Id);
                        customerDataNotFromBvn.PhoneNumber = CustomerServiceNotFromBvnService.ReplaceFirstDigit(customerDataNotFromBvn.PhoneNumber, "234");
                        TransactionListRequest transactionListRequest = new TransactionListRequest();
                        transactionListRequest.TransID = req.transRef;
                        transactionListRequest.Source_Account = Request.SourceAccountNo;
                        transactionListRequest.AccountNumber = Request.SourceAccountNo;
                        transactionListRequest.Amount = formattedAmount;
                        transactionListRequest.Narration = Request.Narration;
                        transactionListRequest.Destination_Account = Request.DestinationAccountNo;
                        transactionListRequest.Destination_AccountName = Request.DestinationAccountName;
                        transactionListRequest.Destination_BankName = Destination_BankName;
                        transactionListRequest.CreatedOn = getTransactionStatus.CreatedOn.ToString(); ;
                        _logger.LogInformation("proceeding to run background ......getTransactionStatus " + JsonConvert.SerializeObject(getTransactionStatus));
                        if(getTransactionStatus.Status!=3) {
                        Task.Run(async () =>
                        {
                            _logger.LogInformation("starting background job for interbank");
                            await RunBackgroundOperationsForInterBank(Request, con, transactionListRequest, formattedAmount, resp2.processingID, customerDataNotFromBvn);
                        });
                        }
                        await UpdateTrans(transId, con, false,getTransactionStatus.Message,getTransactionStatus.StatusRemark,req.transRef);
                        _logger.LogInformation("transResponse " + JsonConvert.SerializeObject(transResponse));
                        return new TransResponse()
                        {
                            Response = getTransactionStatus.Status == 6||getTransactionStatus.Status == 4|| getTransactionStatus.Status == 2 || getTransactionStatus.Status == 0? EnumResponse.Successful : transResponse.Response,
                            Success = getTransactionStatus.Status == 6 || getTransactionStatus.Status == 4|| getTransactionStatus.Status == 2 || getTransactionStatus.Status == 0,
                            TransId = req.transRef,
                            Message = getTransactionStatus.Message,
                            postDate = getTransactionStatus.CreatedOn.ToString(),
                            TranAmt = formattedAmount,
                            DestinationAccountName = Request.DestinationAccountName,
                            SourceAccountNo = Request.SourceAccountNo,
                            DestinationAccountNo = Request.DestinationAccountNo,
                            Narration = Request.Narration,
                            TransDetail = getTransactionStatus.StatusRemark,
                            TransStatus = getTransactionStatus.Status
                        };
                    }
                if (resp2.success)
                {
                    //run in background
                    // var usr = await _genServ.GetUserbyUsername(Request.Username, con);
                    string Destination_BankName = (await con.QueryAsync<string>($"select Destination_BankName from transfer where transid='{resp2.processingID}'")).FirstOrDefault();
                    CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)usr.Id);
                    customerDataNotFromBvn.PhoneNumber = CustomerServiceNotFromBvnService.ReplaceFirstDigit(customerDataNotFromBvn.PhoneNumber, "234");
                    TransactionListRequest transactionListRequest = new TransactionListRequest();
                    transactionListRequest.TransID = resp2.processingID;
                    transactionListRequest.Source_Account = Request.SourceAccountNo;
                    transactionListRequest.AccountNumber = Request.SourceAccountNo;
                    transactionListRequest.Amount = formattedAmount;
                    transactionListRequest.Narration = Request.Narration;
                    transactionListRequest.Destination_Account = Request.DestinationAccountNo;
                    transactionListRequest.Destination_AccountName = Request.DestinationAccountName;
                    transactionListRequest.Destination_BankName = Destination_BankName;
                    transactionListRequest.CreatedOn = resp2.postDate != null ? resp2.postDate : new DateTime().ToString(); ;
                    Task.Run(async () =>
                    {
                        _logger.LogInformation("starting background job for interbank");
                        _logger.LogInformation("starting background job for interbank");
                        await RunBackgroundOperationsForInterBank(Request, con, transactionListRequest, formattedAmount, resp2.processingID, customerDataNotFromBvn);
                    });
                        //check for transaction status
                    GetTransactionStatus getTransactionStatus =await
                            GetCustomerInterBankTransactionusStatusForOutwardInService(resp2.processingID,Request.Username,Request.ChannelId,Request.Session);
                    TransResponse transResponse = GetInterBankStatusResponse(getTransactionStatus);
                    _logger.LogInformation(" transResponse " + JsonConvert.SerializeObject(transResponse));
                    return new TransResponse()
                    {
                        Response =resp2.success?EnumResponse.Successful:transResponse.Response,
                        Success = resp2.success,
                        TransId = resp2.processingID,
                        Message = resp2.message,
                        postDate = resp2.postDate,
                        TranAmt = formattedAmount,
                        DestinationAccountName = Request.DestinationAccountName,
                        SourceAccountNo = Request.SourceAccountNo,
                        DestinationAccountNo = Request.DestinationAccountNo,
                        Narration = Request.Narration,
                        TransDetail=getTransactionStatus.StatusRemark,
                        TransStatus=getTransactionStatus.Status
                    };
                }

                return new TransResponse() { Response = EnumResponse.NotSuccessful };
              }
                return new TransResponse() { Response = EnumResponse.NotSuccessful };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during PostingTransaction",ex.Message);
                throw;
            }
        }

        private async Task RunBackgroundOperationsForInterBank(TransferRequestSingle request, IDbConnection con, TransactionListRequest transactionListRequest, string formattedAmount, string processingId,CustomerDataNotFromBvn customerDataNotFromBvn)
        {
            try
            {
                using (var connection = _context.CreateConnection())
                {
                    if (_settings.EmailAlertNotification=="y")
                    {
                        sendTransactionEmail(transactionListRequest, transactionListRequest.Source_Account, con);
                    }
                    var chargRequest = new GetChargeRequest()
                    {
                        Amount = request.Amount,
                        DestinationAccount = request.DestinationAccountNo,
                        DestinationBankCode = request.DestinationBankCode,
                        Username = request.Username,
                        Session = request.Session
                    };
                    var charge = await GetCharge(chargRequest);
                    var response = await ChargeCustomer(request, charge);
                    if (_settings.SmsAlertNotification=="y")
                    {
                        var smsResponse = await _smsBLService.SendSmsToCustomer("TrustBanc Notification",
                             request.SourceAccountNo, request.Amount.ToString("N2", CultureInfo.InvariantCulture),
                             customerDataNotFromBvn.PhoneNumber, request.Narration, "Debit", charge.Charge.ToString("N2", CultureInfo.InvariantCulture)); // send sms for charges
                        _logger.LogInformation("debit transfer " + smsResponse.Message);
                    }
                     GenericBLServiceHelper genericServiceHelper = new GenericBLServiceHelper();
                   // Console.WriteLine($"in try catch");
                    var myCharge = TransferChargeCalculator.CalculateCharge(request.Amount, _settings.NipCharges);
                    Console.WriteLine("myCharge " + myCharge);
                    _logger.LogInformation("service charge inter bank " + myCharge);
                    if (_settings.SmsAlertNotification=="y")
                    {
                        var serviceChargesmsResponse = await _smsBLService.SendSmsToCustomer("TrustBanc Notification",
                         request.SourceAccountNo, myCharge.ToString(), customerDataNotFromBvn.PhoneNumber, "INTERBANK CHARGE ON ACCOUNT " + request.SourceAccountNo, "Debit"); // send sms for charges
                        _logger.LogInformation("service charge debit transfer " + serviceChargesmsResponse.Message);
                    }
                    Users usr = await _genServ.GetUserbyUsername(request.Username, con);
                    BillPaymentGLPoster billPaymentGLPoster = new BillPaymentGLPoster();
                    billPaymentGLPoster.Narration = _genServ.RemoveSpecialCharacters(request.Narration);
                    billPaymentGLPoster.AccountNumber = request.SourceAccountNo;
                    billPaymentGLPoster.Username = request.Username;
                    billPaymentGLPoster.ChannelId = request.ChannelId;
                    DebitMe debitMe = new DebitMe();
                    debitMe.tellerno = genericServiceHelper.GenerateRequestID(21);
                    debitMe.TransactionReference = genericServiceHelper.GenerateRequestID(21);
                    decimal vat = _settings.Vat / 100;
                    _logger.LogInformation("vat " + vat + " calculation " + decimal.Parse((myCharge * vat).ToString()));
                    debitMe.Payamount = decimal.Parse((myCharge * vat).ToString());
                    debitMe.narration1 = "BANK VAT CHARGE ON ACCOUNT-"+debitMe.debitAcct; 
                    _logger.LogInformation("processing for vats debit for inter bank");
                    debitMe.debitAcct = request.SourceAccountNo;
                    debitMe.RequestID = genericServiceHelper.GenerateRequestID(21);
                    debitMe.CreditAcct = _settings.VatChargesGL;
                    var resp = await DebitCustomerOnGL(billPaymentGLPoster, debitMe, usr, _settings.VatChargesGL, "vat");
                    _logger.LogInformation($"Vat debited successfully interbank {resp}");
                   // await sendNotificationToCustomer(request, customerDataNotFromBvn.PhoneNumber, con, "Credit Alert");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during background operations");
            }
        }
        private async Task RunBackgroundOperations(TransferRequestSingle request, IDbConnection con, TransactionListRequest transactionListRequest, string formattedAmount, string processingId)
        {
            try
            {
                using (var connection = _context.CreateConnection())
                {
                    connection.Open();
                    if (_settings.EmailAlertNotification=="y")
                    {
                        sendTransactionEmail(transactionListRequest, request.SourceAccountNo, connection, "DEBIT");
                        sendTransactionEmail(transactionListRequest, request.DestinationAccountNo, connection, "CREDIT");
                    }
                    var myCharge = TransferChargeCalculator.CalculateCharge(request.Amount, _settings.NipCharges);
                    _logger.LogInformation("myCharge " + myCharge);
                    var user = await _genServ.GetUserbyUsername(request.Username, connection);
                    _logger.LogInformation("Vat charge calcualtion " + decimal.Parse((myCharge * (_settings.Vat / 100)).ToString()));
                    var debitMe = new DebitMe
                    {
                        tellerno = new GenericBLServiceHelper().GenerateRequestID(21),
                        TransactionReference = new GenericBLServiceHelper().GenerateRequestID(21),
                        Payamount = decimal.Parse((myCharge * (_settings.Vat / 100)).ToString()),
                        narration1 = "BANK VAT CHARGE ON ACCOUNT-"+request.SourceAccountNo,
                        debitAcct = request.SourceAccountNo,
                        RequestID = new GenericBLServiceHelper().GenerateRequestID(21),
                        CreditAcct = _settings.VatChargesGL
                    };

                    var billPaymentGLPoster = new BillPaymentGLPoster()
                    {
                        Narration = _genServ.RemoveSpecialCharacters(request.Narration),
                        AccountNumber = request.SourceAccountNo,
                        Username = request.Username,
                        ChannelId = request.ChannelId
                    };

                   // var vatResponse = await DebitCustomerOnGL(billPaymentGLPoster, debitMe, user, _settings.VatChargesGL, "vat");
                    //_logger.LogInformation($"Vat debited successfully {vatResponse}");

                    var finedgeSearchBvn = await _genServ.GetCustomerbyAccountNo(request.DestinationAccountNo);
                    string bvn = finedgeSearchBvn.result.bvn.Trim();
                    var recipientPhoneNumber = (await connection.QueryAsync<string>("select PhoneNumber from customerdatanotfrombvn where username=(select username from users where Bvn=@bvn)", new { bvn })).FirstOrDefault();

                    if (recipientPhoneNumber != null)
                    {
                        if (_settings.SmsAlertNotification=="y")
                        {
                            var creditResponse = await _smsBLService.SendSmsToCustomerForCredit(
                            "TrustBanc Notification",
                            request.DestinationAccountNo,
                            request.Amount.ToString("N2", CultureInfo.InvariantCulture),
                            "234" + recipientPhoneNumber.Substring(1),
                            request.Narration,
                            "Debit Transfer");
                            _logger.LogInformation("transfer credit transfer SMS sent: " + creditResponse.Message);
                        }
                    }
                    await sendNotificationToCustomer(request, request.DestinationAccountNo, connection, "Credit Alert");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during background operations");
            }
        }

        /*
        private async Task<TransResponse> PostingTransaction(TransferRequestSingle Request, IDbConnection con, long transId)
        {
            try
            {
                CultureInfo nigerianCulture = new CultureInfo("en-NG");
                string formattedAmount = Request.Amount.ToString("C", nigerianCulture);
                if (Request.DestinationBankCode == _settings.TrustBancBankCode)
                {
                    var request = new PostInternal()
                    {
                        payamount = Request.Amount.ToString(),
                        tellerno = _settings.FinedgeKey,
                        creditAcct = Request.DestinationAccountNo,
                        narration = Request.Narration,
                        debitAcct = Request.SourceAccountNo,
                        transactionReference = Request.TransactionRef
                    };
                    _logger.LogInformation("PostInternal " + JsonConvert.SerializeObject(request));
                    var resp = await _genServ.CallServiceAsync<PostingResponse>(Method.POST, $"{_settings.FinedgeUrl}api/Posting/IntraAccountToAccountPosting", request, true);
                    _logger.LogInformation($"resp2 ({(resp != null ? JsonConvert.SerializeObject(resp) : "")})");
                    if (resp != null)
                    {
                        if (resp.success)
                            await UpdateTrans(transId, con, true, resp.message, EnumResponse.Successful.ToString(), resp.processingID);
                        else
                            await UpdateTrans(transId, con, false, resp.message, EnumResponse.NotSuccessful.ToString(), resp.processingID);
                    }
                    var BankName = (await con.QueryAsync<string>($"select Destination_BankName from transfer where transid='{resp.processingID}'")).FirstOrDefault();
                    if (resp.success)
                    {
                        TransactionListRequest transactionListRequest = new TransactionListRequest();
                        transactionListRequest.TransID = resp.processingID;
                        transactionListRequest.Source_Account = Request.SourceAccountNo;
                        transactionListRequest.AccountNumber = Request.SourceAccountNo;
                        transactionListRequest.Amount = formattedAmount;
                        transactionListRequest.Narration = Request.Narration;
                        transactionListRequest.Destination_Account = Request.DestinationAccountNo;
                        transactionListRequest.Destination_AccountName = Request.DestinationAccountName;
                        transactionListRequest.Destination_BankName = BankName;
                        transactionListRequest.CreatedOn = resp.Resp == null ? resp.postDate : new DateTime().ToString();
                        new Thread(async () =>
                        {
                            sendTransactionEmail(transactionListRequest, Request.SourceAccountNo, con, "DEBIT");
                           // con.Close();
                            //con.Open();
                            sendTransactionEmail(transactionListRequest, Request.DestinationAccountNo, con, "CREDIT");
                            await sendNotificationToCustomer(Request, Request.DestinationAccountNo, con, "Credit Alert");
                            DebitMe debitMe = new DebitMe();
                            GenericBLServiceHelper genericServiceHelper = new GenericBLServiceHelper();
                            Console.WriteLine($"in try catch");
                            var myCharge = TransferChargeCalculator.CalculateCharge(Request.Amount, _settings.NipCharges);
                            Console.WriteLine("myCharge " + myCharge);
                            Users usr = await _genServ.GetUserbyUsername(Request.Username, con);
                            BillPaymentGLPoster billPaymentGLPoster = new BillPaymentGLPoster();
                            billPaymentGLPoster.Narration = _genServ.RemoveSpecialCharacters(Request.Narration);
                            billPaymentGLPoster.AccountNumber = Request.SourceAccountNo;
                            billPaymentGLPoster.Username = Request.Username;
                            billPaymentGLPoster.ChannelId = Request.ChannelId;
                            debitMe.tellerno = genericServiceHelper.GenerateRequestID(21);
                            debitMe.TransactionReference = genericServiceHelper.GenerateRequestID(21);
                            decimal vat = _settings.Vat / 100;
                            _logger.LogInformation("intrabank vat " + vat + " calculation " + decimal.Parse((Request.Amount * vat).ToString()));
                            debitMe.Payamount = decimal.Parse((Request.Amount * vat).ToString());
                            debitMe.narration1 = "Vat Charge";
                            _logger.LogInformation("processing for vats debit");
                            debitMe.debitAcct = Request.SourceAccountNo;
                            debitMe.RequestID = genericServiceHelper.GenerateRequestID(21);
                            debitMe.CreditAcct = _settings.VatChargesGL;
                            var resp = await DebitCustomerOnGL(billPaymentGLPoster, debitMe, usr, _settings.VatChargesGL, "vat");
                            _logger.LogInformation($"Vat debited successfully {resp}");
                            var finedgeSearchBvn = await _genServ.GetCustomerbyAccountNo(Request.DestinationAccountNo);
                            string bvn = finedgeSearchBvn.result.bvn.Trim();
                            Console.WriteLine("bvn " + bvn);
                            var ReceipientPhoneNumber = (await con.QueryAsync<string>("select PhoneNumber from customerdatanotfrombvn where username=(select username from users where Bvn=@bvn)", new { bvn = bvn })).FirstOrDefault();
                            _logger.LogInformation("preparing for credit sms " + ReceipientPhoneNumber);
                            await Task.Delay(5);
                            var creditResponse = await _smsBLService.SendSmsToCustomerForCredit("TrustBanc Notification",
                                 Request.DestinationAccountNo, Request.Amount.ToString("N2", CultureInfo.InvariantCulture),
                                 "234" + ReceipientPhoneNumber.Substring(1), Request.Narration, "Debit Transfer"); // send sms for charges
                            _logger.LogInformation("credit transfer " + creditResponse.Message);
                        }).Start();
                        _logger.LogInformation("returning response for intrabank " + resp.success + " " + resp.processingID);
                    }
                    return new TransResponse() { Response = resp.success ? EnumResponse.Successful : EnumResponse.NotSuccessful, Success = resp.success, TransId = resp.processingID, Message = resp.message, TranAmt = resp.TranAmt, DestinationAccountName = Request.DestinationAccountName, SourceAccountNo = Request.SourceAccountNo, DestinationAccountNo = Request.DestinationAccountNo, Narration = Request.Narration };
                }
                return new TransResponse() { Response=EnumResponse.NotSuccessful };
            }
            catch (Exception ex)
            {
                throw;
            }
        }
        */

        //old version of PostingTransaction.it is also working but maybe slow.
        private async Task<TransResponse> PostingTransaction3(TransferRequestSingle Request, IDbConnection con, long transId)
        {
            try
            {
                CultureInfo nigerianCulture = new CultureInfo("en-NG");
                string formattedAmount = Request.Amount.ToString("C", nigerianCulture);
                // return new TransResponse() { Response = EnumResponse.Successful, Success = true, TransId = transId+"" };  // this is a test response
                if (Request.DestinationBankCode == _settings.TrustBancBankCode)
                {
                    var request = new PostInternal()
                    {
                        payamount = Request.Amount.ToString(),
                        tellerno = _settings.FinedgeKey,
                        creditAcct = Request.DestinationAccountNo,
                        narration = Request.Narration,
                        debitAcct = Request.SourceAccountNo,
                        transactionReference = Request.TransactionRef
                    };

                    /*
                    var request = new PostInternalForIntraBank()
                    {
                        Payamount = R.equest.AmountOrPrincipal.ToString(),              
                        CreditAcct = Request.DestinationAccountNo,
                        narration = Request.Narration,
                        debitAcct = Request.SourceAccountNo,
                    };
                    */
                    // var resp = await PostInternal(request);
                    Console.WriteLine("PostInternal " + JsonConvert.SerializeObject(request));
                    _logger.LogInformation("PostInternal " + JsonConvert.SerializeObject(request));
                    var resp = await _genServ.CallServiceAsync<PostingResponse>(Method.POST, $"{_settings.FinedgeUrl}api/Posting/IntraAccountToAccountPosting", request, true);
                    _logger.LogInformation($"resp2 ({(resp != null ? JsonConvert.SerializeObject(resp) : "")})");
                    if (resp != null)
                    {
                        if (resp.success)
                            await UpdateTrans(transId, con, true, resp.message, EnumResponse.Successful.ToString(), resp.processingID);
                        else
                            await UpdateTrans(transId, con, false, resp.message, EnumResponse.NotSuccessful.ToString(), resp.processingID);
                    }
                    Console.WriteLine("processingID " + resp.processingID);
                    _logger.LogInformation("processingID " + resp.processingID);
                    // send a transaction email
                    var BankName = (await con.QueryAsync<string>($"select Destination_BankName from transfer where transid='{resp.processingID}'")).FirstOrDefault();
                   // Console.WriteLine("BankName " + BankName);
                    
                    if (resp.success)
                    {
                        TransactionListRequest transactionListRequest = new TransactionListRequest();
                        transactionListRequest.TransID = resp.processingID;
                        transactionListRequest.Source_Account = Request.SourceAccountNo;
                        transactionListRequest.AccountNumber = Request.SourceAccountNo;
                        transactionListRequest.Amount = formattedAmount;
                        transactionListRequest.Narration = Request.Narration;
                        transactionListRequest.Destination_Account = Request.DestinationAccountNo;
                        transactionListRequest.Destination_AccountName = Request.DestinationAccountName;
                        transactionListRequest.Destination_BankName = BankName;
                        transactionListRequest.CreatedOn = resp.Resp == null ? resp.postDate : new DateTime().ToString();
                         
                        await Task.Run(async () =>
                        {
                            sendTransactionEmail(transactionListRequest, Request.SourceAccountNo,con,"DEBIT");
                            con.Close();
                            //con.Open();
                            sendTransactionEmail(transactionListRequest, Request.DestinationAccountNo, con, "CREDIT");
                            con.Close();
                            //GetAirtimeCode customer token
                            await sendNotificationToCustomer(Request,Request.DestinationAccountNo,con,"Credit Alert");                        
                        });
                        
                         /*
                        var thread =new Thread(async () =>
                        {
                            sendTransactionEmail(transactionListRequest, Request.SourceAccountNo,con,"DEBIT");
                            _logger.LogInformation("sending credit email");
                            sendTransactionEmail(transactionListRequest, Request.DestinationAccountNo, con,"CREDIT");
                            _logger.LogInformation("sent credit email");
                            //GetAirtimeCode customer token
                            await sendNotificationToCustomer(Request, Request.DestinationAccountNo, con, "Credit Alert");
                           
                          });
                         */
                       // thread.Start();
                    }
                    if (resp.success) {
                        // await task3;
                        var task3 = Task.Run(async () =>
                        {
                            DebitMe debitMe = new DebitMe();
                                try
                                {
                                    Console.WriteLine("About to enter try catch ");
                                    using (IDbConnection con = GetConnection())
                                    {
                                        GenericBLServiceHelper genericServiceHelper = new GenericBLServiceHelper();
                                        Console.WriteLine($"in try catch");
                                        var myCharge = TransferChargeCalculator.CalculateCharge(Request.Amount, _settings.NipCharges);
                                        Console.WriteLine("myCharge " + myCharge);
                                        Users usr = await _genServ.GetUserbyUsername(Request.Username, con);
                                        BillPaymentGLPoster billPaymentGLPoster = new BillPaymentGLPoster();
                                        billPaymentGLPoster.Narration = _genServ.RemoveSpecialCharacters(Request.Narration);
                                        billPaymentGLPoster.AccountNumber = Request.SourceAccountNo;
                                        billPaymentGLPoster.Username = Request.Username;
                                        billPaymentGLPoster.ChannelId = Request.ChannelId;
                                        debitMe.tellerno = genericServiceHelper.GenerateRequestID(21);
                                        debitMe.TransactionReference = genericServiceHelper.GenerateRequestID(21);
                                        decimal vat = _settings.Vat / 100;
                                      _logger.LogInformation("intrabank vat " + vat + " calculation " + decimal.Parse((Request.Amount * vat).ToString()));
                                        debitMe.Payamount = decimal.Parse((Request.Amount * vat).ToString());
                                    //debitMe.Payamount = decimal.Parse((Request.Amount * vat).ToString("F2", CultureInfo.InvariantCulture));
                                    //debitMe.Payamount = decimal.Parse((Request.Amount * vat).ToString("F2", CultureInfo.InvariantCulture));
                                       debitMe.narration1 = "Vat Charge";
                                        _logger.LogInformation("processing for vats debit");
                                        debitMe.debitAcct = Request.SourceAccountNo;
                                        debitMe.RequestID = genericServiceHelper.GenerateRequestID(21);
                                        debitMe.CreditAcct = _settings.VatChargesGL;
                                        var resp = await DebitCustomerOnGL(billPaymentGLPoster, debitMe,usr, _settings.VatChargesGL,"vat");
                                        _logger.LogInformation($"Vat debited successfully {resp}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine(ex.Message);
                                }                          
                            return debitMe;
                        }
                        );
                      // var debitMe3 = await task3;
                     // process sms and debit for service charge
                     
                        var task4 = Task.Run(async () =>
                        {
                                GenericBLServiceHelper genericServiceHelper = new GenericBLServiceHelper();
                                _logger.LogInformation("proceeding to charge " + "for transfer ");
                                var chargRequest = new GetChargeRequest()
                                {
                                    Amount = Request.Amount,
                                    DestinationAccount = Request.DestinationAccountNo,
                                    DestinationBankCode = Request.DestinationBankCode,
                                    Username = Request.Username,
                                    Session = Request.Session
                                };
                                var charge = await GetCharge(chargRequest);
                                //var response = await ChargeCustomer(Request, charge); no charge for intra
                                try
                                {
                                    _logger.LogInformation("Test sms " + " SamplePhoneNumber " + _settings.SamplePhoneNumber);
                                 //   Console.WriteLine("About to enter try catch SamplePhoneNumber " + _settings.SamplePhoneNumber);
                                    // _genServ.LogRequestResponse("Test sms ", "SamplePhoneNumber", _settings.SamplePhoneNumber);
                                    using (IDbConnection con = GetConnection())
                                    {
                                       //con.Close();                            
                                        _logger.LogInformation($"in try catch", "_settings.SamplePhoneNumber", _settings.SamplePhoneNumber);
                                        Console.WriteLine($"in try catch");
                                        var myCharge = TransferChargeCalculator.CalculateCharge(Request.Amount, _settings.NipCharges);
                                        Console.WriteLine("myCharge " + myCharge);
                                       _logger.LogInformation("myCharge " + myCharge);
                                      //  var usr = await _genServ.GetUserbyUsername(Request.Username, con);
                                    // CustomerDataNotFromBvn customerDataNotFromBvn = await genericServiceHelper.getCustomerData(con, "select PhoneNumber,Email from customerdatanotfrombvn where userid=" + usr.Id);
                                   // CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)usr.Id);                 
                                   //  _logger.LogInformation("preparing for debit sms");
                                   //  Console.WriteLine("preparing for debit sms");
                                  //  customerDataNotFromBvn.PhoneNumber = CustomerServiceNotFromBvnService.ReplaceFirstDigit(customerDataNotFromBvn.PhoneNumber, "234");
                                   // _logger.LogInformation("customerDataNotFromBvn.PhoneNumber " + customerDataNotFromBvn.PhoneNumber);
                                    /*
                                    var smsResponse = await _smsBLService.SendSmsToCustomer("TrustBanc Notification",
                                            Request.SourceAccountNo, Request.Amount.ToString("N2", CultureInfo.InvariantCulture),
                                            customerDataNotFromBvn.PhoneNumber, Request.Narration, "Debit Transfer", charge.Charge.ToString("N2", CultureInfo.InvariantCulture)); // send sms for charges
                                    _logger.LogInformation("debit transfer sms sent " + JsonConvert.SerializeObject(smsResponse));
                                     */
                                    // BalanceEnquiryResponse balanceEnquiryResponse = await _genServ.GetAccountDetailsbyAccountNumber(Request.DestinationAccountNo);
                                    var finedgeSearchBvn =  await _genServ.GetCustomerbyAccountNo(Request.DestinationAccountNo);
                                    string bvn = finedgeSearchBvn.result.bvn.Trim();
                                    Console.WriteLine("bvn "+bvn);
                                    // con.Open();
                                   // string bvn = balanceEnquiryResponse.balances.ElementAtOrDefault(0).bvn;
                                    var ReceipientPhoneNumber = (await con.QueryAsync<string>("select PhoneNumber from customerdatanotfrombvn where username=(select username from users where Bvn=@bvn)", new {bvn=bvn})).FirstOrDefault();
                                    _logger.LogInformation("preparing for credit sms " + ReceipientPhoneNumber);
                                       await Task.Delay(5);
                                   var creditResponse = await _smsBLService.SendSmsToCustomerForCredit("TrustBanc Notification",
                                        Request.DestinationAccountNo, Request.Amount.ToString("N2", CultureInfo.InvariantCulture),
                                        "234"+ReceipientPhoneNumber.Substring(1), Request.Narration, "Debit Transfer"); // send sms for charges
                                    _logger.LogInformation("credit transfer " + creditResponse.Message);
                                   // con.Close();
                                   }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine(ex.Message);
                                }
                              return "end task1";
                        });
                        
                        var result3 = await task3;
                        var result4= await task4;
                       }
                    _logger.LogInformation("returning response for intrabank " + resp.success + " " + resp.processingID);
                    return new TransResponse() { Response = resp.success ? EnumResponse.Successful : EnumResponse.NotSuccessful, Success = resp.success, TransId = resp.processingID, Message = resp.message, TranAmt = resp.TranAmt, DestinationAccountName = Request.DestinationAccountName, SourceAccountNo = Request.SourceAccountNo, DestinationAccountNo = Request.DestinationAccountNo, Narration = Request.Narration };
                }
                var req = new PostExternal()
                {
                    amount = Request.Amount,
                    beneficiaryAccount = Request.DestinationAccountNo,
                    beneficiaryBank = Request.DestinationBankCode,
                    clientKey = _settings.FinedgeKey,
                    narration = Request.Narration,
                    sourceAccount = Request.SourceAccountNo,
                    transRef = Request.TransactionRef
                };
                //"TRST-INT-" + GenerateRequestID(15)

                var resp2 = await PostExternal(req);
                _genServ.LogRequestResponse(" ", "resp2 ", JsonConvert.SerializeObject(resp2));
                if (resp2.success)
                    await UpdateTrans(transId, con, true, resp2.message, EnumResponse.Successful.ToString(), resp2.processingID);
                else
                    await UpdateTrans(transId, con, false, resp2.message, EnumResponse.NotSuccessful.ToString(), resp2.processingID);
                // send a transaction email
                var usr = await _genServ.GetUserbyUsername(Request.Username, con);
                string Destination_BankName = (await con.QueryAsync<string>($"select Destination_BankName from transfer where transid='{resp2.processingID}'")).FirstOrDefault();
                CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)usr.Id);
                customerDataNotFromBvn.PhoneNumber = CustomerServiceNotFromBvnService.ReplaceFirstDigit(customerDataNotFromBvn.PhoneNumber, "234");
                if (resp2.success) {          
                    TransactionListRequest transactionListRequest = new TransactionListRequest();
                    transactionListRequest.TransID = resp2.processingID;
                    transactionListRequest.Source_Account = Request.SourceAccountNo;
                    transactionListRequest.AccountNumber = Request.SourceAccountNo;
                    transactionListRequest.Amount = formattedAmount;
                    transactionListRequest.Narration = Request.Narration;
                    transactionListRequest.Destination_Account = Request.DestinationAccountNo;
                    transactionListRequest.Destination_AccountName = Request.DestinationAccountName;
                    transactionListRequest.Destination_BankName = Destination_BankName;
                    transactionListRequest.CreatedOn = resp2.postDate != null ? resp2.postDate : new DateTime().ToString(); ;
                    await Task.Run(async() =>
                    {
                        sendTransactionEmail(transactionListRequest, Request.SourceAccountNo,con);
                        await sendNotificationToCustomer(Request,customerDataNotFromBvn.PhoneNumber,con,"Credit Alert");
                    });
                   // thread.Start();
                }
                // process sms and debit for service charge
                var task1 = Task.Run(async () =>
                {
                    // var resp2 = resp2.Clone();
                    if (resp2.success)
                    {
                        GenericBLServiceHelper genericServiceHelper = new GenericBLServiceHelper();
                        _logger.LogInformation("proceeding to charge " + "for transfer ");
                        Console.WriteLine("proceeding to charge ");
                        var chargRequest = new GetChargeRequest()
                        {
                            Amount = Request.Amount,
                            DestinationAccount = Request.DestinationAccountNo,
                            DestinationBankCode = Request.DestinationBankCode,
                            Username = Request.Username,
                            Session = Request.Session
                        };
                        var charge = await GetCharge(chargRequest);
                        var response = await ChargeCustomer(Request, charge);
                        try
                        {
                            _logger.LogInformation("Test sms " + " SamplePhoneNumber " + _settings.SamplePhoneNumber);
                            Console.WriteLine("About to enter try catch SamplePhoneNumber " + _settings.SamplePhoneNumber);
                            // _genServ.LogRequestResponse("Test sms ", "SamplePhoneNumber", _settings.SamplePhoneNumber);
                            using (IDbConnection con = GetConnection())
                            {
                                _logger.LogInformation($"in try catch", "_settings.SamplePhoneNumber", _settings.SamplePhoneNumber);
                                Console.WriteLine($"in try catch");
                                var myCharge = TransferChargeCalculator.CalculateCharge(Request.Amount, _settings.NipCharges);
                                Console.WriteLine("myCharge " + myCharge);
                                var usr = _genServ.GetUserbyUsername(Request.Username, con);
                                CustomerDataNotFromBvn customerDataNotFromBvn = await genericServiceHelper.getCustomerData(con, "select PhoneNumber,Email from customerdatanotfrombvn where userid=" + usr.Id);
                                _logger.LogInformation("preparing for debit sms");
                                customerDataNotFromBvn.PhoneNumber = CustomerServiceNotFromBvnService.ReplaceFirstDigit(customerDataNotFromBvn.PhoneNumber,"234");
                                var smsResponse = await _smsBLService.SendSmsToCustomer("TrustBanc Notification",
                                    Request.SourceAccountNo, Request.Amount.ToString("N2", CultureInfo.InvariantCulture),
                                    customerDataNotFromBvn.PhoneNumber, Request.Narration, "Debit Transfer",charge.Charge.ToString("N2",CultureInfo.InvariantCulture)); // send sms for charges
                                _logger.LogInformation("debit transfer " + smsResponse.Message);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                            // return new TransResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
                        }
                    }
                    return "end task1";
                });
               // await task1;
                var task2 = Task.Run(async () =>
                {
                    DebitMe debitMe = new DebitMe();
                    if (resp2.success)
                    {
                        try
                        {
                            Console.WriteLine("About to enter try catch ");
                            using (IDbConnection con = GetConnection())
                            {
                                GenericBLServiceHelper genericServiceHelper = new GenericBLServiceHelper();
                                Console.WriteLine($"in try catch");
                                var myCharge = TransferChargeCalculator.CalculateCharge(Request.Amount, _settings.NipCharges);
                                Console.WriteLine("myCharge " + myCharge);
                                Users usr = await _genServ.GetUserbyUsername(Request.Username, con);
                                BillPaymentGLPoster billPaymentGLPoster = new BillPaymentGLPoster();
                                billPaymentGLPoster.Narration = _genServ.RemoveSpecialCharacters(Request.Narration);
                                billPaymentGLPoster.AccountNumber = Request.SourceAccountNo;
                                billPaymentGLPoster.Username = Request.Username;
                                billPaymentGLPoster.ChannelId = Request.ChannelId;
                                debitMe.tellerno = genericServiceHelper.GenerateRequestID(21);
                                debitMe.TransactionReference = genericServiceHelper.GenerateRequestID(21);
                                decimal vat = _settings.Vat / 100;
                                _logger.LogInformation("vat "+vat + " calculation "+ decimal.Parse((Request.Amount * vat).ToString()));
                                debitMe.Payamount = decimal.Parse((Request.Amount * vat).ToString());
                                //debitMe.Payamount = decimal.Parse((Request.Amount * vat).ToString("F2", CultureInfo.InvariantCulture));
                                debitMe.narration1 = "Vat Charge";
                               // var usr = await _genServ.GetUserbyUsername(Request.Username,con);
                                _logger.LogInformation("processing for vats debit");
                                debitMe.debitAcct = Request.SourceAccountNo;
                                debitMe.RequestID = genericServiceHelper.GenerateRequestID(21);
                                debitMe.CreditAcct = _settings.VatChargesGL;
                                var resp = await DebitCustomerOnGL(billPaymentGLPoster, debitMe,usr,_settings.VatChargesGL,"vat");
                             //   GenericResponse response = await DebitCustomerOnGL(billPaymentGLPoster, debitMe, usr, _settings.TransferChargesGL, "charge");
                                _logger.LogInformation($"Vat debited successfully {resp}");
                             }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                        }
                    }
                     return debitMe;
                }
            );
                var debitMe = await task2;
               // _logger.LogInformation("resulf of task3 "+result);
                _logger.LogInformation("returning response for interbank "+resp2.success+" "+resp2.processingID);
                return new TransResponse()
                {
                    Response = resp2.success ? EnumResponse.Successful : EnumResponse.NotSuccessful,
                    Success = resp2.success,
                    TransId = resp2.processingID,
                    Message = resp2.message,
                    postDate = resp2.postDate,
                    TranAmt = formattedAmount,
                    DestinationAccountName = Request.DestinationAccountName,
                    SourceAccountNo = Request.SourceAccountNo,
                    DestinationAccountNo = Request.DestinationAccountNo,
                    Narration = Request.Narration,
                };
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        public async void sendTransactionEmail(TransactionListRequest transactionListRequest, string accountnumber,IDbConnection con,string type=null)
        {
            _logger.LogInformation("entered sendTransactionEmail ..accountnumber " + accountnumber);
            var CustomerDetails = await _genServ.GetCustomerbyAccountNo(accountnumber);
            var balanaceenquiry = await _genServ.GetAccountbyCustomerId(CustomerDetails.result.customerID);
           // _logger.LogInformation("image link " + "http://10.20.22.236:8081/omnichannel_transactions/" + "HEader.jpg");
            BalanceEnquiryDetails balanceEnquiryDetails = balanaceenquiry.balances.Any() ? balanaceenquiry.balances.ToList().Find(e => e.accountNumber == accountnumber) : null;
            string availableBalance = balanceEnquiryDetails != null ? balanceEnquiryDetails.availableBalance.ToString() : "";
            _logger.LogInformation("got here ...." + availableBalance);
            Users users = await _genServ.GetUserbyCustomerId(CustomerDetails.result.customerID,con);
            CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con,(int)users.Id);
            string htmlcontent = PdfCreator.ReceiptHtml("./wwwroot/HEader.jpg", transactionListRequest, CustomerDetails, availableBalance, _genServ,type);
            SendMailObject sendMailObject = new SendMailObject();
            sendMailObject.Html = htmlcontent;
            //sendMailObject.Email = CustomerDetails.result.email;
            sendMailObject.Email = customerDataNotFromBvn?.Email ?? CustomerDetails.result.email;
            sendMailObject.Subject = "TrustBanc J6 MFB- Transaction Notification";
            _logger.LogInformation("sending mail in thread");
            _logger.LogInformation($" enter in thread to send email ");
            _genServ.SendMail(sendMailObject);
            _logger.LogInformation("mail sent ....");
        }

        private async void sendTransactionEmail(TransactionListRequest transactionListRequest, string accountnumber, string Email, string htmlcontent, string Subject)
        {
            _logger.LogInformation("entered sendTransactionEmail ..accountnumber " + accountnumber);
            var CustomerDetails = await _genServ.GetCustomerbyAccountNo(accountnumber);
            var balanaceenquiry = await _genServ.GetAccountbyCustomerId(CustomerDetails.result.customerID);
           // _logger.LogInformation("image link " + "http://10.20.22.236:8081/omnichannel_transactions/" + "HEader.jpg");
            BalanceEnquiryDetails balanceEnquiryDetails = balanaceenquiry.balances.Any() ? balanaceenquiry.balances.ToList().Find(e => e.accountNumber == accountnumber) : null;
            string availableBalance = balanceEnquiryDetails != null ? balanceEnquiryDetails.availableBalance.ToString() : "";
          //  _logger.LogInformation("got here ...." + availableBalance);
            //_logger.LogInformation("htmlcontent " + htmlcontent);
            SendMailObject sendMailObject = new SendMailObject();
            sendMailObject.Html = htmlcontent;
            sendMailObject.Email = Email;
            sendMailObject.Subject = Subject;
            _logger.LogInformation($" enter in thread to send email ");
            _genServ.SendMail(sendMailObject);
            _logger.LogInformation("mail sent ....");
        }

        private async Task<TransResponse> PostingTransaction2(TransferRequestSingle Request, IDbConnection con, long transId)
        {
            try
            {
                // return new TransResponse() { Response = EnumResponse.Successful, Success = true, TransId = transId+"" };  // this is a test response
                if (Request.DestinationBankCode == _settings.TrustBancBankCode)
                {

                    var request = new PostInternal()
                    {
                        payamount = Request.Amount.ToString(),
                        tellerno = _settings.FinedgeKey,
                        creditAcct = Request.DestinationAccountNo,
                        narration = Request.Narration,
                        debitAcct = Request.SourceAccountNo,
                        transactionReference = Request.TransactionRef
                    };

                    /*
                    var request = new PostInternalForIntraBank()
                    {
                        Payamount = Request.AmountOrPrincipal.ToString(),              
                        CreditAcct = Request.DestinationAccountNo,
                        narration = Request.Narration,
                        debitAcct = Request.SourceAccountNo,
                    };
                    */
                    // var resp = await PostInternal(request);
                    Console.WriteLine("PostInternal " + JsonConvert.SerializeObject(request));
                    var resp = await _genServ.CallServiceAsync<PostingResponse>(Method.POST, $"{_settings.FinedgeUrl}api/Posting/IntraAccountToAccountPosting", request, true);
                    Console.WriteLine("resp2 " + resp);
                    if (resp.success)
                        await UpdateTrans(transId, con, true, resp.message, EnumResponse.Successful.ToString(), resp.processingID);
                    else
                        await UpdateTrans(transId, con, false, resp.message, EnumResponse.NotSuccessful.ToString(), "");

                    return new TransResponse() { Response = resp.success ? EnumResponse.Successful : EnumResponse.NotSuccessful, Success = resp.success, TransId = resp.processingID };
                }

                var req = new PostExternal()
                {
                    amount = Request.Amount,
                    beneficiaryAccount = Request.DestinationAccountNo,
                    beneficiaryBank = Request.DestinationBankCode,
                    clientKey = _settings.FinedgeKey,
                    narration = Request.Narration,
                    sourceAccount = Request.SourceAccountNo,
                    transRef = Request.TransactionRef
                };
                var resp2 = await PostExternal(req);
                if (resp2.success)
                    await UpdateTrans(transId, con, true, resp2.message, EnumResponse.Successful.ToString(), resp2.processingID);
                else
                    await UpdateTrans(transId, con, false, resp2.message, EnumResponse.NotSuccessful.ToString(), "");

                return new TransResponse() { Response = resp2.success ? EnumResponse.Successful : EnumResponse.NotSuccessful, Success = resp2.success, TransId = resp2.Resp.requestID, Message = resp2.Resp.retmsg };
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        private async Task<PostingResponse> PostInternal3(PostInternal Request) => await _genServ.CallServiceAsync<PostingResponse>(Method.POST, $"{_settings.FinedgeUrl}api/Posting/AccountToAccountPosting", Request, true);

        private async Task<PostingResponse> PostInternal(PostInternal Request) => await _genServ.CallServiceAsync<PostingResponse>(Method.POST, $"{_settings.FinedgeUrl}api/Posting/IntraAccountToAccountPosting", Request, true);

        private async Task<PostingResponse> PostInternal2(PostInternal Request) => await _genServ.CallServiceAsync<PostingResponse>(Method.POST, $"{_settings.FinedgeUrl}api/Posting/PostAcctAcctTransfer", Request, true);

        private async Task<PostingResponse> PostExternal(PostExternal Request)
        {
            _logger.LogInformation("Outward Request ...." + JsonConvert.SerializeObject(Request));
            string concatText = _settings.AccessCheckSumKey + Request.sourceAccount + Request.beneficiaryAccount + Request.transRef + Request.amount.ToString("#.##");
            _logger.LogInformation($"concatText dataTobeHash- {concatText}");
            //  _logger.LogInformation($"concatText dataTobeHash- {concatText}");
            string checksum = _genServ.GenerateHmac(concatText,_settings.InterBankKey, true);
            Request.CheckSum = checksum;
            _logger.LogInformation($"checksum - {checksum}");
            Dictionary<string, string> dict = new Dictionary<string, string>();
            dict.Add("ClientKey", _settings.AccessCheckSumKey);
            return await _genServ.CallServiceAsync<PostingResponse>(Method.POST, $"{_settings.AccessUrl}AccessOutward/MakeTransfer", Request, true, dict);
        }

        private async Task<PostingResponse> PostExternal2(PostExternal Request) => await _genServ.CallServiceAsync<PostingResponse>(Method.POST, $"{_settings.FinedgeUrl}api/Posting/InterBankTransfer", Request, true);

        private async Task<PostingResponse> PostExternalOld(PostExternal Request) => await _genServ.CallServiceAsync<PostingResponse>(Method.POST, $"{_settings.AccessUrl}AccessOutward/MakeTransfer", Request, true);

        private async Task UpdateTrans(long Id, IDbConnection con, bool Success, string ResponseCode, string ResponseMessage, string TransId)
        {
            try
            {
                _logger.LogInformation("ResponseCode "+ ResponseCode+ "ResponseMessage "+ ResponseMessage+ " TransId "+ TransId);
                string sql = $"update transfer set success = {(Success ? 1 : 0)}, responsecode = @rspcode,responsemessage = @rspmsg, transid= @trns where id = {Id}";
                await con.ExecuteAsync(sql, new { rspcode = ResponseCode, rspmsg = ResponseMessage, trns = TransId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
            }
        }

        public async Task<ValidateAccountResponse> ValidateNumber(string ClientKey, ValidateAccountNumber Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Request.Username, Request.Session, Request.ChannelId, con);
                    if (!validateSession)
                        return new ValidateAccountResponse() { Response = EnumResponse.InvalidSession };

                    return await ValidateNumber(Request, con);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new ValidateAccountResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        private async Task<ValidateAccountResponse> ValidateNumber(ValidateAccountNumber Request, IDbConnection con)
        {
            try
            {
                var usr = await _genServ.GetUserbyUsername(Request.Username, con);
                var getUserlimits = await _genServ.GetUserLimits(usr.Id, con, BeneficiaryType.Transfer);
                var result = new ValidateAccountResponse()
                {
                    DailyTotalSpent = getUserlimits.DailyTotalSpent,
                    DailyLimit = getUserlimits.DailyLimit,
                    SuggestedAmount = getUserlimits.SuggestedAmount,
                    Response = EnumResponse.DailyLimitExceed
                };
                _logger.LogInformation("result " + result);
                /*
                if (result.AvailableLimit == 0)
                    return result;
                */
                var chkname = await _genServ.ValidateNumberOnly(Request.DestinationAccount, Request.DestinationBankCode);
                result.Success = chkname.Success;
                result.Response = chkname.Response;
                result.AccountName = chkname.AccountName;
                result.AllowedForTransaction = true;
                return result;

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new ValidateAccountResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<TransResponse> TransferFund(string ClientKey, TransferRequestSingle Request)
        {
            try
            {
                if (Request.SourceAccountNo.Trim() == Request.DestinationAccountNo.Trim() && Request.DestinationBankCode.Trim() == _settings.TrustBancBankCode.Trim())
                    return new TransResponse() { Success = false, Response = EnumResponse.SameAccountErrors };

                if (string.IsNullOrEmpty(Request.TransactionRef) || Request.Amount == 0)
                    return new TransResponse() { Success = false, Response = EnumResponse.ZeroAmountError };

                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Request.Username, Request.Session, Request.ChannelId, con);
                    if (!validateSession)
                        return new TransResponse() { Response = EnumResponse.InvalidSession };

                    var usr = await _genServ.GetUserbyUsername(Request.Username, con);
                    if (usr == null)
                        return new TransResponse() { Response = EnumResponse.UserNotFound };

                    return await TransferFunds(Request, con, usr);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new TransResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<TransResponse> RemoveBenficiary(string ClientKey, string Username, string CustomerName, string Session, int ChannelId)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Username, Session, ChannelId, con);
                    if (!validateSession)
                        return new TransResponse() { Response = EnumResponse.InvalidSession };
                    await con.ExecuteAsync("delete from beneficiary where userid=@userid and name=@name", new { userid = Username, name = CustomerName });
                    return new TransResponse() { Success = true, Response = EnumResponse.Successful };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new TransResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<TransResponse> forgetBenficiary(string ClientKey, int beneficiaryId, string Username, string Session, int ChannelId)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Username, Session, ChannelId, con);
                    if (!validateSession)
                        return new TransResponse() { Response = EnumResponse.InvalidSession };
                    await con.ExecuteAsync("delete from beneficiary where id=@id", new { id = beneficiaryId });
                    return new TransResponse() { Success = true, Response = EnumResponse.Successful };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new TransResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }
        public async Task<GenericResponse> AirtimePurchase(string clientKey, AirTimePurchase request) {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(request.Username, request.Session, request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var requestobject = new
                    {
                        customerId = request.CustomerId,
                        requestId = request.RequestId,
                        amount = request.Amount,
                        billerId = request.BillerId
                    };
                    var resp = await _genServ.CallServiceAsyncToString(Method.POST, $"{_settings.billpayment}vtu/airtime/payment", requestobject, true);
                    AirTimeRoot airTimeRoot = JsonConvert.DeserializeObject<AirTimeRoot>(resp);
                    airTimeRoot.CustomerId = request.CustomerId;
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = airTimeRoot };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> DataPurchase(string clientKey, DataPurchase request) {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(request.Username, request.Session, request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var requestobject = new
                    {
                        customerId = request.CustomerId,
                        requestId = request.RequestId,
                        amount = request.Amount,
                        bouquetCode = request.BouquetCode,
                        billerId = request.BillerId
                    };
                    _logger.LogInformation("requestobject ..... " + requestobject);
                    var resp = await _genServ.CallServiceAsyncToString(Method.POST, $"{_settings.billpayment}vtu/data/payment", requestobject, true);
                    _logger.LogInformation("resp for data purchase ......." + resp);
                    DataPaymentResponse dataPaymentResponse = JsonConvert.DeserializeObject<DataPaymentResponse>(resp);
                    dataPaymentResponse.CustomerId = request.CustomerId;
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = dataPaymentResponse };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }
        public async Task<GenericResponse> ValidateVTUPhoneNumber(string clientKey, ValidatePhoneNumber request) {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(request.Username, request.Session, request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var requestobject = new
                    {
                        customerId = request.customerId,
                        requestId = request.requestId
                    };
                    var resp = await _genServ.CallServiceAsyncToString(Method.POST, $"{_settings.billpayment}vtu/validate", requestobject, true);
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = JsonConvert.DeserializeObject<ValidatePhoneNumberRoot>(resp) };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> mcnpaymentforupgradeordowngrade(string clientKey, TvPayment request) {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(request.Username, request.Session, request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var requestobject = new {
                        billerId = request.billerId,
                        customerId = request.customerId,
                        requestId = request.requestId,
                        amount = request.amount,
                        bouquetCode = request.bouquetCode,
                        addonCode = request.addonCode,
                        customerName = request.customerName,
                        customerNumber = request.customerNumber
                    };
                    _logger.LogInformation("tv payment requestobject "+requestobject);
                    var resp = await _genServ.CallServiceAsyncToString(Method.POST, $"{_settings.billpayment}tv/payment", requestobject, true);
                    _logger.LogInformation("tv response "+resp.Trim());
                    if (resp.Trim().Contains("412 Precondition Failed:") && resp.Trim().Contains("\"status\":412"))
                    {
                        int jsonStartIndex = resp.IndexOf(':') + 2;
                        string jsonPayload = resp.Substring(jsonStartIndex).Trim().Trim('"'); // Trim quotes
                        // Parse JSON using JToken
                        JToken token = JToken.Parse(jsonPayload);
                        if (token["status"] != null)
                        {
                            int status = token["status"].Value<int>();
                            BillResponseDetails details = JsonConvert.DeserializeObject<BillResponseDetails>(jsonPayload);
                            _logger.LogInformation("TvResponse Detail " + details);
                            return new BillPaymentResponse() { Success = false, Response = EnumResponse.NotSuccessful, Data = details };
                        }
                    }
                    TvpaymentRoot tvpaymentRoot = JsonConvert.DeserializeObject<TvpaymentRoot>(resp);
                    tvpaymentRoot.CustomerId = request.customerId;
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = tvpaymentRoot };
                }
            } catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }
        public async Task<GenericResponse> GetAllBillerCategories(string clientKey, string Username, string Session, int ChannelId) {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Username, Session, ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var resp = await _genServ.CallServiceAsyncToString(Method.GET, $"{_settings.billpayment}meta/getbillercategories", null, true);
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = JsonConvert.DeserializeObject<CategoryRootObject>(resp) };
                };

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }
        public async Task<GenericResponse> Getallbillers(string clientKey, string Username, string Session, int ChannelId)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Username, Session, ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var resp = await _genServ.CallServiceAsyncToString(Method.GET, $"{_settings.billpayment}meta/getallbillers", null, true);
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = JsonConvert.DeserializeObject<BillerRootObject>(resp) };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> ValidateBet(string clientKey, Bet request) {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(request.Username, request.Session, request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var requestobject = new
                    {
                        billerId = request.billerId,
                        requestId = request.requestId,
                        customerId = request.customerId
                    };
                    var resp = await _genServ.CallServiceAsyncToString(Method.POST, $"{_settings.billpayment}bet/validate", requestobject, true);
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = JsonConvert.DeserializeObject<BetValidateRoot>(resp) };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> RequeryTransaction(string clientKey, string requery, string Username, string Session, int ChannelId)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Username, Session, ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var resp = await _genServ.CallServiceAsyncToString(Method.GET, $"{_settings.billpayment}requerytransaction?requestid={requery}", null, true);
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = JsonConvert.DeserializeObject<string>(resp) };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }
        public async Task<GenericResponse> BetPayment(string clientKey, BetPayment request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(request.Username, request.Session, request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var requestobject = new
                    {

                        billerId = request.billerId,
                        customerId = request.customerId,
                        requestId = request.requestId,
                        customerName = request.customerName,
                        amount = request.Amount
                    };
                    var resp = await _genServ.CallServiceAsyncToString(Method.POST, $"{_settings.billpayment}bet/payment", requestobject, true);
                    BetPaymentRoot betPaymentRoot = JsonConvert.DeserializeObject<BetPaymentRoot>(resp);
                    betPaymentRoot.CustomerId = request.customerId;
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = betPaymentRoot };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> GetBouquets(string clientKey, string Username, string Session, int ChannelId, string catId, string billerId)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Username, Session, ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var resp = await _genServ.CallServiceAsyncToString(Method.GET, $"{_settings.billpayment}bouquets/" + catId + "/" + billerId, null, true);
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = JsonConvert.DeserializeObject<BouquetsRoot>(resp) };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }
        public async Task<GenericResponse> GetMyBillers(string clientKey, string Username, string Session, int ChannelId)
        {

            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Username, Session, ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var resp = await _genServ.CallServiceAsyncToString(Method.GET, $"{_settings.billpayment}meta/getmybillers", null, true);
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = JsonConvert.DeserializeObject<Root>(resp) };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }
        public async Task<GenericResponse> validateCustomerKyc(string clientKey, KycCustomerValidation request) {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(request.Username, request.Session, request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var requestobject = new
                    {
                        billerId = request.billerId,
                        requestId = request.requestId,
                        customerId = request.customerId,
                    };
                    var resp = await _genServ.CallServiceAsyncToString(Method.POST, $"{_settings.billpayment}kyc/validate", requestobject, true);
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = JsonConvert.DeserializeObject<KycRoot>(resp) };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> InternetSubscriptionPayment(string clientKey, InternetSubscriptionPayment request) {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(request.Username, request.Session, request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var requestobject = new
                    {
                        billerId = request.billerId,
                        customerId = request.customerId,
                        requestId = request.requestId,
                        amount = request.Amount,
                        customerAddress = request.customerAddress,
                        customerName = request.customerName,
                        bouquetCode = request.BouquetCode,
                    };
                    var resp = await _genServ.CallServiceAsyncToString(Method.POST, $"{_settings.billpayment}internet/payment", requestobject, true);
                    InternetSubscriptionPaymentRoot internetSubscriptionPaymentRoot = JsonConvert.DeserializeObject<InternetSubscriptionPaymentRoot>(resp);
                    internetSubscriptionPaymentRoot.CustomerId = request.customerId;
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = internetSubscriptionPaymentRoot };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> validateInternetCustomerId(string clientKey, InternetSubscription request) {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(request.Username, request.Session, request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var requestobject = new
                    {
                        billerId = request.billerId,
                        customerId = request.customerId,
                        requestId = request.requestId,
                    };
                    var resp = await _genServ.CallServiceAsyncToString(Method.POST, $"{_settings.billpayment}internet/validate", requestobject, true);
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = JsonConvert.DeserializeObject<InternetSubscriptionRoot>(resp) };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> DiscoPayment(string clientKey, DiscoPayment request) {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(request.Username, request.Session, request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var requestobject = new
                    {
                        billerId = request.billerId,
                        customerId = request.customerId,
                        requestId = request.requestId,
                        amount = request.Amount,
                        customerAddress = request.customerAddress,
                        customerName = request.customerName
                    };
                    var resp = await _genServ.CallServiceAsyncToString(Method.POST, $"{_settings.billpayment}disco/payment", requestobject, true);
                    _logger.LogInformation("disco response " +  resp.Trim());
                    if (resp.Trim().Contains("412 Precondition Failed:") && resp.Trim().Contains("\"status\":412")) { 
                        int jsonStartIndex = resp.IndexOf(':') + 2;
                        string jsonPayload = resp.Substring(jsonStartIndex).Trim().Trim('"'); // Trim quotes
                        // Parse JSON using JToken
                        JToken token = JToken.Parse(jsonPayload);
                        if (token["status"] != null)
                        {
                            int status = token["status"].Value<int>();
                            BillResponseDetails details = JsonConvert.DeserializeObject<BillResponseDetails>(jsonPayload);
                            _logger.LogInformation("BillResponseDetails " + details);
                            return new BillPaymentResponse() { Success = false, Response = EnumResponse.NotSuccessful, Data = details };
                        }
                    }
                    DiscoPaymentRoot discoPaymentRoot = JsonConvert.DeserializeObject<DiscoPaymentRoot>(resp);
                    discoPaymentRoot.CustomerId = request.customerId;
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = discoPaymentRoot };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.BillPaymentNotCompleted, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> validateDiscoCustomer(string clientKey, ValidateDisco request) {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(request.Username, request.Session, request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var requestobject = new
                    {
                        billerId = request.billerId,
                        customerId = request.customerId,
                        requestId = request.requestId,
                    };
                    var resp = await _genServ.CallServiceAsyncToString(Method.POST, $"{_settings.billpayment}disco/validate", requestobject, true);
                    // check response and handle error
                    _logger.LogInformation("disco validation resp "+resp.Trim());
                    if (resp.EndsWith(","))
                    {
                        resp = resp.TrimEnd(',') + "}";
                    }
                    _logger.LogInformation("trimmed disco validation resp " + resp.Trim());
                    JToken token = JToken.Parse(resp.Trim());
                    if (token["data"] != null && token["data"]["status"]?.Value<string>() == "Failed")
                    {
                        BillApiErrorResponse apiResponse = JsonConvert.DeserializeObject<BillApiErrorResponse>(resp);

                        if (apiResponse.Data != null && apiResponse.Data.Status == "Failed")
                        {
                            return new BillPaymentResponse() { Success=false,Response=EnumResponse.IncorrectMeterOrAccountNumber,Data=apiResponse.Data};
                        }
                    }
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = JsonConvert.DeserializeObject<DiscoValidate>(resp) };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }

        }

        public async Task<GenericResponse> starTimepayment(string clientKey, StarTimepayment request) {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(request.Username, request.Session, request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var requestobject = new
                    {
                        billerId = request.billerId,
                        customerId = request.customerId,
                        requestId = request.requestId,
                        customerName = request.customerName,
                        amount = request.Amount,
                    };
                    var resp = await _genServ.CallServiceAsyncToString(Method.POST, $"{_settings.billpayment}tv/startime/payment", requestobject, true);
                    StarTimePaymentRoot starTimePaymentRoot = JsonConvert.DeserializeObject<StarTimePaymentRoot>(resp);
                    starTimePaymentRoot.CustomerId = request.customerId;
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = starTimePaymentRoot };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }

        }

        public async Task<GenericResponse> starTimeValidation(string clientKey, StarTimeValidation request) {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(request.Username, request.Session, request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var requestobject = new
                    {
                        billerId = request.billerId,
                        customerId = request.customerId,
                        requestId = request.requestId,
                    };
                    var resp = await _genServ.CallServiceAsyncToString(Method.POST, $"{_settings.billpayment}tv/startime/validation", requestobject, true);
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = JsonConvert.DeserializeObject<string>(resp) };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> mcnpaymentforrenewal(string clientKey, McnRenewalTvPayment request) {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(request.Username, request.Session, request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var requestobject = new
                    {
                        billerId = request.billerId,
                        customerId = request.customerId,
                        requestId = request.requestId,
                        customerName = request.customerName,
                        amount = request.amount,
                    };
                    _logger.LogInformation("requestobject for renewal "+ JsonConvert.SerializeObject(requestobject));
                    var resp = await _genServ.CallServiceAsyncToString(Method.POST, $"{_settings.billpayment}tv/renewal/payment", requestobject, true);
                    _logger.LogInformation("resp "+resp.Trim());
                    if (resp.Trim().Contains("412 Precondition Failed:") && resp.Trim().Contains("\"status\":412"))
                        {
                        int jsonStartIndex = resp.IndexOf(':') + 2;
                        string jsonPayload = resp.Substring(jsonStartIndex).Trim().Trim('"'); // Trim quotes
                        // Parse JSON using JToken
                        JToken token = JToken.Parse(jsonPayload);
                        if (token["status"] != null)
                        {
                            int status = token["status"].Value<int>();
                            BillResponseDetails details = JsonConvert.DeserializeObject<BillResponseDetails>(jsonPayload);
                            _logger.LogInformation("Tv renewal Response Detail " + details);
                            return new BillPaymentResponse() { Success = false, Response = EnumResponse.NotSuccessful, Data = details };
                        }
                    }
                    TvRenewalPaymentRoot tvRenewalPaymentRoot = JsonConvert.DeserializeObject<TvRenewalPaymentRoot>(resp);
                    tvRenewalPaymentRoot.CustomerId = request.customerId;
                    _logger.LogInformation("resp " +JsonConvert.SerializeObject(resp));
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = tvRenewalPaymentRoot };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> mcnvalidationforrenewal(string clientKey, McnTvValidationRenewal request) {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(request.Username, request.Session, request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var requestobject = new
                    {
                        billerId = request.billerId,
                        customerId = request.customerId,
                        requestId = request.requestId
                    };
                    var resp = await _genServ.CallServiceAsyncToString(Method.POST, $"{_settings.billpayment}tv/renewal/validate", requestobject, true);
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = JsonConvert.DeserializeObject<TvRenewalRoot>(resp) };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }

        }

        public async Task<GenericResponse> McnValidateTVforUpgradeOrdowngrade(string clientKey, TVValidate request) {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(request.Username, request.Session, request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var requestobject = new
                    {
                        billerId = request.BillerId,
                        customerId = request.CustomerId,
                        requestId = request.RequestId
                    };
                    var resp = await _genServ.CallServiceAsyncToString(Method.POST, $"{_settings.billpayment}tv/validate", requestobject, true);
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = JsonConvert.DeserializeObject<TvValidateRoot>(resp) };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }
        public async Task<GenericResponse> eduWaecPayment(string clientKey, WaecPaymentDTO request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(request.Username, request.Session, request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var requestobj = new {
                        billerId = request.BillerId,
                        bouquetCode = request.BouquetCode,
                        amount = request.Amount,
                        requestId = request.RequestId
                    };

                    var resp = await _genServ.CallServiceAsyncToString(Method.POST, $"{_settings.billpayment}education/payment", requestobj, true);
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = JsonConvert.DeserializeObject<WaecRoot>(resp) };
                }
            } catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> DebitCustomer(BillPaymentGLPoster request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(request.Username, request.Session, request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var Request = new
                    {
                        CreditAcct = _settings.BillspaymentGL,
                        DebitAcct = request.AccountNumber,
                        Narration = request.Narration,
                        Payamount = request.Amount,
                        RequestID = GenerateRequestID(21)
                    };

                    Console.WriteLine("request " + JsonConvert.SerializeObject(Request));
                    Dictionary<string, string> dict = new Dictionary<string, string>();
                    dict.Add("ClientKey", _settings.AccessCheckSumKey);
                    List<HeaderApi> header = new List<HeaderApi>();
                    HeaderApi head = new HeaderApi();
                    //head.Value = clientKey;
                    head.Value = _settings.AccessCheckSumKey;
                    head.Header = "ClientKey";
                    header.Add(head);
                    Console.WriteLine("calling api api/Posting/AccountToGlPosting2");
                    var respfromGL = await _genServ.CallServiceAsync<AccounttoGLResponse>(_settings, Method.POST, $"{_settings.FinedgeUrl}api/Posting/AccountToGlPosting2", Request, true, header) as AccounttoGLResponse;
                    Console.WriteLine("respfromGL2 " + JsonConvert.SerializeObject(respfromGL));
                    _logger.LogInformation("JsonConvert.SerializeObject(respfromGL) ....." + JsonConvert.SerializeObject(respfromGL));
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = JsonConvert.DeserializeObject<AccounttoGLResponse>(JsonConvert.SerializeObject(respfromGL)) };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> DebitCustomerOnGL(BillPaymentGLPoster request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(request.Username, request.Session, request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var usr = await _genServ.GetUserbyUsername(request.Username, con);
                    if (usr == null)
                        return new TransResponse() { Response = EnumResponse.UserNotFound };

                    DebitMe debitMe = new DebitMe();
                    debitMe.tellerno = GenerateRequestID(21);
                    debitMe.TransactionReference = GenerateRequestID(21);
                    debitMe.Payamount = Convert.ToDecimal(request.Amount);
                    debitMe.narration1 = _genServ.RemoveSpecialCharacters(request.Narration);
                    debitMe.debitAcct = request.AccountNumber;
                    debitMe.CreditAcct = _settings.BillspaymentGL;
                    debitMe.RequestID = GenerateRequestID(21);
                    var validt = await ValidateNumber(new ValidateAccountNumber() { DestinationAccount = request.AccountNumber, DestinationBankCode = _settings.TrustBancBankCode, Username = request.Username }, con);
                    if (!validt.Success)
                        return new TransResponse() { Response = EnumResponse.InvalidBeneficiary };
                    _logger.LogInformation("Debit json string " + JsonConvert.SerializeObject(debitMe));
                    var respfromGL = await _genServ.CallServiceAsyncToString(Method.POST, $"{_settings.FinedgeUrl}api/Posting/AccountToGlPosting2", debitMe, true);
                    Console.WriteLine("respfromGL2 " + JsonConvert.SerializeObject(respfromGL));
                    _logger.LogInformation("respfromGL ....." + respfromGL);
                    string sql = $@"INSERT INTO transfer (User_Id,Source_Account,Transaction_Ref,Destination_AccountName,Destination_Account,
                        Destination_BankCode,Destination_BankName,AmountOrPrincipal,Charge,Narration,Channel_Id, Session,CreatedOn,DeviceId,success,transtype) values 
                        ({usr.Id},@sour,@trnsRef,@destname,@destacct,@destcode,@destbank,{debitMe.Payamount},{0.0},@narr,{request.ChannelId},@sess,NOW(),@devId,1,'bill')";
                    await con.ExecuteAsync(sql, new
                    {
                        sour = debitMe.debitAcct,
                        trnsRef = debitMe.TransactionReference,
                        destname = validt.AccountName,
                        destacct = _settings.BillspaymentGL,
                        destcode = _settings.TrustBancBankCode,
                        destbank = _settings.TrustBancBankName,
                        narr = debitMe.narration1,
                        sess = request.Session,
                        devId = request.DeviceId
                    });
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = JsonConvert.DeserializeObject<DebitMeOnGLResponse>(respfromGL) };
                }
            } catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> DebitCustomerOnInvestmentGL(BillPaymentGLPoster request, DebitMe debitMe, Users usr)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(request.Username, request.Session, request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    if (usr == null)
                        return new TransResponse() { Response = EnumResponse.UserNotFound };
                    var validt = await ValidateNumber(new ValidateAccountNumber() { DestinationAccount = request.AccountNumber, DestinationBankCode = _settings.TrustBancBankCode, Username = request.Username }, con);
                    if (!validt.Success)
                        return new TransResponse() { Response = EnumResponse.InvalidBeneficiary };
                    _logger.LogInformation("Debit json string " + JsonConvert.SerializeObject(debitMe));
                    var respfromGL = await _genServ.CallServiceAsyncToString(Method.POST, $"{_settings.FinedgeUrl}api/Posting/AccountToGlPosting2", debitMe, true);
                    _logger.LogInformation("respfromGL ....." + respfromGL);
                    string sql = $@"INSERT INTO transfer (User_Id,Source_Account,Transaction_Ref,Destination_AccountName,Destination_Account,
                        Destination_BankCode,Destination_BankName,Amount,Charge,Narration,Channel_Id, Session,CreatedOn,DeviceId,success,transtype) values 
                        ({usr.Id},@sour,@trnsRef,@destname,@destacct,@destcode,@destbank,{debitMe.Payamount},{0.0},@narr,{request.ChannelId},@sess,NOW(),@devId,1,'bill')";
                    await con.ExecuteAsync(sql, new
                    {
                        sour = debitMe.debitAcct,
                        trnsRef = debitMe.TransactionReference,
                        destname = validt.AccountName,
                        destacct = _settings.FixedDepositGL,
                        destcode = _settings.TrustBancBankCode,
                        destbank = _settings.TrustBancBankName,
                        narr = debitMe.narration1,
                        sess = request.Session,
                        devId = request.DeviceId
                    });
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = JsonConvert.DeserializeObject<DebitMeOnGLResponse>(respfromGL) };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> DebitCustomerOnGL(BillPaymentGLPoster request, DebitMe debitMe, Users usr, string GL)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(request.Username, request.Session, request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    if (usr == null)
                        return new TransResponse() { Response = EnumResponse.UserNotFound };
                    var validt = await ValidateNumber(new ValidateAccountNumber() { DestinationAccount = request.AccountNumber, DestinationBankCode = _settings.TrustBancBankCode, Username = request.Username }, con);
                    if (!validt.Success)
                        return new TransResponse() { Response = EnumResponse.InvalidBeneficiary };
                  //  _logger.LogInformation("Debit json string " + JsonConvert.SerializeObject(debitMe));
                    var respfromGL = await _genServ.CallServiceAsyncToString(Method.POST, $"{_settings.FinedgeUrl}api/Posting/AccountToGlPosting2", debitMe, true);
                     _logger.LogInformation("posted into gl ....." + respfromGL);
                    string sql = $@"INSERT INTO transfer (User_Id,Source_Account,Transaction_Ref,Destination_AccountName,Destination_Account,
                        Destination_BankCode,Destination_BankName,Amount,Charge,Narration,Channel_Id, Session,CreatedOn,DeviceId,success,transtype) values 
                        ({usr.Id},@sour,@trnsRef,@destname,@destacct,@destcode,@destbank,{debitMe.Payamount},{0.0},@narr,{request.ChannelId},@sess,NOW(),@devId,1,'charge')";
                    await con.ExecuteAsync(sql, new
                    {
                        sour = debitMe.debitAcct,
                        trnsRef = debitMe.TransactionReference,
                        destname = validt.AccountName,
                        destacct = GL,
                        destcode = _settings.TrustBancBankCode,
                        destbank = _settings.TrustBancBankName,
                        narr = debitMe.narration1,
                        sess = request.Session,
                        devId = request.DeviceId
                    });
                    _logger.LogInformation($"transfer to {GL} sucessful .....");
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = JsonConvert.DeserializeObject<DebitMeOnGLResponse>(respfromGL) };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> DebitCustomerOnGL(BillPaymentGLPoster request, DebitMe debitMe, Users usr, string GL,string transtype)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(request.Username, request.Session, request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    if (usr == null)
                        return new TransResponse() { Response = EnumResponse.UserNotFound };
                    var validt = await ValidateNumber(new ValidateAccountNumber() { DestinationAccount = request.AccountNumber, DestinationBankCode = _settings.TrustBancBankCode, Username = request.Username }, con);
                    if (!validt.Success)
                        return new TransResponse() { Response = EnumResponse.InvalidBeneficiary };
                    //  _logger.LogInformation("Debit json string " + JsonConvert.SerializeObject(debitMe));
                    var respfromGL = await _genServ.CallServiceAsyncToString(Method.POST, $"{_settings.FinedgeUrl}api/Posting/AccountToGlPosting2", debitMe, true);
                    _logger.LogInformation("posted into gl ....." + respfromGL);
                    string sql = $@"INSERT INTO transfer (User_Id,Source_Account,Transaction_Ref,Destination_AccountName,Destination_Account,
                        Destination_BankCode,Destination_BankName,Amount,Charge,Narration,Channel_Id, Session,CreatedOn,DeviceId,success,transtype) values 
                        ({usr.Id},@sour,@trnsRef,@destname,@destacct,@destcode,@destbank,{debitMe.Payamount},{0.0},@narr,{request.ChannelId},@sess,NOW(),@devId,1,'{transtype}')";
                    await con.ExecuteAsync(sql, new
                    {
                        sour = debitMe.debitAcct,
                        trnsRef = debitMe.TransactionReference,
                        destname = validt.AccountName,
                        destacct = GL,
                        destcode = _settings.TrustBancBankCode,
                        destbank = _settings.TrustBancBankName,
                        narr = debitMe.narration1,
                        sess = request.Session,
                        devId = request.DeviceId
                    });
                    _logger.LogInformation($"transfer to {GL} sucessful .....");
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = JsonConvert.DeserializeObject<DebitMeOnGLResponse>(respfromGL) };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> checkIfbillServerIsUp(string Username, string Session, int ChannelId)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Username, Session, ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var resp = await _genServ.CallServiceAsyncToString(Method.POST, $"{_settings.vas2nets}", null, false);
                    return new GenericResponse() { Response = EnumResponse.Successful, Message = "Server is up", Success = true };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> ChargeCustomer(TransferRequestSingle Request, GetChargeResponse charge)
        {
            try {
                using (IDbConnection con = _context.CreateConnection())
                {
                  //  Console.WriteLine("got here ");
                    var validateSession = await _genServ.ValidateSession(Request.Username, Request.Session, Request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    Request.Narration = _genServ.RemoveSpecialCharacters(Request.Narration);
                    var myCharge = TransferChargeCalculator.CalculateCharge(Request.Amount, _settings.NipCharges);
                    _logger.LogInformation("charge.... " + charge);
                    _logger.LogInformation("myCharge from calculator .... " + myCharge);
                    BillPaymentGLPoster billPaymentGLPoster = new BillPaymentGLPoster();
                    billPaymentGLPoster.Narration = _genServ.RemoveSpecialCharacters(Request.Narration);
                    billPaymentGLPoster.AccountNumber = Request.SourceAccountNo;
                    billPaymentGLPoster.Username = Request.Username;
                    billPaymentGLPoster.ChannelId = Request.ChannelId;
                    DebitMe debitMe = new DebitMe();
                    debitMe.tellerno = GenerateRequestID(21);
                    debitMe.TransactionReference = GenerateRequestID(21);
                    Console.WriteLine("charge " + charge.Charge);
                    //debitMe.Payamount = charge.Charge; this includes the vat but i want only charge
                   // debitMe.Payamount = Convert.ToDecimal(charge.Charge);
                    debitMe.Payamount = Convert.ToDecimal(myCharge);
                    debitMe.narration1 = "INTERBANK CHARGE ON ACCOUNT-"+Request.SourceAccountNo+"/"+_genServ.RemoveSpecialCharacters(Request.Narration).Trim();
                    debitMe.debitAcct = Request.SourceAccountNo;
                    debitMe.RequestID = GenerateRequestID(21);
                    debitMe.CreditAcct = _settings.TransferChargesGL;
                    Console.WriteLine("_settings.TransferChargesGL " + _settings.TransferChargesGL);
                    var usr = await _genServ.GetUserbyUsername(Request.Username, con);
                    Console.WriteLine("processing debit for transfer charge .....");
                    GenericResponse response = await DebitCustomerOnGL(billPaymentGLPoster, debitMe, usr, _settings.TransferChargesGL,"charge");
                    return response;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public IDbConnection GetConnection()
        {
            return _context.CreateConnection();
        }

        public async Task<GenericResponse> saveReversal(DebitMe debitMe, string username, IDbConnection con, int ChannelId)
        {
            var usr = await _genServ.GetUserbyUsername(username, con);
            if (usr == null)
                return new GenericResponse() { Response = EnumResponse.UserNotFound };
            var validt = await ValidateNumber(new ValidateAccountNumber() { DestinationAccount = debitMe.debitAcct, DestinationBankCode = _settings.TrustBancBankCode, Username = username }, con);
            if (!validt.Success)
                return new TransResponse() { Response = EnumResponse.InvalidBeneficiary };
            var sql = $@"INSERT INTO transfer 
            (User_Id, Source_Account, Transaction_Ref, Destination_AccountName, Destination_Account, 
             Destination_BankCode, Destination_BankName, Amount, Charge, Narration, Channel_Id, Session, 
             CreatedOn, DeviceId, success, transtype) 
            VALUES 
            (@userId, @sourceAccount, @transactionRef, @destinationAccountName, @destinationAccount, 
             @destinationBankCode, @destinationBankName, @amount, @charge, @narration, @channelId, @session, 
             NOW(), @deviceId, @success, @transactionType)";
            await con.ExecuteAsync(sql, new
            {
                userId = usr.Id,
                sourceAccount = debitMe.debitAcct,
                transactionRef = debitMe.TransactionReference,
                destinationAccountName = validt.AccountName,
                destinationAccount = _settings.BillspaymentGL,
                destinationBankCode = _settings.TrustBancBankCode,
                destinationBankName = _settings.TrustBancBankName,
                amount = debitMe.Payamount,
                charge = 0.0, // Properly parameterized
                narration = debitMe.narration1,
                channelId = ChannelId,
                session = "",
                deviceId = "",
                success = 1,
                transactionType = "bill"
            });
            _logger.LogInformation("Bill payment transfer successful");
            _logger.LogInformation("succesful saved ...."+JsonConvert.SerializeObject(debitMe));
            return new GenericResponse() { Response=EnumResponse.Successful,Success=true};
        }

        public async Task<GenericResponse> DebitCustomerOnGL(BillPaymentGLPoster request, DebitMe debitMe, string GL)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(request.Username, request.Session, request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var usr = await _genServ.GetUserbyUsername(request.Username, con);
                    if (usr == null)
                        return new GenericResponse() { Response = EnumResponse.UserNotFound };
                    var validt = await ValidateNumber(new ValidateAccountNumber() { DestinationAccount = request.AccountNumber, DestinationBankCode = _settings.TrustBancBankCode, Username = request.Username }, con);
                    if (!validt.Success)
                        return new TransResponse() { Response = EnumResponse.InvalidBeneficiary };
                    _logger.LogInformation("Debit json string " + JsonConvert.SerializeObject(debitMe));
                    _logger.LogInformation("FinedgeUrl " + $"{_settings.FinedgeUrl}api/Posting/AccountToGlPosting2");
                    var respfromGL = await _genServ.CallServiceAsyncToString(Method.POST, $"{_settings.FinedgeUrl}api/Posting/AccountToGlPosting2", debitMe, true);
                    DebitMeOnGLResponse debitMeOnGLResponse = JsonConvert.DeserializeObject<DebitMeOnGLResponse>(respfromGL);
                   // Console.WriteLine("respfromGL2 " + JsonConvert.SerializeObject(respfromGL));
                    _logger.LogInformation("respfromGL ....." + respfromGL);
                    if (!debitMeOnGLResponse.success && debitMeOnGLResponse?.resp?.retval != 0)
                    {
                        return new GenericResponse() { Success = false, Response = EnumResponse.NotSuccessful };
                    }
                    string sql = null;
                    /*
                    if (debitMe.CreditAcct.Equals(_settings.VatChargesGL,StringComparison.OrdinalIgnoreCase)) {
                        sql = $@"INSERT INTO transfer (User_Id,Source_Account,Transaction_Ref,Destination_AccountName,Destination_Account,
                        Destination_BankCode,Destination_BankName,Amount,Charge,Narration,Channel_Id, Session,CreatedOn,DeviceId,success,transtype) values 
                        ({usr.Id},@sour,@trnsRef,@destname,@destacct,@destcode,@destbank,{debitMe.Payamount},{0.0},@narr,{request.ChannelId},@sess,NOW(),@devId,1,'bill')";
                        _logger.LogInformation("vat transfer successful");
                        await con.ExecuteAsync(sql, new
                        {
                            sour = debitMe.debitAcct,
                            trnsRef = debitMe.TransactionReference,
                            destname = validt.AccountName,
                            destacct = _settings.VatChargesGL,
                            destcode = _settings.TrustBancBankCode,
                            destbank = _settings.TrustBancBankName,
                            narr = debitMe.narration1,
                            sess = request.Session,
                            devId = request.DeviceId
                        });
                    }
                    */
                     if(debitMe.CreditAcct.Equals(_settings.BillspaymentGL,StringComparison.OrdinalIgnoreCase)) {
                        sql = $@"INSERT INTO transfer (User_Id,Source_Account,Transaction_Ref,Destination_AccountName,Destination_Account,
                        Destination_BankCode,Destination_BankName,Amount,Charge,Narration,Channel_Id, Session,CreatedOn,DeviceId,success,transtype) values 
                        ({usr.Id},@sour,@trnsRef,@destname,@destacct,@destcode,@destbank,{debitMe.Payamount},{0.0},@narr,{request.ChannelId},@sess,NOW(),@devId,1,'bill')";
                        _logger.LogInformation("billpayment transfer successful");
                        await con.ExecuteAsync(sql, new
                        {
                            sour = debitMe.debitAcct,
                            trnsRef = debitMe.TransactionReference,
                            destname = validt.AccountName,
                            destacct = _settings.BillspaymentGL,
                            destcode = _settings.TrustBancBankCode,
                            destbank = _settings.TrustBancBankName,
                            narr = debitMe.narration1,
                            sess = request.Session,
                            devId = request.DeviceId
                        });
                    }
                     /*
                    else if(debitMe.CreditAcct.Equals(_settings.TransferChargesGL,StringComparison.OrdinalIgnoreCase))
                    {
                        sql = $@"INSERT INTO transfer (User_Id,Source_Account,Transaction_Ref,Destination_AccountName,Destination_Account,
                        Destination_BankCode,Destination_BankName,Amount,Charge,Narration,Channel_Id, Session,CreatedOn,DeviceId,success,transtype) values 
                        ({usr.Id},@sour,@trnsRef,@destname,@destacct,@destcode,@destbank,{debitMe.Payamount},{0.0},@narr,{request.ChannelId},@sess,NOW(),@devId,1,'bill')";
                        _logger.LogInformation("transfer successful");
                        await con.ExecuteAsync(sql, new
                        {
                            sour = debitMe.debitAcct,
                            trnsRef = debitMe.TransactionReference,
                            destname = validt.AccountName,
                            destacct = _settings.TransferChargesGL,
                            destcode = _settings.TrustBancBankCode,
                            destbank = _settings.TrustBancBankName,
                            narr = debitMe.narration1,
                            sess = request.Session,
                            devId = request.DeviceId
                        });
                    }
                     */
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = debitMeOnGLResponse };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> GetPaymentRecord(string username, string session, string transRefId,int ChannelId)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(username,session, ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    Console.WriteLine("Transaction_Ref " + transRefId);
                   var Transfer  = (await con.QueryAsync<Transfer>("select Narration,Charge,Source_Account,Amount,Transaction_Ref,transtype,CreatedOn,requestid from transfer where Transaction_Ref=@transaction_Ref",new { transaction_Ref=transRefId })).FirstOrDefault();
                    if (Transfer.requestid != null)
                    {  //do api call
                        string response = await _genServ.CallServiceAsyncToString(Method.GET, _settings.BillsRecordGetterUrl+Transfer.requestid, null, true);
                        JObject json = (JObject)JToken.Parse(response);
                        string customerName = json.ContainsKey("customerName") ? json["customerName"].ToString() : "";
                        Transfer.customerName = customerName;
                        string customerId = json.ContainsKey("customerId") ? json["customerId"].ToString() : "";
                        Transfer.customerId = customerId;
                        string billerId = json.ContainsKey("billerId") ? json["billerId"].ToString() : "";
                        Transfer.billerId = billerId;
                        string customerAddress = json.ContainsKey("customerAddress") ? json["customerAddress"].ToString() : "";
                        Transfer.customerAddress = customerAddress;
                        string bouquetCode = json.ContainsKey("bouquetCode") ? json["bouquetCode"].ToString() : "";
                        Transfer.bouquetCode = bouquetCode;
                        string customerNumber = json.ContainsKey("customerNumber") ? json["customerNumber"].ToString() : "";
                        Transfer.customerNumber = customerNumber;
                        string smartCardNumber = json.ContainsKey("smartCardNumber") ? json["smartCardNumber"].ToString() : "";
                        Transfer.smartCardNumber = smartCardNumber;
                        string UnitsPurchased = json.ContainsKey("unitsPurchased") ? json["unitsPurchased"].ToString() : "";
                        Transfer.UnitsPurchased = UnitsPurchased;
                        string token = json.ContainsKey("token") ? json["token"].ToString() : "";
                        Transfer.token = token;
                        return  new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = Transfer };
                    }
                    else {
                       return  new BillPaymentResponse() { Success = true, Response = EnumResponse.NoRecordFound,Data = Transfer };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> GetTransferRecordByTransRefOrId(string clientKey, string transRef, string userName, string session,int ChannelId)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(userName, session, ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                   var TransferRecord = (await con.QueryAsync<TransferRecord>(@$"
                                                  SELECT 
                                                `Source_Account`,
                                                 TransID,
                                                 Transaction_Ref,
                                                `Destination_AccountName`,
                                                `Destination_Account`,
                                                `Destination_BankCode`,
                                                `Destination_BankName`,
                                                `Amount`,
                                                `Charge`,
                                                `Narration`,
                                                `createdon`,
                                                `Success`,
                                                `transtype`
                                              FROM `transfer` where Transaction_Ref=@Transaction_Ref             
                                                ",new {Transaction_Ref =transRef})).FirstOrDefault();
                    return new BillPaymentResponse() { Response = EnumResponse.Successful,Data=TransferRecord,Message="successful"};
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }

        }

        public async Task<GenericResponse> ReportTransaction(string clientKey, ReportTransaction request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(request.Username, request.Session,request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    request.Comment = _genServ.RemoveSpecialCharacters(request.Comment);
                    //check if transref exists 
                  string refId = (await con.QueryAsync<string>("select transactionref from reporttransaction where transactionref=@transactionref",new { transactionref= request.TransactionRefId })).FirstOrDefault();
                    if (!string.IsNullOrEmpty(refId)) {
                        return new GenericResponse() { Response = EnumResponse.TransactionIdAlreadyExists };
                    }  
                  string sql = $@"insert into reporttransaction(username,comment,transactionref,createdOn,amount,dateoftransaction) values(@username,@comment,@transactionref,@createdOn,@amount,@dateoftransaction)";
                    var userid = await _genServ.GetUserbyUsername(request.Username, con);
                    _logger.LogInformation("username " + request.Username);
                    await con.ExecuteAsync(sql, new
                    {
                        username = request.Username,
                        comment = request.Comment,
                        transactionref=request.TransactionRefId,
                        createdOn=DateTime.Now,
                        amount=request.Amount,
                        dateoftransaction=request.dateTime
                    });
                    GenericBLServiceHelper genericServiceHelper = new GenericBLServiceHelper();
                    CustomerDataNotFromBvn customerDataNotFromBvn = await genericServiceHelper.getCustomerData(con, "select PhoneNumber,Email from customerdatanotfrombvn where userid=" + userid.Id);
                    var CustomerDetails = await _genServ.GetCustomerbyAccountNo(request.SourceAccount);
                    // send sms and email
                    new Thread(async () => {
                        Thread.Sleep(50);
                      GenericResponse response  = await _smsBLService.SendSmsNotificationToCustomer("Transaction Report",customerDataNotFromBvn.PhoneNumber,$"your report has been forwarded to support on {DateTime.Now}.You can also call our customer service center.","Report");  // send sms
                        _logger.LogInformation("response "+response.Message);
                        // send mail
                        SendMailObject sendMailObject = new SendMailObject();
                        sendMailObject.Html = $@"<p>The Customer {CustomerDetails.result.firstname.ToUpper()} {CustomerDetails.result.lastname.ToUpper()} with {customerDataNotFromBvn.Email} and PhoneNumber {customerDataNotFromBvn.PhoneNumber}
                                                  did a transaction amount of NGN{request.Amount} on the date {request.dateTime} with accountnumber {request.SourceAccount} and phonenumber {customerDataNotFromBvn.PhoneNumber} has reported the following on this transaction:
                                                 </p>
                                                 <p> 
                                                  '{request.Comment}'
                                                 </p>
                                                  <p>Kindly respond as soon as possible</p>
                                                 ";
                        sendMailObject.Email = _settings.CustomerServiceEmail; // send mail to admin
                        sendMailObject.Subject = "TrustBanc J6 MFB-Transaction Report";
                        _genServ.SendMail(sendMailObject);
                      }).Start();
                    return new GenericResponse() { Message = "Successful", Response = EnumResponse.Successful };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }

        }

        public async Task<GenericResponse> GetBeneficiariesIucOrMeterNumber(string clientKey, string Username, string session, int channelId, int devicetype,string iucormeteraccount)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Username,session,channelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var usr = await _genServ.GetUserbyUsername(Username, con);
                    var meteroriucbenefificiaries = await con.QueryAsync<SaveBeneficiariesIucOrMeterNumber>($"select beneficiarytype,iucormeteraccount,Bankname,code,productname,custname from meteroriucbeneficiary where userId=@id and beneficiarytype=@beneficiarytype",new {id=usr.Id, beneficiarytype = devicetype});
                    if (!meteroriucbenefificiaries.Any())
                    {
                        return new BillPaymentResponse()
                        {
                            Response = EnumResponse.Successful,
                            Success = true,
                            Data = Enumerable.Empty<string>()
                    };
                    }
                    return new BillPaymentResponse() {Response=EnumResponse.Successful,
                        Success=true,Data=meteroriucbenefificiaries.ToList()};
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

    public async Task<GenericResponse> SaveBeneficiariesIucOrMeterNumber(string clientKey, SaveBeneficiariesIucOrMeterNumber request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(request.Username, request.Session,request.ChannelId,con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    string sql = "insert into meteroriucbeneficiary(userId,beneficiarytype,productname,iucormeteraccount,bankname,code,createdon,custname) " +
                        "values (@userId,@beneficiarytype,@productname,@iucormeteraccount,@bankname,@code,@createdon,@custname)";
                    var usr = await _genServ.GetUserbyUsername(request.Username, con);
                    _logger.LogInformation("username " + request.Username);
                    if (request.beneficiarytype!=1 && request.beneficiarytype!=2) {
                        return new GenericResponse() { Message = "Successful", Response = EnumResponse.InvalidBeneficiaryType,Success=false };
                    }
                   string BeneficiaryCheck  = (await con.QueryAsync<string>("select iucormeteraccount from meteroriucbeneficiary where iucormeteraccount=@iucormeteraccount", new { iucormeteraccount = request.iucormeteraccount})).FirstOrDefault();
                    if (!string.IsNullOrEmpty(BeneficiaryCheck)) {
                        return new GenericResponse() { Response = EnumResponse.Successful, Success = true };
                    }
                    await con.ExecuteAsync(sql, new
                    {
                        userId = usr.Id,
                        beneficiarytype = request.beneficiarytype,
                        productname = request.productname,
                        iucormeteraccount = request.iucormeteraccount,
                        bankname = request.Bankname,
                        code = request.code,
                        createdon=DateTime.Now,
                        custName=request.CustName
                    });
                    return new GenericResponse() { Message = "Successful", Response = EnumResponse.Successful,Success=true };
                }
            }catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }

        }

        public async Task<GenericResponse> DeleteBeneficiariesIucOrMeterNumber(string clientKey, string username, string iucOrMeterAccount, string session,  int channelId)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(username,session,channelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var usr = await _genServ.GetUserbyUsername(username,con);
                    string BeneficiaryCheck = (await con.QueryAsync<string>("select iucormeteraccount from meteroriucbeneficiary where iucormeteraccount=@iucormeteraccount and userId=@userId", new { iucormeteraccount = iucOrMeterAccount, userId=usr.Id })).FirstOrDefault();
                    if (string.IsNullOrEmpty(BeneficiaryCheck))
                    {
                        return new GenericResponse() { Response = EnumResponse.BeneficiaryNotFound, Success = false };
                    }
                    string sql = "delete from meteroriucbeneficiary where userId=@id and iucormeteraccount=@iucormeteraccount";
                    await con.ExecuteAsync(sql, new
                    {
                        id = usr.Id,
                        iucormeteraccount = iucOrMeterAccount
                    });
                    return new GenericResponse() { Response = EnumResponse.Successful,Success=true};
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        private bool makeTransactionLimitDecision(bool limitstatus, decimal cummulativevalue,
            string cummulativeAccountIndemnity, string cummulativeAccountAccountLimit)
        {
            if (decimal.Parse(cummulativeAccountIndemnity) != 0.0m) // meaning if he/she has indemnity
            {
                if (cummulativevalue < decimal.Parse(cummulativeAccountIndemnity))
                {
                    limitstatus = true;
                }
                else
                {
                    limitstatus = false;
                }
            }
            else if (decimal.Parse(cummulativeAccountAccountLimit) != 0.0m) // meaning if he/she has account limit
            {
                if (cummulativevalue < decimal.Parse(cummulativeAccountAccountLimit))
                {
                    limitstatus = true;
                }
                else
                {
                    limitstatus = false;
                }
            }
            return limitstatus;
        }

        private TransLimitValidation processTransValidation(decimal? cummulativevalue, CustomerIndemnitySetting customerIndemnitySetting,
            bool limitstatus, decimal Amount,
            AccountIndemnitySetting accountIndemnitySetting,
            CustomerLimitSetting customerLimitSetting, AccountLimitSetting accountLimitSetting)
           {
            IndemnityTransactionChecker indemnityTransactionChecker = new IndemnityTransactionChecker();
            if (customerIndemnitySetting!=null)
            {
                if (Amount > customerIndemnitySetting.SingleTransactionWithdrawalLimit)
                {
                    limitstatus = false;
                    GenericResponse2 genericResponse2 = new GenericResponse2() { Response = EnumResponse.CustomerIndemnityStatus, data = limitstatus };
                    return new TransLimitValidation(){ limitstatus=limitstatus, genericResponse2=genericResponse2, indemnityTransactionChecker= null };
                }
                else if (cummulativevalue.HasValue && cummulativevalue !=null && cummulativevalue!=default)
                {
                    if (cummulativevalue > customerIndemnitySetting.DailyTransactionWithdrawalLimit)
                    {
                        limitstatus = false;
                        GenericResponse2 genericResponse2 = new GenericResponse2() { Response = EnumResponse.cummulativeTierLimitExceeded, data = limitstatus };
                        return new TransLimitValidation() { limitstatus = limitstatus, genericResponse2 = genericResponse2 , indemnityTransactionChecker = null };
                    }
                }
                //set customerIndemnitySetting status to true 
                indemnityTransactionChecker.CustomerIndemnityStatusChecker = true;
            }
            if (accountIndemnitySetting!=null)
            {
                if (Amount > accountIndemnitySetting.SingleTransactionWithdrawalLimit) //account indemnity
                {
                    limitstatus = false;
                    GenericResponse2 genericResponse2 = new GenericResponse2() { Response = EnumResponse.AccountIndemnityStatus, data = limitstatus };
                    return new TransLimitValidation() { limitstatus = limitstatus, genericResponse2 = genericResponse2, indemnityTransactionChecker= null };
                }
                else if (cummulativevalue.HasValue && cummulativevalue != null && cummulativevalue != default)
                {
                    if (cummulativevalue > accountIndemnitySetting.DailyTransactionWithdrawalLimit)
                    {
                        limitstatus = false;
                        GenericResponse2 genericResponse2 = new GenericResponse2() { Response = EnumResponse.cummulativeTierLimitExceeded, data = limitstatus };
                        return new TransLimitValidation() { limitstatus = limitstatus, genericResponse2 = genericResponse2, indemnityTransactionChecker= null };
                    }
                }
                indemnityTransactionChecker.AccountIndemnityStatusChecker = true;
            }
           if(accountLimitSetting != null)
            {
                if (Amount > accountLimitSetting.SingleTransactionWithdrawalLimit)
                {
                    limitstatus = false;
                    GenericResponse2 genericResponse2 = new GenericResponse2() { Response = EnumResponse.SinglecummulativeTierLimitExceeded, data = limitstatus };
                    return new TransLimitValidation() { limitstatus = limitstatus, genericResponse2 = genericResponse2, indemnityTransactionChecker= null };
                }
                else if (cummulativevalue.HasValue && cummulativevalue != null && cummulativevalue != default)
                {
                    if (cummulativevalue > accountLimitSetting.DailyTransactionWithdrawalLimit)
                    {
                        limitstatus = false;
                        GenericResponse2 genericResponse2 = new GenericResponse2() { Response = EnumResponse.cummulativeTierLimitExceeded, data = limitstatus  };
                        return new TransLimitValidation() { limitstatus = limitstatus, genericResponse2 = genericResponse2,indemnityTransactionChecker=null };
                    }
                }
            }
            if (customerLimitSetting != null)
            {
                if (Amount > customerLimitSetting.SingleTransactionWithdrawalLimit) //customer  limit
                {
                    limitstatus = false;
                    GenericResponse2 genericResponse2 = new GenericResponse2() { Response = EnumResponse.CustomerLimitStatus, data = limitstatus };
                    return new TransLimitValidation() { limitstatus = limitstatus, genericResponse2 = genericResponse2, indemnityTransactionChecker = null };
                }
                else if (cummulativevalue.HasValue && cummulativevalue != null && cummulativevalue != default)
                {
                    if (cummulativevalue > customerLimitSetting.DailyTransactionWithdrawalLimit)
                    {
                        limitstatus = false;
                        GenericResponse2 genericResponse2 = new GenericResponse2() { Response = EnumResponse.cummulativeTierLimitExceeded, data = limitstatus };
                        return new TransLimitValidation() { limitstatus = limitstatus, genericResponse2 = genericResponse2, indemnityTransactionChecker= null };
                    }
                }
            }
            GenericResponse2 genericResponse = new GenericResponse2() { Response = EnumResponse.Successful, data =limitstatus,Success=true };
            return new TransLimitValidation() { limitstatus = true, genericResponse2 = genericResponse,indemnityTransactionChecker=indemnityTransactionChecker};
        }

          //check daily withdrawal transactionlimit
        public async Task<TransLimitValidation> CheckCustomerDailyandSingleTransactionLimit(string clientKey, string username, string session, int channelId,decimal Amount,string AccountNumber)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    _logger.LogInformation("_tier1AccountLimitInfo " + JsonConvert.SerializeObject(_tier1AccountLimitInfo));
                    _logger.LogInformation("_tier2AccountLimitInfo " + JsonConvert.SerializeObject(_tier2AccountLimitInfo));
                    _logger.LogInformation("_tier3AccountLimitInfo " + JsonConvert.SerializeObject(_tier3AccountLimitInfo));
                    var usr = await _genServ.GetUserbyUsername(username, con);
                    //  List<AccountIndemnitySetting> accountIndemnitySettings = (await con.QueryAsync<AccountIndemnitySetting>("SELECT AccountNumber, " + "CAST(Singlewithdrawaltransactionlimit AS DECIMAL(18, 2)) AS Singlewithdrawaltransactionlimit, " + "CAST(Dailywithdrawaltransactionlimit AS DECIMAL(18, 2)) AS Dailywithdrawaltransactionlimit " + "FROM customerindemnity " + "WHERE AccountNumber IN @list AND IndemnityType = @IndemnityType", new { list = accountList, IndemnityType = "accountindemnity" })).ToList();
                    string query = $@"select sum(CAST(COALESCE(Amount, '0') AS DECIMAL(18, 2))) as amount 
                                    from transfer 
                                    where User_Id=@userid 
                                      and DATE(createdOn)=curdate() 
                                      ";
                   // var cummulativevalue = (await con.QueryAsync<decimal>("select sum(CAST(Amount AS DECIMAL(18, 2))) as amount from transfer where User_Id=@userid and createdOn=curdate();", new { userid = usr.Id })).FirstOrDefault();
                    var cummulativevalue = (await con.QueryAsync<decimal?>(query, new { userid = usr.Id })).FirstOrDefault();
                    BalanceEnquiryResponse balanceEnquiryResponse = await _genServ.GetAccountbyCustomerId(usr.CustomerId);
                    string kyclevel = balanceEnquiryResponse.balances.ElementAtOrDefault(0).kycLevel;
                    bool limitstatus = true;
                    _logger.LogInformation("kyclevel "+ kyclevel);
                    _logger.LogInformation("cummulativevalue " + cummulativevalue);
                    // Query for account indemnity settings
                    var accountIndemnitySetting = (await con.QueryAsync<AccountIndemnitySetting>(
                                                    @"SELECT 
                                    Singlewithdrawaltransactionlimit AS SingleTransactionWithdrawalLimit,
                                    Dailywithdrawaltransactionlimit AS DailyTransactionWithdrawalLimit,
                                    AccountNumber 
                                  FROM customerindemnity 
                                  WHERE 
                                    userid = @userid 
                                    AND indemnityapproval = TRUE 
                                    AND AccountNumber = @AccountNumber 
                                    AND IndemnityType = 'accountindemnity';",
                        new { AccountNumber = AccountNumber, userid = usr.Id }
                    )).FirstOrDefault();
                    _logger.LogInformation("accountIndemnitySetting " + JsonConvert.SerializeObject(accountIndemnitySetting));
                    // Query for customer indemnity settings
                    var customerIndemnitySetting = (await con.QueryAsync<CustomerIndemnitySetting>(
                                            @"SELECT 
                            Singlewithdrawaltransactionlimit AS SingleTransactionWithdrawalLimit,
                            Dailywithdrawaltransactionlimit AS DailyTransactionWithdrawalLimit 
                          FROM customerindemnity 
                          WHERE 
                            userid = @userid 
                            AND indemnityapproval = TRUE 
                            AND (AccountNumber IS NULL OR AccountNumber = '') 
                            AND IndemnityType = 'customerindemnity';",
                                            new { userid = usr.Id }
                    )).FirstOrDefault();
                    _logger.LogInformation("customerIndemnitySetting " + JsonConvert.SerializeObject(customerIndemnitySetting));
                    // Query for account transaction limit settings
                    var accountLimitSettings = (await con.QueryAsync<AccountLimitSetting>(
                                            @"SELECT 
                            Dailywithdrawaltransactionlimit AS DailyTransactionWithdrawalLimit,
                            Singlewithdrawaltransactionlimit AS SingleTransactionWithdrawalLimit,
                            AccountNumber 
                          FROM customertransactionlimit 
                          WHERE 
                            userid = @userid 
                            AND AccountNumber = @AccountNumber 
                            AND LimitType = 'accountlimit' AND limitapproval=TRUE;",
                                            new { AccountNumber = AccountNumber, userid = usr.Id }
                    )).FirstOrDefault();
                    _logger.LogInformation("accountLimitSettings " + JsonConvert.SerializeObject(accountLimitSettings));
                    // Query for customer transaction limit settings
                    var customerLimitSettings = (await con.QueryAsync<CustomerLimitSetting>(
                                                @"SELECT 
                                Dailywithdrawaltransactionlimit AS DailyTransactionWithdrawalLimit,
                                Singlewithdrawaltransactionlimit AS SingleTransactionWithdrawalLimit 
                              FROM customertransactionlimit 
                              WHERE 
                                userid = @userid 
                                AND (AccountNumber IS NULL OR AccountNumber = '') 
                                AND LimitType = 'customerlimit' AND limitapproval=TRUE;",
                        new { userid = usr.Id }
                    )).FirstOrDefault();
                    _logger.LogInformation("customerLimitSettings " + JsonConvert.SerializeObject(customerLimitSettings));
                    if (kyclevel.Equals(_tier1AccountLimitInfo.kycLevel, StringComparison.CurrentCultureIgnoreCase))
                    {
                        // limitstatus = makeTransactionLimitDecision(limitstatus,cummulativevalue, cummulativeAccountIndemnity, cummulativeAccountAccountLimit);
                        //customer indemnity level
                        var response = processTransValidation(cummulativevalue,
                            customerIndemnitySetting,limitstatus,
                            Amount,accountIndemnitySetting,customerLimitSettings,accountLimitSettings);
                        _logger.LogInformation("validation response "+JsonConvert.SerializeObject(response));
                      IndemnityTransactionChecker indemnityTransactionChecker =   response.indemnityTransactionChecker;
                        if (indemnityTransactionChecker!=null) {
                            if (indemnityTransactionChecker.CustomerIndemnityStatusChecker && indemnityTransactionChecker.AccountIndemnityStatusChecker) {
                                return response;
                            }
                        }
                        if(!((bool)response.genericResponse2.data))
                        {
                            return response;
                        }
                        if (Amount > decimal.Parse(_accountChannelLimit.SingleTransactionWithdrawalLimit))
                        {
                            limitstatus = false;
                            GenericResponse2 g = new GenericResponse2() { Response = EnumResponse.TransactionLimitOutOfRange, data = limitstatus };
                            return new TransLimitValidation() { limitstatus = limitstatus, genericResponse2 = g };
                            //  return new GenericResponse2() { Response = EnumResponse.TransactionLimitOutOfRange };
                        }
                        _logger.LogInformation("proceding to check tier status "+cummulativevalue);
                        if (cummulativevalue.HasValue && cummulativevalue > decimal.Parse(_tier1AccountLimitInfo.Dailywithdrawaltransactionlimit))
                        {
                            limitstatus = false;
                            GenericResponse2 g = new GenericResponse2() { Response = EnumResponse.cummulativeTierLimitExceeded, data = limitstatus };
                            return new TransLimitValidation() {limitstatus= limitstatus, genericResponse2 =g};
                        }
                    }
                    else if (kyclevel.Equals(_tier2AccountLimitInfo.kycLevel, StringComparison.CurrentCultureIgnoreCase))
                    {
                        var response = processTransValidation(cummulativevalue,
                            customerIndemnitySetting, limitstatus,
                            Amount, accountIndemnitySetting, customerLimitSettings, accountLimitSettings);
                        _logger.LogInformation("validation response " + JsonConvert.SerializeObject(response));
                        //if (!response.limitstatus)
                        IndemnityTransactionChecker indemnityTransactionChecker = response.indemnityTransactionChecker;
                        if (indemnityTransactionChecker != null)
                        {
                            if (indemnityTransactionChecker.CustomerIndemnityStatusChecker && indemnityTransactionChecker.AccountIndemnityStatusChecker)
                            {
                                return response;
                            }
                        }
                        if (!((bool)response.genericResponse2.data))
                        {
                            return response;
                        }
                        if (Amount > decimal.Parse(_accountChannelLimit.SingleTransactionWithdrawalLimit))
                        {
                            limitstatus = false;
                            GenericResponse2 g = new GenericResponse2() { Response = EnumResponse.TransactionLimitOutOfRange, data = limitstatus };
                            return new TransLimitValidation() { limitstatus = limitstatus, genericResponse2 = g };
                            //  return new GenericResponse2() { Response = EnumResponse.TransactionLimitOutOfRange };
                        }
                        if (cummulativevalue.HasValue && cummulativevalue > decimal.Parse(_tier2AccountLimitInfo.Dailywithdrawaltransactionlimit))
                        {
                            limitstatus = false;
                            GenericResponse2 g = new GenericResponse2() { Response = EnumResponse.cummulativeTierLimitExceeded, data = limitstatus };
                            return new TransLimitValidation() { limitstatus = limitstatus, genericResponse2 = g };
                        }
                    }
                    else if (kyclevel.Equals(_tier3AccountLimitInfo.kycLevel, StringComparison.CurrentCultureIgnoreCase))
                    {
                        var response = processTransValidation(cummulativevalue,
                             customerIndemnitySetting, limitstatus,
                             Amount, accountIndemnitySetting, customerLimitSettings, accountLimitSettings);
                        _logger.LogInformation("validation response " + JsonConvert.SerializeObject(response));
                        _logger.LogInformation("response from limit and indemnity settings " + JsonConvert.SerializeObject(customerLimitSettings));
                        // if (!response.limitstatus)
                        IndemnityTransactionChecker indemnityTransactionChecker = response.indemnityTransactionChecker;
                        if (indemnityTransactionChecker != null)
                        {
                            if (indemnityTransactionChecker.AccountIndemnityStatusChecker)
                            {
                                return response;
                            }
                            if (indemnityTransactionChecker.CustomerIndemnityStatusChecker)
                            {
                                return response;
                            }
                        }
                        if (!((bool)response.genericResponse2.data))
                        {
                            return response;
                        }
                        if (Amount > decimal.Parse(_accountChannelLimit.SingleTransactionWithdrawalLimit))
                        {
                            limitstatus = false;
                            GenericResponse2 g = new GenericResponse2() { Response = EnumResponse.TransactionLimitOutOfRange, data = limitstatus };
                            return new TransLimitValidation() { limitstatus = limitstatus, genericResponse2 = g };
                          //  return new GenericResponse2() { Response = EnumResponse.TransactionLimitOutOfRange };
                        }
                        if (cummulativevalue.HasValue && cummulativevalue > decimal.Parse(_tier3AccountLimitInfo.Dailywithdrawaltransactionlimit))
                        {
                            limitstatus = false;
                            GenericResponse2 g = new GenericResponse2() { Response = EnumResponse.cummulativeTierLimitExceeded, data = limitstatus };
                            return new TransLimitValidation() { limitstatus = limitstatus, genericResponse2 = g };
                        }
                    }
                    GenericResponse2 g2 = new GenericResponse2() { Response = EnumResponse.TierLimitExceeded, data = limitstatus };
                    return new TransLimitValidation() { limitstatus = true, genericResponse2 = g2};
                }
            }
            catch (Exception ex)
            {
                //limitstatus = false;
                _logger.LogInformation("exception "+ex.Message);
                GenericResponse2 g3 = new GenericResponse2() { Response = EnumResponse.TierLimitExceeded };
                return new TransLimitValidation() { limitstatus = false, genericResponse2 = g3 };
            }
        }
        public async Task<GenericResponse> GetAccountLimitPerDay(decimal Amount,string Account,string username,string Session,int ChannelID)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(username, Session, ChannelID, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var usr = await _genServ.GetUserbyUsername(username, con);
                    BalanceEnquiryResponse balanceEnquiryResponse = await _genServ.GetAccountbyCustomerId(usr.CustomerId);
                    string kyclevel = balanceEnquiryResponse.balances.ElementAt(0).kycLevel;
                    string url = "api/Enquiry/GetAccountLimitPerDay/" + Account;
                    var resp = await _genServ.CallServiceAsyncToString(Method.GET, $"{_settings.FinedgeUrl}{url}", null, true);
                    //Console.WriteLine("resp " + resp);
                    GenericResponse2 genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(resp);
                    Console.WriteLine(JsonConvert.SerializeObject(genericResponse2.data));
                    if (resp == null || resp == "")
                    {
                        return new GenericResponse() { Success = false, Response = EnumResponse.NoRecordFound };
                    }
                    GetTransactionLimitPerDay getTransactionLimitPerDay = JsonConvert.DeserializeObject<GetTransactionLimitPerDay>(JsonConvert.SerializeObject(genericResponse2.data));
                    decimal TotalTransactions = getTransactionLimitPerDay.TotalTransaction;
                    if (TotalTransactions==00) {
                        return new PrimeAdminResponse()
                        {
                            Success = true,
                            Response = EnumResponse.Successful,
                            Data = new { Status = true }
                        };
                    }
                    if (kyclevel.Equals("001", StringComparison.CurrentCultureIgnoreCase))
                    {
                        if (TotalTransactions > Decimal.Parse(_settings.Tier1CummulativeMax))
                        {
                            return new PrimeAdminResponse()
                            {
                                Success = true,
                                Response = EnumResponse.Successful,
                                Data = new { Status = false }
                            };
                        }
                        else
                        {
                            return new PrimeAdminResponse()
                            {
                                Success = true,
                                Response = EnumResponse.Successful,
                                Data = new { Status = false }
                            };
                        }
                    }
                    else if (kyclevel.Equals("002", StringComparison.CurrentCultureIgnoreCase))
                    {
                        if (TotalTransactions > Decimal.Parse(_settings.Tier2CummulativeMax))
                        {
                            return new PrimeAdminResponse()
                            {
                                Success = true,
                                Response = EnumResponse.Successful,
                                Data = new { Status = true }
                            };
                        }
                        else
                        {
                            return new PrimeAdminResponse()
                            {
                                Success = true,
                                Response = EnumResponse.Successful,
                                Data = new { Status = false }
                            };
                        }
                    }
                    else if(kyclevel.Equals("003", StringComparison.CurrentCultureIgnoreCase))
                    {
                        if (TotalTransactions > Decimal.Parse(_settings.Tier3CummulativeMax))
                        {
                            return new PrimeAdminResponse()
                            {
                                Success = true,
                                Response = EnumResponse.Successful,
                                Data = new { Status = true }
                            };
                        }
                        else
                        {
                            return new PrimeAdminResponse()
                            {
                                Success = true,
                                Response = EnumResponse.Successful,
                                Data = new { Status = false }
                            };
                        }
                        //  return null;
                    }
                    return null;
                }
            }catch(Exception ex) { 
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse2> ValidateCustomerPin(string clientKey, PinValidationChecker request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var usr = await _genServ.GetUserbyUsername(request.username,con);
                    var validateSession = await _genServ.ValidateSession(usr.Id,request.Session,request.Channelid, con);
                    if (!validateSession)
                        return new GenericResponse2() { Response = EnumResponse.InvalidSession };
                        var transPin = await _genServ.GetUserCredential(CredentialType.TransactionPin, usr.Id, con);
                        Console.WriteLine(" transPin " + transPin);
                        string enterpin = _genServ.EncryptString(request.Pin);
                        _logger.LogInformation("Pin " + String.Equals(enterpin, transPin));
                        if (enterpin != transPin)
                        {
                         return new GenericResponse2() { Response=EnumResponse.InvalidTransactionPin };
                        }
                    return new GenericResponse2() { Response = EnumResponse.Successful, data = new {IsConfirm=true},Success=true}; ;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }
        private class MyBankListFromAccountNumber
        {
            public List<Dictionary<string, string>> GetLikeBank(List<AccessBankList>list , string accountNumber)
            {

                var Banks = new List<Dictionary<string, string>>();
                list.ForEach(x => {
                    Console.WriteLine("adding to dict");
                    Banks.Add(new Dictionary<string, string> { { "name", x.Bankname }, { "code", x.BankCode } });
                    Console.WriteLine("added to dict");
                });
                var algo = new List<int> { 3, 7, 3, 3, 7, 3, 3, 7, 3, 3, 7, 3 };

                var accountList = accountNumber.ToCharArray();
                var integerOfAccount = accountList.Select(c => (int)char.GetNumericValue(c)).ToList();

                var codeList = Banks.Select(bank => bank["code"].ToCharArray().Select(c => c.ToString()).ToList()).ToList();
               // Console.WriteLine("Bank "+Banks.Count+" codeList data "+codeList.Count);
                var dataSet = new List<Dictionary<string, string>>();
                foreach (var code in codeList)
                {
                    var data = new Dictionary<string, string>();
                    var d = code.Select(c => int.Parse(c)).ToList();
                    var allList = d.Concat(integerOfAccount.Take(9)).ToList();
                    int checksum = integerOfAccount.Last();
                    int total = 0;
                    var resList = algo.Select((t, i) => t * allList[i]).ToList();
                    total = resList.Sum();
                    int mod = total % 10;
                    int check = 10 - mod;
                    var codeStr = string.Join("", code.Take(3));
                    if (check == checksum || check == 10)
                    {
                        data["code"] = codeStr;
                        dataSet.Add(data);
                    }
                   // Console.WriteLine("codelist ...."+dataSet.Count);
                }
                var finalResult = new List<Dictionary<string, string>>();
                Console.WriteLine("Banks " + JsonConvert.SerializeObject(Banks));
                Console.WriteLine("dataSet " +JsonConvert.SerializeObject(dataSet));
                foreach (var codes in dataSet)
                {
                    foreach (var bank in Banks)
                    {
                        if (bank["code"] != codes["code"]) continue;
                        var valueData = new Dictionary<string, string>
                      {
                    { "code", bank["code"] },
                    { "name", bank["name"] }
                };
                        finalResult.Add(valueData);
                    }
                }
                return finalResult;
            }
        }
        public class FinedgePostingResponse3
        {
            public string status { get; set; }

            public string statuscode { get; set; }

            public Response response { set; get; }
        }
        public class Response
    {
        public int retVal { set; get; }
        public string retMsg { set; get; }
        public string PostingSequence { set; get; }
        public string postdate { set; get; }
        public string RequestID { set; get; }
        public string TranAmt { set; get; }
    }
    }
    
}
