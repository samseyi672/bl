using Dapper;
using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using RestSharp;
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
    public class MobileInvestmentService : IMobileInvestment
    {
        private readonly ILogger<AuthenticationServices> _logger;
        private readonly AppSettings _settings;
        private readonly SmtpDetails smtpDetails;
        private readonly IGeneric _genServ;
        private readonly IMemoryCache _cache;
        private readonly DapperContext _context;

        public MobileInvestmentService(ILogger<AuthenticationServices> logger, IOptions<AppSettings> options, IOptions<SmtpDetails> options1, IGeneric genServ, IMemoryCache memoryCache, DapperContext context)
        {
            _logger = logger;
            _settings = options.Value;
             smtpDetails = options1.Value;
            _genServ = genServ;
            _cache = memoryCache;
            _context = context;
        }

        public async Task<GenericResponse> GetActiveFixedDepositOrHalalaInvestment(string UserName)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var usr = await _genServ.GetUserbyUsername(UserName, con);
                var customerid = usr.CustomerId;
                string url = "api/Posting/FetchAllFixedDepositAccount/"+customerid;
                var resp = await _genServ.CallServiceAsyncToString(Method.GET, $"{_settings.FinedgeUrl}{url}", null, true);
                GenericResponse2 genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(resp);
               _logger.LogInformation($"{_settings.FinedgeUrl}{url}");
              _logger.LogInformation("data " + JsonConvert.SerializeObject(genericResponse2));
            if (resp == null || resp == "")
            {
                return new GenericResponse() { Success = true, Response = EnumResponse.NoRecordFound };
            }
            if (genericResponse2.data != null)
            {
                ListOfFixedDepositResponse list = JsonConvert.DeserializeObject<ListOfFixedDepositResponse>(JsonConvert.SerializeObject(genericResponse2.data));
                if (list.Status.Equals("Successful", StringComparison.OrdinalIgnoreCase))
                {
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = list };
                }
            }
            return new BillPaymentResponse() { Success = true, Response = EnumResponse.NoRecordFound, Message = "No record found" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }

        }

        public async Task<GenericResponse> GetAllFixedDepositHistory(string username, string investmenttype) {
            using IDbConnection con = _context.CreateConnection();
            var usr = await _genServ.GetUserbyUsername(username, con);
            var customerid = usr.CustomerId;
            string url = "api/Posting/FetchAllFixedDepositAccount/" + customerid;
            var resp = await _genServ.CallServiceAsyncToString(Method.GET, $"{_settings.FinedgeUrl}{url}", null, true);
            //Console.WriteLine("resp " + resp);
            GenericResponse2 genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(resp);
            Console.WriteLine(JsonConvert.SerializeObject(genericResponse2.data));
            if (resp == null || resp == "")
            {
                return new GenericResponse() { Success = true, Response = EnumResponse.NoRecordFound };
            }
            ListOfFixedDepositResponse list = JsonConvert.DeserializeObject<ListOfFixedDepositResponse>(JsonConvert.SerializeObject(genericResponse2.data));
            var listOfFixedDepositFromFinedge = list.Response.Select(e => e.TDAccountNo).ToList();
            _logger.LogInformation("listOfFixedDepositFromFinedge " + (string.Join(", ", listOfFixedDepositFromFinedge)));
            var fixdeddepositunderliquidationprocess = await con.QueryAsync<string>("select accountNumber_of_trustbanc from  investment where liquidationstatus=@liq and userId=@id", new { liq = "liquidationunderprocessing", id = usr.Id });
            var halalunderliquidationprocess = await con.QueryAsync<string>("select accountNumber_of_trustbanc from noninterestinvestment where liquidationstatus=@liq and userId=@id", new { liq = "liquidationunderprocessing", id = usr.Id });
            if (investmenttype == EnumFixedDepositInvestmentType.Fixeddeposit.GetDescription())
            {
                if (fixdeddepositunderliquidationprocess.Any())
                {
                    _logger.LogInformation("fixdeddepositunderliquidationprocess " + (string.Join(", ", fixdeddepositunderliquidationprocess)));
                    var listofdepositliqdated = fixdeddepositunderliquidationprocess.ToList().Where(f => !listOfFixedDepositFromFinedge.Contains(f)).ToList();
                    if (listofdepositliqdated.Any())
                    {
                        var inClause = string.Join(", ", listofdepositliqdated);
                        await con.ExecuteAsync($"update investment set liquidationstatus=@status where userId=@id and accountNumber_of_trustbanc in ({inClause})", new { id = usr.Id, status = "liquidated" });
                    }
                }
            }
            else if (investmenttype == EnumFixedDepositInvestmentType.Halal.GetDescription())
            {
                if (halalunderliquidationprocess.Any())
                {
                    _logger.LogInformation("fixdeddepositunderliquidationprocess " + (string.Join(", ", fixdeddepositunderliquidationprocess)));
                    var listofdepositliqdated = halalunderliquidationprocess.ToList().Where(f => !listOfFixedDepositFromFinedge.Contains(f)).ToList();
                    if (listofdepositliqdated.Any())
                    {
                        var inClause = string.Join(", ", listofdepositliqdated);
                        await con.ExecuteAsync($"update noninterestinvestment set liquidationstatus=@status where userId=@id and accountNumber_of_trustbanc in ({inClause})", new { id = usr.Id, status = "liquidated" });
                    }
                }
            }
            IEnumerable<FixedDepositHistory> fixedDepositHistory = null;
            if (investmenttype == EnumFixedDepositInvestmentType.Fixeddeposit.GetDescription())
            {
                fixedDepositHistory = (await con.QueryAsync<FixedDepositHistory>("select * from investment where userId=@id", new { id = usr.Id })).ToList();
            }
            else if (investmenttype == EnumFixedDepositInvestmentType.Halal.GetDescription())
            {
                fixedDepositHistory = (await con.QueryAsync<FixedDepositHistory>("select * from noninterestinvestment where userId=@id", new { id = usr.Id })).ToList();
            }
            return new BillPaymentResponse() { Response = EnumResponse.Successful, Data = fixedDepositHistory, Message = "Successful", Success = true };
        }

        public async Task<GenericResponse> GetMutualFundInvestment(string UserName)
        {
            using IDbConnection con = _context.CreateConnection();
            var usr = await _genServ.GetUserbyUsername(UserName, con);
            string sql = "select * from mutualfund where userId=@id order by createdOn asc";
            var MutualFundResponse = await con.QueryAsync<MutualFundResponse>(sql, new { id = usr.Id });
            return new BillPaymentResponse() { Response = EnumResponse.Successful, Success = true, Data = MutualFundResponse };
        }

        public async Task<GenericResponse> GetPublicSectorLoan(string userName)
        {
            using IDbConnection con = _context.CreateConnection();
            var usr = await _genServ.GetUserbyUsername(userName, con);
            string sql = "select * from evaluatedpublicsectorloan where userId=@id order by createdOn asc";
            var PrimePublicLoanResponse = await con.QueryAsync<PublicSectorLoan>(sql, new { id = usr.Id });
            return new PrimeAdminResponse() { Response = EnumResponse.Successful, Success = true, Data = PrimePublicLoanResponse };
        }

        public async Task<GenericResponse> GetRetailLoan(string UserName)
        {
            using IDbConnection con = _context.CreateConnection();
            var usr = await _genServ.GetUserbyUsername(UserName, con);
            string sql = "select * from RETAIL_LOAN where userId=@id order by createdOn asc";
            var PrimeRetailLoanResponse = await con.QueryAsync<PrimeRetailLoan>(sql, new { id = usr.Id });
            return new PrimeAdminResponse() { Response = EnumResponse.Successful, Success = true, Data = PrimeRetailLoanResponse };
        }

    }
}
























































































































































