using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Asn1.Ocsp;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using Retailbanking.Common.DbObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Retailbanking.BL.Services
{
    public class SupportService : ISupportService
    {
        private readonly IGeneric _genServ;
        private readonly DapperContext _context;
        private readonly AppSettings _settings;
        private readonly ILogger<AuthenticationServices> _logger;

        public SupportService(IOptions<AppSettings> options, IGeneric genServ, DapperContext context, ILogger<AuthenticationServices> logger)
        {
            _genServ = genServ;
            _context = context;
            _settings = options.Value;
            _logger = logger;
        }

        public async Task<GenericResponse> ProvideSupportService(Support Request,string Username)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                   var usr =  await _genServ.GetUserbyUsername(Username,con);
                    if (usr == null)
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidUsernameOrPassword };
                    }
                  var validateSession = await _genServ.ValidateSession(usr.Id, Request.Session, Request.ChannelId, con);
                   if (!validateSession)
                      return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    string sql = "insert into contactsupport(phonenumber,email,comment,requestreference,firstname,lastname,subject) " +
                                 "values (@phonenumber,@email,@comment,@requestreference,@firstname,@lastname,@subject)";
                    Console.WriteLine("preparing to send emAIL ....");
                    CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)usr.Id);
                    new Thread(() =>
                    {
                        SendMailObject sendMailObject = new SendMailObject();
                        sendMailObject.Html = $@"<p>The Customer {usr.Firstname.ToUpper()} {usr.LastName.ToUpper()} with phonenumber {customerDataNotFromBvn.PhoneNumber} require the following support stated below:
                                                 </p>
                                                 <p> 
                                                  '{Request.Comment}'
                                                 </p>
                                                  <p>Kindly respond as soon as possible</p>
                                                 ";
                        _logger.LogInformation("email " + _settings.CustomerServiceEmail);
                        sendMailObject.Email = _settings.CustomerServiceEmail; // send mail to admin
                        sendMailObject.Subject = "TrustBanc-Mobile";
                        _genServ.SendMail(sendMailObject);
                    }).Start();
                    return new GenericResponse() { Success = true, Message = "successful", Response = EnumResponse.Successful };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }
    }
}

























































