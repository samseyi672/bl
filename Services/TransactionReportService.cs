using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Asn1.Ocsp;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static Retailbanking.BL.Services.TransferServices;

namespace Retailbanking.BL.Services
{
    public class TransactionReportService : ITransactionReportService
    {

        private readonly ILogger<AuthenticationServices> _logger;
        private readonly AppSettings _settings;
        private readonly IGeneric _genServ;
        private readonly DapperContext _context;

        public TransactionReportService(IOptions<AppSettings> options, ILogger<AuthenticationServices> logger, IGeneric genServ, DapperContext context)
        {
            _logger = logger;
            _settings = options.Value;
            _genServ = genServ;
            _context = context;
        }

        public async Task<GenericResponse> getTransactionWithIssues(int page, int size)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    int skip = (page - 1) * size;
                    int take = size;
                    Console.WriteLine("got here.....");
                    var transactionReports = (await con.QueryAsync<ReportTransactionResponse>($@"SELECT * FROM reporttransaction where status is false order by id asc, createdon desc limit @Take offset @Skip", new { Take = take, Skip = skip })).ToList();
                    return new PrimeAdminResponse() { Response = EnumResponse.Successful, Success = true, Data = transactionReports };
                }
            }
            catch (Exception ex)
            {
                // Log the error
                _logger.LogInformation(ex.Message);
                return new PrimeAdminResponse() { Response = EnumResponse.NotSuccessful, Success = true };
            }

        }

        public async Task<GenericResponse> SearchTransactionWithIssues(string startdate, string enddate)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    string datePattern = @"^\d{2}-\d{2}-\d{4}$"; // YYYY-MM-DD format

                    if (!Regex.IsMatch(startdate, datePattern) || !Regex.IsMatch(enddate, datePattern))
                    {
                        return new GenericResponse { Response = EnumResponse.WrongDateformat, Success = false, Message = "Invalid date format. Use MM-DD-YYYY" };
                    }
                    startdate = DateTime.ParseExact(startdate, "dd-MM-yyyy", CultureInfo.InvariantCulture)
                       .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    enddate = DateTime.ParseExact(enddate, "dd-MM-yyyy", CultureInfo.InvariantCulture).AddDays(1)
                        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    var transactionReports = (await con.QueryAsync<ReportTransactionResponse>($@"SELECT * FROM reporttransaction where createdon between @startdate and @enddate order by id asc, createdon desc", new { startdate = startdate, enddate = enddate })).ToList();
                    return new PrimeAdminResponse() { Response = EnumResponse.Successful, Success = true, Data = transactionReports };
                }
            }
            catch (Exception ex)
            {
                // Log the error
                _logger.LogInformation(ex.Message);
                return new PrimeAdminResponse() { Response = EnumResponse.NotSuccessful, Success = true };
            }
        }

        public async Task<GenericResponse> ReportTransactionAsFixed(string username,bool status,string transactionref)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var response = await con.ExecuteAsync($@"update reporttransaction set status=@status where username=@username and transactionref=@transactionref",new { transactionref=transactionref, username= username, status=status});
                    return new PrimeAdminResponse() { Response = EnumResponse.StatusUpdated, Success = true};
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
                return new PrimeAdminResponse() { Response = EnumResponse.NotSuccessful, Success = true };
            }

        }
            public async Task<GenericResponse> SearchTransactionWithReference(string transactionref)
              {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    Console.WriteLine("transactionref " + transactionref);
                  var response = (await con.QueryAsync<ReportTransactionResponse>($@"SELECT * FROM reporttransaction where transactionref like '%{_genServ.RemoveSpecialCharacters(transactionref)}%'")).ToList();
                  return new PrimeAdminResponse() { Response = EnumResponse.Successful, Success = true, Data = response };
                }
            }
            catch (Exception ex)
            {
                // Log the error
                _logger.LogInformation(ex.Message);
                return new PrimeAdminResponse() { Response = EnumResponse.NotSuccessful, Success = true };
            }
        }

        public async Task<GenericResponse> TotalTransactionByTransfer()
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    //var response = (await con.QueryAsync<int>($@"SELECT count(*) FROM transfer where transtype='transfer'"));
                    var response = await con.QuerySingleAsync<int>("SELECT COUNT(*) FROM transfer WHERE transtype = @transtype", new { transtype = "transfer" });
                    return new PrimeAdminResponse() { Response = EnumResponse.Successful, Success = true, Data = response };
                }
            }
            catch (Exception ex)
            {
                // Log the error
                _logger.LogInformation(ex.Message);
                return new PrimeAdminResponse() { Response = EnumResponse.NotSuccessful, Success = true };
            }
        }

        public async Task<GenericResponse> TotalTransactionByBill()
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    //var response = (await con.QueryAsync<int>($@"SELECT count(*) FROM transfer where transtype='transfer'"));
                    var response = await con.QuerySingleAsync<int>("SELECT COUNT(*) FROM transfer WHERE transtype = @transtype", new { transtype = "bill" });
                    return new PrimeAdminResponse() { Response = EnumResponse.Successful, Success = true, Data = response };
                }
            }
            catch (Exception ex)
            {
                // Log the error
                _logger.LogInformation(ex.Message);
                return new PrimeAdminResponse() { Response = EnumResponse.NotSuccessful, Success = true };
            }
        }

        public async Task<GenericResponse> TotalCustomer()
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var response = await con.QuerySingleAsync<int>("SELECT COUNT(*) FROM users");
                    return new PrimeAdminResponse() { Response = EnumResponse.Successful, Success = true, Data = response };
                }
            }
            catch (Exception ex)
            {
                // Log the error
                _logger.LogInformation(ex.Message);
                return new PrimeAdminResponse() { Response = EnumResponse.NotSuccessful, Success = true };
            }
        }

        public async Task<GenericResponse> TotalActiveOrInActiveCustomer(string status)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    if (status.Equals("active", StringComparison.CurrentCultureIgnoreCase))
                    {
                        var response = await con.QuerySingleAsync<int>("SELECT COUNT(*) FROM users where status=@status", new { status = 1 });
                        return new PrimeAdminResponse() { Response = EnumResponse.Successful, Success = true, Data = response };
                    }
                    else if (status.Equals("inactive", StringComparison.CurrentCultureIgnoreCase))
                    {
                        var response = await con.QuerySingleAsync<int>("SELECT COUNT(*) FROM users where status=@status", new { status = 2 });
                        return new PrimeAdminResponse() { Response = EnumResponse.Successful, Success = true, Data = response };
                    }
                    else {
                        return new PrimeAdminResponse() { Response = EnumResponse.WrongInput};
                    }
                 }
            }
            catch (Exception ex)
            {
                // Log the error
                _logger.LogInformation(ex.Message);
                return new PrimeAdminResponse() { Response = EnumResponse.NotSuccessful, Success = true };
            }
        }

        public async Task<GenericResponse> TotalTransactionByTransferAndBill()
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var response = await con.QuerySingleAsync<int>("SELECT COUNT(*) FROM transfer WHERE transtype = @transtype", new { transtype = "bill" });
                    var response1 = await con.QuerySingleAsync<int>("SELECT COUNT(*) FROM transfer WHERE transtype = @transtype", new { transtype = "transfer" });
                    return new PrimeAdminResponse() { Response = EnumResponse.Successful, Success = true, Data = new { bill=response,transfer=response1} };
                }
            }
            catch (Exception ex)
            {
                // Log the error
                _logger.LogInformation(ex.Message);
                return new PrimeAdminResponse() { Response = EnumResponse.NotSuccessful, Success = true };
            }
        }

        public async Task<GenericResponse> TotalActiveAndInActiveCustomer()
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {                 
                        var response = await con.QuerySingleAsync<int>("SELECT COUNT(*) FROM users where status=@status", new { status = 1 });
                       // return new PrimeAdminResponse() { Response = EnumResponse.Successful, Success = true, Data = response };
                        var response2 = await con.QuerySingleAsync<int>("SELECT COUNT(*) FROM users where status=@status", new { status = 2 });
                       return new PrimeAdminResponse() { Response = EnumResponse.Successful, Success = true, Data = new {active=response,inactive=response2}};               
                }
            }
            catch (Exception ex)
            {
                // Log the error
                _logger.LogInformation(ex.Message);
                return new PrimeAdminResponse() { Response = EnumResponse.NotSuccessful, Success = true };
            }
        }
    }

}

















































































































