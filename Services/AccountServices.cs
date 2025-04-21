using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using System.Linq;
using System.Data;
using System;
using System.Threading.Tasks;
using RestSharp;
using System.Collections.Generic;
using RestSharp.Serialization.Json;
using Newtonsoft.Json;
using System.Globalization;
using Dapper;
using Retailbanking.Common.DbObj;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
using System.Drawing;
using Google.Protobuf.WellKnownTypes;
using iText.StyledXmlParser.Jsoup.Select;
using StackExchange.Redis;
using System.Runtime.InteropServices;
using System.Transactions;
using Method = RestSharp.Method;

namespace Retailbanking.BL.Services
{
    public class AccountServices : IAccounts
    {
        private readonly ILogger<AccountServices> _logger;
        private readonly AppSettings _settings;
        private readonly IGeneric _genServ;
        private readonly IMemoryCache _cache;
        private readonly DapperContext _context;
        private readonly CustomerChannelLimit _customerChannelLimit;
        private readonly AccountChannelLimit _accountChannelLimit;

        public AccountServices(IOptions<AccountChannelLimit> accountChannelLimit, IOptions<CustomerChannelLimit> customerChannelLimit, ILogger<AccountServices> logger, IOptions<AppSettings> options, IGeneric genServ, IMemoryCache memoryCache, DapperContext context)
        {
            _logger = logger;
            _settings = options.Value;
            _customerChannelLimit = customerChannelLimit.Value;
            _accountChannelLimit = accountChannelLimit.Value;
            _genServ = genServ;
            _cache = memoryCache;
            _context = context;
        }

