using Dapper;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Utilities.Zlib;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using Retailbanking.Common.DbObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.Services
{
    public class TestService : ITestService
    {

        private readonly ILogger<TransferServices> _logger;
        private readonly AppSettings _settings;
        private readonly IGeneric _genServ;
        private readonly DapperContext _context;

        public TestService(ILogger<TransferServices> logger, IOptions<AppSettings> options, IGeneric genServ, DapperContext context)
        {
            _logger = logger;
            _settings = options.Value;
            _genServ = genServ;
            _context = context;
        }

        public async Task<GenericResponse> ClearData(string username, string Session, int ChannelId)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                  //  var validateSession = await _genServ.ValidateSession(username,Session,ChannelId, con);
                  //  if (!validateSession)
                  //      return new TransResponse() { Response = EnumResponse.InvalidSession };
                    var usr = await _genServ.GetUserbyUsername(username,con);
                    if (usr != null)
                    {
                    string bvn = usr.Bvn;
                    long  id = usr.Id;
                    int regid = (await con.QueryAsync<int>("select id from registration where username=@username",new { username = username })).FirstOrDefault();
                    await con.ExecuteAsync("delete from registration where username=@username", new { username = username });
                    await con.ExecuteAsync("delete from users where username=@username", new { username = username });
                    await con.ExecuteAsync("delete from bvn_validation where BVN=@BVN", new { BVN = bvn });
                    await con.ExecuteAsync("delete from customerdatanotfrombvn where username=@username", new { username = username });
                    await con.ExecuteAsync("delete from mobiledevice where userid=@userid", new { userid=id});
                    await con.ExecuteAsync("delete from otp_session where objid=@objid", new { objid = regid});
                    await con.ExecuteAsync("delete from user_session where userid=@userid", new { userid = id });
                    await con.ExecuteAsync("delete from user_credentials where UserId=@UserId", new { UserId = id });                   
                    return new GenericResponse() { Response = EnumResponse.Successful,Success=true };
                }
                    else
                    {
                    return new GenericResponse() { Response = EnumResponse.UsernameNotFound };
                    }
                
            }
            catch (Exception ex)
            {
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }
    }
}
