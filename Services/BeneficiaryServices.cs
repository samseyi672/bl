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
using Newtonsoft.Json;

namespace Retailbanking.BL.Services
{
    public class BeneficiaryServices : IBeneficiary
    {
        private readonly ILogger<BeneficiaryServices> _logger;
        private readonly AppSettings _settings;
        private readonly SmtpDetails smtpDetails;
        private readonly IGeneric _genServ;
        private readonly IMemoryCache _cache;
        private readonly DapperContext _context;

        public BeneficiaryServices(ILogger<BeneficiaryServices> logger, IOptions<AppSettings> options, IOptions<SmtpDetails> options1, IGeneric genServ, IMemoryCache memoryCache, DapperContext context)
        {
            _logger = logger;
            _settings = options.Value;
            smtpDetails = options1.Value;
            _genServ = genServ;
            _cache = memoryCache;
            _context = context;
        }


        public async Task<GenericBeneficiary> GetBeneficiary2(string ClientKey, GenericRequest Request, BeneficiaryType beneficiaryType, bool TopBeneficiary = false)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                   
                    var validateSession = await _genServ.ValidateSession(Request.Username, Request.Session, Request.ChannelId, con);
                    if (!validateSession)
                        return new GenericBeneficiary() { Response = EnumResponse.InvalidSession };
                    
                    var getUser = await _genServ.GetUserbyUsername(Request.Username, con);
                    if (getUser == null)
                        return new GenericBeneficiary() { Response = EnumResponse.UserNotFound };

                    string sql = $"select * from beneficiary where userid = {getUser.Id} and beneficiarytype = {(int)beneficiaryType} and isdeleted = 0 order by name";

                    var beneficiary = await con.QueryAsync<BeneficiaryModel>(sql);

                    if (!beneficiary.Any())
                        return new GenericBeneficiary() { Response = EnumResponse.Successful, Success = beneficiary.Any(), Beneficiaries = beneficiary.ToList() };
                    
                    return new GenericBeneficiary() { Response = EnumResponse.Successful, Success = true, Beneficiaries = beneficiary.ToList()};
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericBeneficiary() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericBeneficiary> GetBeneficiary(string ClientKey, GenericRequest Request, BeneficiaryType beneficiaryType, bool TopBeneficiary = false)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Request.Username, Request.Session, Request.ChannelId, con);
                    if (!validateSession)
                        return new GenericBeneficiary() { Response = EnumResponse.InvalidSession };

                    var getUser = await _genServ.GetUserbyUsername(Request.Username, con);
                    if (getUser == null)
                        return new GenericBeneficiary() { Response = EnumResponse.UserNotFound };

                    string sql = $"select * from beneficiary where userid = {getUser.Id} and beneficiarytype = {(int)beneficiaryType} and isdeleted = 0 order by name";

                    var beneficiary = await con.QueryAsync<BeneficiaryModel>(sql);

                    if (!beneficiary.Any() || !TopBeneficiary || beneficiary.Count() <= _settings.TopBeneficiaryCount)
                        return new GenericBeneficiary() { Response = EnumResponse.Successful, Success = beneficiary.Any(), Beneficiaries = beneficiary.ToList() };

                    string sql1 = $@"select destination_account value, destination_accountname name, Destination_BankCode code,count(1) ct from transfer 
                    where user_id = {getUser.Id} and success= 1 group by Destination_Account, Destination_AccountName, Destination_BankCode order by ct desc";

                    if (beneficiaryType == BeneficiaryType.Airtime)
                        sql1 = "";
                    if (beneficiaryType == BeneficiaryType.Bills)
                        sql1 = "";

                    var getTop = await con.QueryAsync<TopBenQuery>(sql1);
                    if (!getTop.Any())
                        return new GenericBeneficiary() { Response = EnumResponse.Successful, Success = beneficiary.Any(), Beneficiaries = beneficiary.Take(_settings.TopBeneficiaryCount).ToList() };

                    if (getTop.Count() >= _settings.TopBeneficiaryCount)
                    {
                        var topBen = new List<BeneficiaryModel>();
                        foreach (var n in getTop.OrderBy(x => x.Name).Take(_settings.TopBeneficiaryCount))
                            topBen.Add(beneficiary.FirstOrDefault(x => x.Value == n.Value && x.Code == n.Code));
                        _logger.LogInformation("topBen " + JsonConvert.SerializeObject(topBen));
                        return new GenericBeneficiary() { Response = EnumResponse.Successful, Success = beneficiary.Any(), Beneficiaries = topBen };
                    }

                    var topBen1 = new List<BeneficiaryModel>();
                    foreach (var n in getTop.OrderBy(x => x.Name))
                        topBen1.Add(beneficiary.FirstOrDefault(x => x.Value == n.Value && x.Code == n.Code));
                    _logger.LogInformation("topBen1 " + JsonConvert.SerializeObject(topBen1));
                    topBen1.AddRange(beneficiary.Take(_settings.TopBeneficiaryCount - getTop.Count()));
                    return new GenericBeneficiary() { Response = EnumResponse.Successful, Success = true, Beneficiaries = topBen1.OrderBy(x => x.Name).ToList() };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericBeneficiary() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> SaveBeneficiary(long UserId, BeneficiaryModel Request, IDbConnection con, BeneficiaryType beneficiaryType)
        {
            try
            {
                var ben = await con.QueryAsync<BeneficiaryModel>($"select * from beneficiary where userid = {UserId} and beneficiarytype = {(int)beneficiaryType} and value = @val and servicename=@serv", new { val = Request.Value, serv = Request.ServiceName });

                if (ben == null || !ben.Any())
                {
                    string sql = $@"INSERT INTO beneficiary (UserId,BeneficiaryType,Name,Value,ServiceName,IsDeleted,Code,ChannelId,Email,PhoneNumber,
                        BillerProduct,BillerService,CreatedOn) VALUES ({UserId},{(int)beneficiaryType},@name,@vals,@servs,0,@cod,{Request.ChannelId},@ema,@phn,@billPro,@billServ,sysdate())";

                    await con.ExecuteAsync(sql, new
                    {
                        name = Request.Name,
                        vals = Request.Value,
                        servs = Request.ServiceName,
                        cod = Request.Code,
                        ema = Request.Email,
                        phn = Request.PhoneNumber,
                        billPro = Request.BillerProduct,
                        billServ = Request.BillerService
                    });
                    return new GenericResponse() { Response = EnumResponse.Successful, Success = true };
                }

                if (ben.FirstOrDefault().IsDeleted)
                    await con.ExecuteAsync($"update beneficiary set isdeleted = 0, createdon = sysdate() where id = {ben.FirstOrDefault().Id}");

                return new GenericResponse() { Response = EnumResponse.Successful, Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public Task<GenericResponse> UpdateBeneficiary(string ClientKey, GenericIdRequest Request)
        {
            throw new NotImplementedException();
        }
    }
}
