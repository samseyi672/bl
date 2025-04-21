using Dapper;
using iText.Commons.Actions.Contexts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using Retailbanking.Common.DbObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.Services
{
    public class OfficeTransactionLoader : IOfficeTransactionLoader
    {
        private readonly ILogger<AuthenticationServices> _logger;
        private readonly AppSettings _settings;
        private readonly SmtpDetails smtpDetails;
        private readonly IGeneric _genServ;
        private readonly IMemoryCache _cache;
        private readonly DapperContext _context;
        private readonly IAccounts _acctServ;
        private readonly INotification _notification;
        private readonly IFileService _fileService;
        private readonly IDataService _dataService;
        public OfficeTransactionLoader(IDataService dataService, IFileService fileService, INotification notification, ILogger<AuthenticationServices> logger, IOptions<AppSettings> options, IOptions<SmtpDetails> options1, IGeneric genServ, IMemoryCache memoryCache, DapperContext context)
        {
            _logger = logger;
            _dataService = dataService;
            _settings = options.Value;
            smtpDetails = options1.Value;
            _genServ = genServ;
            _cache = memoryCache;
            _context = context;
            _notification = notification;
            _fileService = fileService;
        }
        public async Task<GenericResponse> FetchTransactionByDate(string transtype,string StartDate, string EndDate,int page,int size)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    Console.WriteLine(con.ConnectionString, "database " + con.Database);
                    con.Open();
                   // var usr = await _genServ.GetUserbyUsername(username, con);
                    StartDate = DateTime.ParseExact(StartDate, "dd-MM-yyyy", CultureInfo.InvariantCulture)
                         .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    EndDate = DateTime.ParseExact(EndDate, "dd-MM-yyyy", CultureInfo.InvariantCulture).AddDays(1)
                        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    int skip = (page - 1) * size;
                    int take = size;
                    string condition = !string.IsNullOrEmpty(transtype) ? "where transtype='"+transtype+ "' and (createdon >= @StartDate and createdon <= @EndDate) and transtype!='USSD'" : "where (createdon >= @StartDate and createdon <= @EndDate) AND transtype!='USSD'";
                    Console.WriteLine("condition " + condition);
                    var parameters = new
                    {
                        StartDate = StartDate,
                        EndDate = EndDate,
                        Skip = skip,
                        Take = take
                    };
                    string query = $@"
                                    select  transtype,
                                    DATE_FORMAT(createdon, '%d/%m/%Y %H:%i:%S') AS createdon,
                                    Transaction_Ref AS TransRef,Destination_Account,
                                    TransID,
                                    Source_Account,
                                    Channel_Id,
                                    Success,
                                    Charge,
                                    Amount,
                                    Destination_AccountName,
                                    Narration,Destination_BankName,
                                    Destination_BankCode
                                    from transfer {condition} order by id desc limit @Take offset @Skip";
                    Console.WriteLine("query " + query);
                    var listoftransaction = await con.QueryAsync<Transactions>(query,parameters);
                    if (!listoftransaction.Any())
                        return new TransactionRequest() { Response = EnumResponse.NoTransactionFound, Success = true, Transactions = new List<TransactionListRequest>() };
                    return new PrimeAdminResponse()
                    {
                        Response = EnumResponse.Successful,
                        Success = true,
                        Data = new { transactions = listoftransaction.Any() ? listoftransaction.ToList() : Enumerable.Empty<Transactions>(), page = page, size = size }
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                Console.WriteLine(ex.Message + " " + ex.StackTrace);
                return new TransactionRequest() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> FetchTransactions(string TransactionType,int page, int size)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    Console.WriteLine(con.ConnectionString, "database " + con.Database);
                    con.Open();
                    int skip = (page - 1) * size;
                    int take = size;
                    string condition = !string.IsNullOrEmpty(TransactionType)?"where transtype='"+TransactionType+ "' and transtype!='USSD'" : " where transtype!='USSD' ";
                    Console.WriteLine("condition " + condition);
                    string query = $@"
                                    select  transtype,
                                    DATE_FORMAT(createdon, '%d/%m/%Y %H:%i:%S') AS createdon,
                                    Transaction_Ref AS TransRef,Destination_Account,
                                    TransID,
                                    Source_Account,
                                    Channel_Id,
                                    Success,
                                    Charge,
                                    Amount,
                                    Destination_AccountName,
                                    Narration,Destination_BankName,
                                    Destination_BankCode
                                    from transfer {condition} order by createdon desc limit @Take offset @Skip";
                    Console.WriteLine("query "+query);
                    var listoftransaction = await con.QueryAsync<Transactions>(query, new { Take = take, Skip = skip });
                    if (!listoftransaction.Any())
                        return new TransactionRequest() { Response = EnumResponse.NoTransactionFound, Success = true, Transactions = new List<TransactionListRequest>() };
                    return new PrimeAdminResponse()
                    {
                        Response = EnumResponse.Successful,
                        Success = true,
                        Data = new { transactions = listoftransaction.Any() ? listoftransaction.ToList() : Enumerable.Empty<Transactions>(), page = page, size = size }
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                Console.WriteLine(ex.Message + " " + ex.StackTrace);
                return new TransactionRequest() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse2> SearchTransactionsByReference(string reference, string type,string UserName,string SourceAccount)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    // con.Open();
                    Users usr = null;
                    if (!string.IsNullOrEmpty(UserName))
                    {
                        usr = await _genServ.GetUserbyUsername(UserName, con);
                    }
                        var Transactions = (await con.QueryAsync<Transactions>(@"
                                                                            SELECT 
                                                                                transtype,
                                                                                DATE_FORMAT(createdon, '%d/%m/%Y %H:%i:%S') AS createdon,
                                                                                Transaction_Ref AS TransRef,
                                                                                Destination_Account,
                                                                                TransID,
                                                                                Source_Account,
                                                                                Channel_Id,
                                                                                Success,
                                                                                Charge,
                                                                                Amount,
                                                                                Destination_AccountName,
                                                                                Narration,
                                                                                Destination_BankName,
                                                                                Destination_BankCode
                                                                                FROM transfer 
                                                                                WHERE 
                                                                                Transaction_Ref LIKE @TransactionRef 
                                                                                AND transtype = @type 
                                                                                AND transtype != 'USSD'" + (usr!=null?$" AND User_Id = '{usr.Id.ToString()}' order by ID desc" :(!string.IsNullOrEmpty(SourceAccount)? $" AND Source_Account = '{SourceAccount}' order by ID desc":" order by ID desc")),
                                                                               new
                                                                               {
                                                                                   TransactionRef = $"%{reference}%",
                                                                                   type = type,
                                                                               })).ToList();
                 return new GenericResponse2() {Response=EnumResponse.Successful,data= Transactions,Success=true};
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                //Console.WriteLine(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> SearchTransactionsBySourceAccount(string SourceAccount)
         {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                   // Console.WriteLine(con.ConnectionString, "database " + con.Database);
                    con.Open();
                    /*
                    string query = $@"
                                    select  transtype,
                                    DATE_FORMAT(createdon, '%d/%m/%Y %H:%i:%S') AS createdon,
                                    Transaction_Ref AS TransRef,Destination_Account,
                                    TransID,
                                    Source_Account,
                                    Channel_Id,
                                    Success,
                                    Charge,
                                    Amount,
                                    Destination_AccountName,
                                    Narration,Destination_BankName,
                                    Destination_BankCode
                                    from transfer where Source_Account like %'"+SourceAccount+ "'% order by createdon desc";
                       */
                    string query = @"
                                SELECT transtype,
                                DATE_FORMAT(createdon, '%d/%m/%Y %H:%i:%S') AS createdon,
                                Transaction_Ref AS TransRef,
                                Destination_Account,
                                TransID,
                                Source_Account,
                                Channel_Id,
                                Success,
                                Charge,
                                Amount,
                                Destination_AccountName,
                                Narration,
                                Destination_BankName,
                                Destination_BankCode
                            FROM transfer
                            WHERE Source_Account LIKE CONCAT('%', @SourceAccount, '%') and transtype!='USSD'
                            ORDER BY createdon DESC";
                   // var result = await con.QueryAsync(query, new { SourceAccount });
                    Console.WriteLine("query " + query);
                    var listoftransaction = await con.QueryAsync<Transactions>(query, new { SourceAccount });
                    if (!listoftransaction.Any())
                        return new TransactionRequest() { Response = EnumResponse.NoTransactionFound, Success = true, Transactions = new List<TransactionListRequest>() };
                    return new PrimeAdminResponse()
                    {
                        Response = EnumResponse.Successful,
                        Success = true,
                        Data = new { transactions = listoftransaction.Any() ? listoftransaction.ToList() : Enumerable.Empty<Transactions>()}
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                Console.WriteLine(ex.Message + " " + ex.StackTrace);
                return new TransactionRequest() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public GenericResponse SendCustomDataToFilter()
        {
            CustomerDataAtInitiationAndApproval customerDataAtInitiationAndApproval = new CustomerDataAtInitiationAndApproval();
            customerDataAtInitiationAndApproval.ApprovalName="test";
            customerDataAtInitiationAndApproval.CustomerName="test";
            customerDataAtInitiationAndApproval.role="authorizer";
            _logger.LogInformation("SendCustomDataToFilter " + JsonConvert.SerializeObject(customerDataAtInitiationAndApproval));
            _dataService.SetDataService(customerDataAtInitiationAndApproval);
            return new GenericResponse() { Success = true ,Response=EnumResponse.Successful};
        }
    }
}

































































