        public async Task<FetchAccounts> FetchAccounts(string ClientKey, GenericRequest Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                  var validateSession = await _genServ.ValidateSession(Request.Username, Request.Session, Request.ChannelId, con);
                   if (!validateSession)
                      return new FetchAccounts() { Response = EnumResponse.InvalidSession };

                    var usr = await _genServ.GetUserbyUsername(Request.Username, con);

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
                                LedgerBalance = Math.Round(n.totalBalance, 2),
                                AccountTier= n.kycLevel
                            });
                        if (accts.Any())
                        {
                            List<string> accountList = accts.Select(x => x.AccountNumber).ToList();
                            List<AccountIndemnitySetting> accountIndemnitySettings = (await con.QueryAsync<AccountIndemnitySetting>("select AccountNumber,Singlewithdrawaltransactionlimit as SingleTransactionWithdrawalLimit,Dailywithdrawaltransactionlimit as DailyTransactionWithdrawalLimit from customerindemnity where AccountNumber in @list and IndemnityType=@IndemnityType and userid=@userid", new { userid=usr.Id,list = accountList, IndemnityType = "accountindemnity" })).ToList();
                            List<CustomerIndemnitySetting> customerIndemnitySettings = (await con.QueryAsync<CustomerIndemnitySetting>("select AccountNumber,Singlewithdrawaltransactionlimit as SingleTransactionWithdrawalLimit,Dailywithdrawaltransactionlimit as DailyTransactionWithdrawalLimit from customerindemnity where IndemnityType=@IndemnityType and userid=@userid", new { userid = usr.Id, IndemnityType = "customerindemnity" })).ToList();
                            string limitquery = $@"SELECT AccountNumber, 
                                                    CAST(TRIM(BOTH ' ' FROM Singlewithdrawaltransactionlimit) AS DECIMAL(18, 2)) AS Singlewithdrawaltransactionlimit, 
                                                    CAST(TRIM(BOTH ' ' FROM Dailywithdrawaltransactionlimit) AS DECIMAL(18, 2)) AS Dailywithdrawaltransactionlimit 
                                            FROM customertransactionlimit 
                                            WHERE AccountNumber IN @list AND LimitType = @LimitType and userid=@userid;";
                            List<AccountLimitSetting> accountLimitSettings = (await con.QueryAsync<AccountLimitSetting>("select AccountNumber,Singlewithdrawaltransactionlimit as SingleTransactionWithdrawalLimit,Dailywithdrawaltransactionlimit as DailyTransactionWithdrawalLimit from customertransactionlimit where AccountNumber in @list and LimitType=@LimitType and userid=@userid", new {userid=usr.Id, list = accountList, LimitType = "accountlimit" })).ToList();
                            //List<AccountLimitSetting> accountLimitSettings = (await con.QueryAsync<AccountLimitSetting>(limitquery, new { userid = usr.Id, list = accountList, LimitType = "accountlimit" })).ToList();
                            string customerlimitquery = $@"SELECT AccountNumber, 
                                                    CAST(TRIM(BOTH ' ' FROM Singlewithdrawaltransactionlimit) AS DECIMAL(18, 2)) AS Singlewithdrawaltransactionlimit, 
                                                    CAST(TRIM(BOTH ' ' FROM Dailywithdrawaltransactionlimit) AS DECIMAL(18, 2)) AS Dailywithdrawaltransactionlimit 
                                            FROM customertransactionlimit 
                                            WHERE LimitType = @LimitType and userid=@userid;";
                            List<CustomerLimitSetting> customerLimitSettings = (await con.QueryAsync<CustomerLimitSetting>("select AccountNumber,Singlewithdrawaltransactionlimit as SingleTransactionWithdrawalLimit,Dailywithdrawaltransactionlimit as DailyTransactionWithdrawalLimit from customertransactionlimit where LimitType=@LimitType and userid=@userid", new { userid=usr.Id,LimitType = "customerlimit" })).ToList();
                           // List<CustomerLimitSetting> customerLimitSettings = (await con.QueryAsync<CustomerLimitSetting>(customerlimitquery, new { userid = usr.Id, LimitType = "customerlimit" })).ToList();
                            //update accts with each accountnumber limit and indemnity
                            accts.ForEach(x =>
                            {
                                //accountindemnity
                                x.AccountLimit = new AccountLimitSetting();
                                x.AccountLimit.SingleTransactionWithdrawalLimit =decimal.Parse(_accountChannelLimit.SingleTransactionWithdrawalLimit);
                                x.AccountLimit.DailyTransactionWithdrawalLimit= decimal.Parse(_accountChannelLimit.DailyTransactionWithdrawalLimit);
                                x.CustomerLimit = new CustomerLimitSetting();
                                x.CustomerLimit.SingleTransactionWithdrawalLimit = decimal.Parse(_customerChannelLimit.SingleTransactionWithdrawalLimit);
                                x.CustomerLimit.DailyTransactionWithdrawalLimit = decimal.Parse(_customerChannelLimit.DailyTransactionWithdrawalLimit); ;
                                x.AccountIndemnity = new AccountIndemnitySetting();
                                x.CustomerIndemnity = new CustomerIndemnitySetting();
                                if (accountIndemnitySettings.Any())
                                 {
                                accountIndemnitySettings.ForEach(y =>
                                {
                                    if (x.AccountNumber.Equals(y?.AccountNumber))
                                    {
                                        x.AccountIndemnity.AccountNumber = y?.AccountNumber ?? "";
                                        x.AccountIndemnity.SingleTransactionWithdrawalLimit = y.SingleTransactionWithdrawalLimit;
                                        x.AccountIndemnity.DailyTransactionWithdrawalLimit = y.DailyTransactionWithdrawalLimit;
                                    }
                                });
                            }
                            //customerindemnity
                            if(customerIndemnitySettings.Any())
                            {
                                customerIndemnitySettings.ForEach(y =>
                                {
                                   // if (x.AccountNumber.Equals(y.AccountNumber))
                                   // {

                                        x.CustomerIndemnity.AccountNumber = y.AccountNumber != null ? y.AccountNumber : "";
                                        x.CustomerIndemnity.SingleTransactionWithdrawalLimit = y.SingleTransactionWithdrawalLimit;
                                        x.CustomerIndemnity.DailyTransactionWithdrawalLimit = y.DailyTransactionWithdrawalLimit;
                                   // }
                                });
                            }
                            //accountlimit
                            if(accountLimitSettings.Any())
                            {
                                    accountLimitSettings.ForEach(y =>
                                    {
                                        Console.WriteLine($"{x.AccountNumber} account limit ..y={y?.AccountNumber}");

                                        if (x.AccountNumber.Equals(y?.AccountNumber))
                                        {
                                            Console.WriteLine($"{JsonConvert.SerializeObject(x)} entered account limit ...."+ y?.AccountNumber);
                                            x.AccountLimit.AccountNumber = y.AccountNumber;
                                            Console.WriteLine("console account limit ....");
                                            x.AccountLimit.SingleTransactionWithdrawalLimit = y.SingleTransactionWithdrawalLimit;
                                            x.AccountLimit.DailyTransactionWithdrawalLimit = y.DailyTransactionWithdrawalLimit;
                                        }
                                    });

                                }
                                //customerlimit
                                if (customerLimitSettings.Any())
                                {
                                 
                                    customerLimitSettings.ForEach(y =>
                                    {
                                        Console.WriteLine("customer limit ..y= " + y.AccountNumber);
                                        // if (x.AccountNumber.Equals(y.AccountNumber))
                                        //{
                                        x.CustomerLimit.AccountNumber = y.AccountNumber != null ? y.AccountNumber : "";
                                            x.CustomerLimit.SingleTransactionWithdrawalLimit = y.SingleTransactionWithdrawalLimit;
                                            x.CustomerLimit.DailyTransactionWithdrawalLimit = y.DailyTransactionWithdrawalLimit;
                                        //}
                                    });
                                 }
                            });
                       
                        }
                        return new FetchAccounts()
                        {
                            Accounts = accts,
                            Response = EnumResponse.Successful,
                            Success = true,
                            //AirtimeBills = await _genServ.GetAirtimeBillsLimit(usr.RequestReference, con)
                        };
                    }
                   // return new FetchAccounts() { Response = EnumResponse.NoAccountExist };
                    return new FetchAccounts() { Response = EnumResponse.SystemError };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new FetchAccounts() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<FetchTransactions> FetchTransactions(string ClientKey, TransactionHistoryRequest Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Request.Username, Request.Session, Request.ChannelId, con);
                    if (!validateSession)
                        return new FetchTransactions() { Response = EnumResponse.InvalidSession };
                   // Console.WriteLine("request object"+JsonConvert.SerializeObject(Request));
                   Request.StartDate= DateTime.ParseExact(Request.StartDate, "dd-MM-yyyy", CultureInfo.InvariantCulture)
                        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    Request.EndDate = DateTime.ParseExact(Request.EndDate, "dd-MM-yyyy", CultureInfo.InvariantCulture)
                        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    Console.WriteLine("request object" + JsonConvert.SerializeObject(Request));
                    var request = new TransHistoryRequest()
                    {
                        EndDate = Request.EndDate,
                        accountnumber = Request.AccountNumber,
                        StartDate = Request.StartDate
                    };

                    var header = new Dictionary<string, string>
                   {
                    { "ClientKey", _settings.FinedgeKey }
                   };

                    var resp = await _genServ.CallServiceAsync<TransHistoryResult>(RestSharp.Method.POST, $"{_settings.FinedgeUrl}api/enquiry/TransactionHistory", request, false, header);
                   // var resp = await _genServ.CallServiceAsync<TransHistoryResult>(Method.POST, $"{_settings.FinedgeUrl}api/enquiry/TransactionHistory2", request, false, header);
                    _logger.LogInformation("resp ..."+resp.ToString()+" and "+JsonConvert.SerializeObject(resp));
                    if (!resp.Success)
                        return new FetchTransactions() { Response = EnumResponse.NotSuccessful };
                    var trns = new List<TransactionHistory>();

                    foreach (var n in resp.Result)
                        trns.Add(new TransactionHistory()
                        {
                            Amount = n.credit == 0 ? Math.Round(n.debit, 2) : Math.Round(n.credit, 2),
                            DrCr = n.credit == 0 ? "DR" : "CR",
                            Narration = n.narration,
                            TransactionDate = n.valueDate,
                            TransactionId = n.postseq,
                            BookBalance = n.bkBalance,
                            tranDate = n.tranDate,
                            Tranname = n.Tranname
                        });

                    return new FetchTransactions()
                    {
                        Response = EnumResponse.Successful,
                        Success = true,
                        Transactions = trns.Any() ? trns.Take(20).ToList() : trns
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new FetchTransactions() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }


        public async Task<FetchTransactions2> FetchTransactions2(string ClientKey, TransactionHistoryRequest Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Request.Username, Request.Session, Request.ChannelId, con);
                    if (!validateSession)
                        return new FetchTransactions2() { Response = EnumResponse.InvalidSession };
                    // Console.WriteLine("request object"+JsonConvert.SerializeObject(Request));
                    Request.StartDate = DateTime.ParseExact(Request.StartDate, "dd-MM-yyyy", CultureInfo.InvariantCulture)
                         .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    Request.EndDate = DateTime.ParseExact(Request.EndDate, "dd-MM-yyyy", CultureInfo.InvariantCulture)
                        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    Console.WriteLine("request object" + JsonConvert.SerializeObject(Request));
                    var request = new TransHistoryRequest()
                    {
                        EndDate = Request.EndDate,
                        accountnumber = Request.AccountNumber,
                        StartDate = Request.StartDate
                    };

                    var header = new Dictionary<string, string>
                   {
                    { "ClientKey", _settings.FinedgeKey }
                   };

                   // var resp = await _genServ.CallServiceAsync<TransHistoryResult>(Method.POST, $"{_settings.FinedgeUrl}api/enquiry/TransactionHistory", request, false, header);
                    var resp = await _genServ.CallServiceAsync<TransHistoryResult2>(Method.POST, $"{_settings.FinedgeUrl}api/enquiry/TransactionHistory2", request, false, header);
                    _logger.LogInformation("resp ..." + resp.ToString() + " and " + JsonConvert.SerializeObject(resp));
                    if (!resp.Success)
                        return new FetchTransactions2() { Response = EnumResponse.NotSuccessful };
                    var trns = new List<TransactionHistory2>();

                    foreach (var n in resp.Result)
                        trns.Add(new TransactionHistory2()
                        {
                            Amount = n.CreditAcct == 0 ? Math.Round(n.DebitAcct, 2) : Math.Round(n.CreditAcct, 2),
                            DrCr = n.CreditAcct == 0 ? "DR" : "CR",
                            Narration = n.narration,
                            valueDate = n.valueDate,
                            TransactionId = n.postseq,
                            BookBalance = n.bkBalance,
                            tranDate = n.tranDate,
                            Tranname = n.Tranname
                        });
                    /*
                    return new FetchTransactions2()
                    {
                        Response = EnumResponse.Successful,
                        Success = true,
                        Transactions = trns.Any() ? trns.Take(20).ToList() : trns
                    };
                    */
                    return new FetchTransactions2()
                    {
                        Response = EnumResponse.Successful,
                        Success = true,
                        Transactions = trns.Any() ? trns.ToList() : trns
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new FetchTransactions2() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<FileProxy> DownloadStatement(string ClientKey, TransactionHistoryRequest Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.CheckIfUserIsLoggedIn(Request.Username, Request.ChannelId, con);
                    if (!validateSession)
                    {
                        _logger.LogInformation("Statement session expired ......");
                       // return new FileProxy() { Response = EnumResponse.InvalidSession };
                    }
                    var dtRange = GetDateRange(Request.StartDate, Request.EndDate);
                    var request = new TransHistoryRequest()
                    {
                        EndDate = Request.EndDate,
                        accountnumber = Request.AccountNumber,
                        StartDate = Request.StartDate
                    };

                    var header = new Dictionary<string, string>
                {
                    { "ClientKey", _settings.FinedgeKey }
                };
                    _logger.LogInformation("calling api here ");
                    var resp = await _genServ.CallServiceAsync<FileProxy>(Method.POST, $"{_settings.FinedgeUrl}api/enquiry/transactionhistory", request,true, header);
                    //var email = await con.QueryAsync<string>($"select email from  users where accountnumber= {}");
                    if (resp.Success)
                        return resp;
                    resp.Response = EnumResponse.NotSuccessful;
                    return resp;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new FileProxy() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }


        public async Task<FileProxy> DownloadStatement2(string ClientKey, TransactionHistoryRequest Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.CheckIfUserIsLoggedIn(Request.Username, Request.ChannelId, con);
                    if (!validateSession)
                        return new FileProxy() { Response = EnumResponse.InvalidSession };

                    var dtRange = GetDateRange(Request.StartDate, Request.EndDate);
                    var request = new TransHistoryRequest()
                    {
                        EndDate = Request.EndDate,
                        accountnumber = Request.AccountNumber,
                        StartDate = Request.StartDate
                    };

                    var header = new Dictionary<string, string>
                {
                    { "ClientKey", _settings.FinedgeKey }
                };
                    Console.WriteLine("calling api here ");
                    var resp = await _genServ.CallServiceAsync<FileProxy>(Method.POST, $"{_settings.FinedgeUrl}api/enquiry/transactionhistory", request, false, header);
                   
                    if (resp.Success)
                        return resp;
                    resp.Response = EnumResponse.NotSuccessful;
                    return resp;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new FileProxy() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        private DateRange GetDateRange(string StartDate, string EndDate)
        {
            var resp = new DateRange() { StartDate = DateTime.Now.AddDays(-7).ToString("dd-MM-yyyy"), EndDate = DateTime.Now.AddDays(1).ToString("dd-MM-yyyy") };
            try
            {
                var stdate = _genServ.ConvertDatetime(Uri.UnescapeDataString(StartDate));
                var eddate = _genServ.ConvertDatetime(Uri.UnescapeDataString(EndDate));
                if (stdate.ToString("dd-MM-yyyy") == eddate.ToString("dd-MM-yyyy"))
                    return resp;
                if (stdate > eddate)
                    return new DateRange() { StartDate = eddate.ToString("dd-MM-yyyy"), EndDate = stdate.ToString("dd-MM-yyyy") };
                return new DateRange() { StartDate = stdate.ToString("dd-MM-yyyy"), EndDate = eddate.ToString("dd-MM-yyyy") };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return resp;
            }
        }

        public async Task<FileProxy> DownloadReceipt(string ClientKey, string Username, long TransId, int ChannelId)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.CheckIfUserIsLoggedIn(Username, ChannelId, con);
                    if (!validateSession)
                        return new FileProxy() { Response = EnumResponse.InvalidSession };                        
                    return new FileProxy();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new FileProxy() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<TransactionRequest> FetchTransactionsNotFromFinEdge(string ClientKey, TransactionHistoryRequest Request, int page, int size)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Request.Username, Request.Session, Request.ChannelId, con);
                    if (!validateSession)
                        return new TransactionRequest() { Response = EnumResponse.InvalidSession };
                    // Console.WriteLine("request object"+JsonConvert.SerializeObject(Request));
                    Request.StartDate = DateTime.ParseExact(Request.StartDate, "dd-MM-yyyy", CultureInfo.InvariantCulture)
                         .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    Request.EndDate = DateTime.ParseExact(Request.EndDate, "dd-MM-yyyy", CultureInfo.InvariantCulture).AddDays(1)
                        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    Console.WriteLine("request object" + JsonConvert.SerializeObject(Request));
                   /*
                    var parameters = new
                    {
                        SourceAccount = Request.AccountNumber,  // Ensure the name matches the query
                        StartDate = Request.StartDate,
                        EndDate = Request.EndDate
                    };
                    */
                    int skip = page==0?0:(page - 1) * size;
                    int take = size;
                    var parameters = new
                    {
                        SourceAccount = Request.AccountNumber,
                        StartDate = Request.StartDate,
                        EndDate = Request.EndDate,
                        Skip = skip,
                        Take = take
                    };
                    
                    string query = @"SELECT 
                                            transtype,
                                            DATE_FORMAT(createdon, '%d/%m/%Y %H:%i:%S') AS createdon,
                                            Transaction_Ref AS TransID,
                                            CASE 
                                                WHEN transtype = 'bill' THEN NULL 
                                                ELSE Destination_Account 
                                            END AS Destination_Account,
                                            Source_Account,
                                            Charge,
                                            Channel_Id,
                                            Narration,
                                            CASE 
                                                WHEN transtype = 'bill' THEN NULL 
                                                ELSE Destination_BankName
                                            END AS Destination_BankName,
                                            Amount,
                                            CASE 
                                                WHEN transtype = 'bill' THEN NULL 
                                                ELSE Destination_AccountName 
                                            END AS Destination_AccountName,
                                            CASE 
                                                WHEN transtype = 'bill' THEN NULL 
                                                ELSE Destination_BankCode 
                                            END AS Destination_BankCode,
                                            CASE WHEN success=1 THEN 'success'
                                                 WHEN success=0 THEN 'fail'
                                            END AS TransactionStatus
                                        FROM 
                                            omnichannel.transfer 
                                        WHERE 
                                            source_account = @SourceAccount 
                                            AND success in (1,0)
                                            AND createdOn >= @StartDate 
                                            AND createdOn <= @EndDate
                                            AND transtype!='USSD'
                                            AND LOWER(TRIM(transtype)) IN ('vat', 'charge', 'transfer','bill')
                                        ORDER BY 
                                             YEAR(createdon) DESC,
                                             MONTH(createdon) DESC,                                
                                             DAY(createdon) DESC,
                                             HOUR(createdon) DESC,
                                             MINUTE(createdon) DESC,
                                             SECOND(createdon) DESC LIMIT @Take OFFSET @Skip;";
                                       
                      /*
                    string query =$@"SELECT
                                              transtype,
                                                DATE_FORMAT(createdon, '%d/%m/%Y %H:%i:%S') AS createdon,
                                                Transaction_Ref AS TransID,
                                                CASE
                                                    WHEN transtype = 'bill' THEN NULL
                                                    ELSE Destination_Account
                                                END AS Destination_Account,
                                                Source_Account,
                                                Charge,
                                                Channel_Id,
                                                Narration,
                                                CASE
                                                    WHEN transtype = 'bill' THEN NULL
                                                    ELSE Destination_BankName
                                                END AS Destination_BankName,
                                                Amount,
                                                CASE
                                                    WHEN transtype = 'bill' THEN NULL
                                                    ELSE Destination_AccountName
                                                END AS Destination_AccountName,
                                                CASE
                                                    WHEN transtype = 'bill' THEN NULL
                                                    ELSE Destination_BankCode
                                                END AS Destination_BankCode,
                                                CASE
                                                    WHEN success = 1 THEN 'success'
                                                    WHEN success = 0 THEN 'fail'
                                                END AS Transaction_Status
                                            FROM
                                                omnichannel.transfer
                                            WHERE
                                                source_account = @SourceAccount
                                                AND success IN (1, 0)
                                                AND createdon BETWEEN @StartDate AND @EndDate
                                                AND LOWER(TRIM(transtype)) != 'ussd'
                                                AND LOWER(TRIM(transtype)) IN('vat', 'charge', 'transfer','bill')
                                            ORDER BY
                                                createdon DESC LIMIT @Take OFFSET @Skip;";
                          */
                    var listoftransaction = await con.QueryAsync<TransactionListRequest>(query.Trim(), parameters);
                    if (!listoftransaction.Any())
                        return new TransactionRequest() { Response = EnumResponse.NoTransactionFound,Success=true,Transactions=new List<TransactionListRequest>()};
                    return new TransactionRequest()
                    {
                        Response = EnumResponse.Successful,
                        Success = true,
                        Transactions = listoftransaction.Any() ? listoftransaction.ToList() : new List<TransactionListRequest>()
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new TransactionRequest() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<TransactionRequest> DownloadReceipt(string ClientKey, string Username, string TransId, int ChannelId)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.CheckIfUserIsLoggedIn(Username, ChannelId, con);
                    if (!validateSession)
                        return new TransactionRequest() { Response = EnumResponse.InvalidSession };
                    string sql = $@"
                    select  createdon,Transaction_Ref,TransID,
                            CASE 
                            WHEN transtype = 'bill' THEN NULL 
                            ELSE Destination_Account 
                            END AS Destination_Account,
                                Source_Account,
                            Channel_Id,
                            Narration, 
                            CASE 
                            WHEN transtype = 'bill' THEN NULL 
                            ELSE  Destination_BankName
                            END AS Destination_BankName,
                            AmountOrPrincipal,
                            CASE 
                            WHEN transtype = 'bill' THEN NULL 
                            ELSE Destination_AccountName 
                            END AS Destination_AccountName
                            from transfer 
                            where transid=@transid or transaction_ref=@transid";
                   // var transactionlistreq = await con.QueryAsync<TransactionListRequest>($"select * from transfer where transid='{TransId}' or transaction_ref='{TransId}'");
                    var transactionlistreq = await con.QueryAsync<TransactionListRequest>(sql,new {transid=TransId});
                   // Console.WriteLine($" transactionlistreq {JsonConvert.SerializeObject(transactionlistreq)}");
                    var list = new List<TransactionListRequest>();
                        list.Add(transactionlistreq.FirstOrDefault());
                    return new TransactionRequest() { Transactions=list};
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new TransactionRequest() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> FetchTransactionsNotFromFinEdgeWithPagination(string ClientKey, TransactionHistoryRequest Request,int page, int size)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Request.Username, Request.Session, Request.ChannelId, con);
                    if (!validateSession)
                        return new TransactionRequest() { Response = EnumResponse.InvalidSession };
                    // Console.WriteLine("request object"+JsonConvert.SerializeObject(Request));
                    Request.StartDate = DateTime.ParseExact(Request.StartDate, "dd-MM-yyyy", CultureInfo.InvariantCulture)
                         .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    Request.EndDate = DateTime.ParseExact(Request.EndDate, "dd-MM-yyyy", CultureInfo.InvariantCulture).AddDays(1)
                        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    // Request.EndDate = Request.EndDate.AddDays(1).ToString("yyyy-MM-dd");
                    // string dateString = "2024-02-26";
                    // DateTime date = DateTime.ParseExact(dateString, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    // DateTime nextDay = date.AddDays(1);
                    Console.WriteLine("request object" + JsonConvert.SerializeObject(Request));
                    int skip = (page - 1) * size;
                    int take = size;
                    var parameters = new
                    {
                        SourceAccount = Request.AccountNumber,
                        StartDate = Request.StartDate,
                        EndDate = Request.EndDate,
                        Skip=skip,
                        Take=take
                    };
                    string query = $@"
                                        SELECT 
                                            transtype,
                                            DATE_FORMAT(createdon, '%d/%m/%Y %H:%i:%S') AS createdon,
                                            Transaction_Ref AS TransID,
                                                CASE 
                                                WHEN transtype = 'bill' THEN NULL 
                                                ELSE Destination_Account 
                                            END AS Destination_Account,
                                            Source_Account,
                                            Charge,
                                            Channel_Id,
                                            Narration,
                                              CASE 
                                                WHEN transtype = 'bill' THEN NULL 
                                                ELSE  Destination_BankName
                                            END AS Destination_BankName,
                                            Amount,
                                            CASE 
                                                WHEN transtype = 'bill' THEN NULL 
                                                ELSE Destination_AccountName 
                                            END AS Destination_AccountName,
                                            CASE 
                                                WHEN transtype = 'bill' THEN NULL 
                                                ELSE Destination_BankCode 
                                            END AS Destination_BankCode
                                        FROM 
                                            transfer 
                                        WHERE 
                                            source_account = @SourceAccount 
                                            AND success = 1 
                                            AND createdOn >= @StartDate 
                                            AND createdOn <= @EndDate 
                                        ORDER BY 
                                             MONTH(createdon) DESC,
	                                         YEAR(createdon) DESC,
	                                         DAY(createdon) DESC,
                                             HOUR(createdon) DESC,
                                             MINUTE(createdon) DESC,
                                             SECOND(createdon) DESC
                                             LIMIT 
                                             @Take OFFSET @Skip;
                                             ";
                    var listoftransaction = await con.QueryAsync<TransactionListRequest>(query, parameters);
                    // Console.WriteLine($"listOfTransactions: {JsonConvert.SerializeObject(listoftransaction)}");
                    if (!listoftransaction.Any())
                        return new TransactionRequest() { Response = EnumResponse.NoTransactionFound, Success = true, Transactions = new List<TransactionListRequest>() };
                    // Console.WriteLine($"listoftransaction {(JsonConvert.SerializeObject(listoftransaction))}");
                    return new PrimeAdminResponse()
                    {
                        Response = EnumResponse.Successful,
                        Success = true,
                        Data = new { transactions = listoftransaction.Any() ? listoftransaction.ToList() : Enumerable.Empty<TransactionListRequest>(), page = page, size = size }
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new TransactionRequest() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }
    }
}
















































































