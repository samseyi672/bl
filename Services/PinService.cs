using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MySqlX.XDevAPI;
using Newtonsoft.Json;
using Org.BouncyCastle.Asn1.Ocsp;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using Retailbanking.Common.DbObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Retailbanking.BL.Services
{
    public class PinService : IPinService
    {
        private readonly ILogger<TransferServices> _logger;
        //private readonly AppSettings _settings;
        private readonly IGeneric _genServ;
        private readonly DapperContext _context;
        private readonly ISmsBLService _smsBLService;
        private readonly INotification _notification;
        private readonly IRedisStorageService _redisStorageService;

        public PinService(IRedisStorageService redisStorageService, ILogger<TransferServices> logger, IGeneric genServ, DapperContext context, ISmsBLService smsBLService, INotification notification)
        {
            _logger = logger;
          //  _settings = settings;
            _genServ = genServ;
            _context = context;
            _smsBLService = smsBLService;
            _notification = notification;
            _redisStorageService = redisStorageService;
        }

        public async Task<GenericResponse2> ChangePin(CustomerPin customerPin)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var validateSession = await _genServ.ValidateSession(customerPin.Username,customerPin.Session,customerPin.ChannelId, con);
                if (!validateSession)
                    return new GenericResponse2() { Response = EnumResponse.InvalidSession };
                var usr = await _genServ.GetUserbyUsername(customerPin.Username, con);
                if (usr == null)
                {
                    return new GenericResponse2() { Response = EnumResponse.UsernameNotFound };
                }
                //validate otp
                CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)usr.Id);
                _logger.LogInformation("customerDataNotFromBvn "+JsonConvert.SerializeObject(customerDataNotFromBvn));
                string otp = await _redisStorageService.GetCustomerAsync($"pin{customerDataNotFromBvn.PhoneNumber}");
               // string otp2 = await _redisStorageService.GetCacheDataAsync($"pin{customerDataNotFromBvn.PhoneNumber}");
                _logger.LogInformation("otp " + otp);
                OtpTransLimit otpTransLimit = JsonConvert.DeserializeObject<OtpTransLimit>(otp);
                _logger.LogInformation("otpTransLimit " + JsonConvert.SerializeObject(otpTransLimit));
                //compare otp 
                if (!otpTransLimit.otp.Equals(customerPin.otp))
                {
                    return new GenericResponse2() { Response = EnumResponse.InvalidOtp, Success = false };
                }
                DateTime parseddateTime = DateTime.Parse(otpTransLimit.DateTimeString);
                _logger.LogInformation("dateTimeString " + otpTransLimit.DateTimeString);
                DateTime dateTime = DateTime.Now;
                // Calculate the difference
                TimeSpan difference = dateTime - parseddateTime;
                // Check if the difference is not greater than 3 minutes
                if (Math.Abs(difference.TotalMinutes) >= 3)
                {
                    return new GenericResponse2() { Response = EnumResponse.OtpTimeOut, Success = false };
                }
                //validate pin
                var encriptedPin = _genServ.EncryptString(customerPin.oldpin);
                var transPin = await _genServ.GetUserCredential(CredentialType.TransactionPin, usr.Id, con);
                //_logger.LogInformation();
                if (encriptedPin != transPin)
                {
                    return new GenericResponse2 { Response = EnumResponse.InvalidTransactionPin };
                }
                var encriptedNewPin = _genServ.EncryptString(customerPin.newpin);
                //update the pin
                //string query = $@"update user_credentials set credential=@cred,createdon=sysdate() where userid=@userid and credentialtype=@credentialtype,temporarypin='y'";
                //await con.ExecuteAsync(query, new { cred = encriptedNewPin, userid = usr.Id, credentialtype = CredentialType.TransactionPin });
                string query = $@"update user_credentials 
                  set temporarypin='n',credential=@cred, createdon=sysdate() 
                  where userid=@userid and credentialtype=@credentialtype";
                await con.ExecuteAsync(query, new { cred = encriptedNewPin, userid = usr.Id, credentialtype = CredentialType.TransactionPin });
                _logger.LogInformation("pin set successfully ......");
                return new GenericResponse2 { Response = EnumResponse.Successful, Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
                return new GenericResponse2() { Response = EnumResponse.NotSuccessful };
            }

        }

        public async Task<GenericResponse2> ForgotPin(string Username, string Session, int ChannelId, string request)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var validateSession = await _genServ.ValidateSession(Username,Session,ChannelId, con);
                if (!validateSession)
                    return new GenericResponse2() { Response = EnumResponse.InvalidSession};
                var usr = await _genServ.GetUserbyUsername(Username,con);
                string requestquery = $@"select userid from pinrequestchange where userid=@userid";
                var  customeruserid = (await con.QueryAsync<long>(requestquery, new {userid=usr.Id})).FirstOrDefault();
                if (customeruserid!=0)
                {
                   string updatequery = $@"update pinrequestchange set initiated=false,createdon = @createdon,approvalstatus=false,request=@request where userid=@userid";
                   await con.ExecuteAsync(updatequery,new {createdon=DateTime.Now,request = request,userid=usr.Id});
                }
                else
                {
                    string query = "insert into pinrequestchange(userid,request,createdon) values(@userid,@request,@createdon)";
                    await con.ExecuteAsync(query, new
                    {
                        userid = usr.Id,
                        request = _genServ.RemoveSpecialCharacters(request),
                        createdon = DateTime.Now,
                    });
                }
                Console.WriteLine("request update successfully ..........");
                CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con,(int)usr.Id);
                SendMailObject sendMailObject = new SendMailObject();
                sendMailObject.Subject = "Pin Request";
                sendMailObject.Email = customerDataNotFromBvn.Email;
                sendMailObject.Html = $@"Dear {usr.Firstname} {usr.LastName},<br/> Your forgot-pin-request has been sent successfully.We will get back to you shortly.<br/>Thank you for banking with us";
                _logger.LogInformation("sending mail for request .....");
                Task.Run(() =>
                {
                  _genServ.SendMail(sendMailObject);
                });
                return new GenericResponse2() { Success=true,Response=EnumResponse.Successful };
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
                return new GenericResponse2() { Response = EnumResponse.NotSuccessful };
            }
        }

        public async Task<GenericResponse2> GetForgotPinRequest(int page, int size) // for admin side
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                int skip = page == 0 ? page : (page - 1) * size;
                int take = size;
               // var request = (await con.QueryAsync<PinRequestchange>("select u.username,u.Firstname,u.Lastname,p.request as reason,p.createdon,u.id as UserId from users u join pinrequestchange p on u.id=p.userid LIMIT @Take OFFSET @Skip;", new { Take = take, Skip = skip })).ToList();
               // string query = "select u.username,u.Firstname,u.Lastname,p.request as reason,p.createdon,u.id as UserId from users u join pinrequestchange p on u.id=p.userid where p.userid not in (select staffidtoaction from staffaction)";
               // var request = (await con.QueryAsync<PinRequestchange>("select u.username,u.Firstname,u.Lastname,p.request as reason,p.createdon,u.id as UserId from users u join pinrequestchange p on u.id=p.userid where p.userid not in (select staffidtoaction from staffaction where approvalstatus=false) LIMIT @Take OFFSET @Skip;", new { Take = take, Skip = skip })).ToList();
                string query = "select u.username,u.Firstname,u.Lastname,p.request as reason,p.createdon,u.id as UserId from users u join pinrequestchange p on u.id=p.userid where approvalstatus=false and initiated=false";
                var request = (await con.QueryAsync<PinRequestchange>($"{query} LIMIT @Take OFFSET @Skip;", new { Take = take, Skip = skip })).ToList();
                return new GenericResponse2() { data = request ,Success=true ,Response=EnumResponse.Successful};
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
                return new GenericResponse2() { Response = EnumResponse.NotSuccessful };
            }
        }


        public async Task<GenericResponse2> GetAssetCapitalInsuranceForgotPinRequest(int page, int size,string UserType) // for admin side on asset,capital, and insurance
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                int skip = page == 0 ? page : (page - 1) * size;
                int take = size;
                string query = "select u.username,u.first_name as Firstname,u.last_name as Lastname,p.request as reason,p.createdon,u.id as UserId from asset_capital_insurance_user u join asset_capital_insurance_pinrequestchange p on u.id=p.userid where p.approvalstatus=false and p.initiated=false and u.user_type=@UserType";
                var request = (await con.QueryAsync<PinRequestchange>($"{query} LIMIT @Take OFFSET @Skip;", new { Take = take, Skip = skip, UserType= UserType })).ToList();
                return new GenericResponse2() { data = request, Success = true, Response = EnumResponse.Successful };
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
                return new GenericResponse2() { Response = EnumResponse.NotSuccessful };
            }
        }


        public async Task<GenericResponse2> AssetCapitalInsuranceForgotPin(string Username, string Session, int ChannelId, string request,string UserType)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(Session, UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(Username,UserType, con);
                string requestquery = $@"select userid from asset_capital_insurance_pinrequestchange where userid=@userid and user_type=@UserType";
                var customeruserid = (await con.QueryAsync<long>(requestquery, new { userid = usr.id,UserType=UserType })).FirstOrDefault();
                if (customeruserid != 0)
                {
                    string updatequery = $@"update asset_capital_insurance_pinrequestchange set initiated=false,createdon = @createdon,approvalstatus=false,request=@request where userid=@userid and user_type=@UserType";
                    await con.ExecuteAsync(updatequery, new { createdon = DateTime.Now, request = request, userid = usr.id, UserType=UserType});
                }
                else
                {
                    string query = "insert into asset_capital_insurance_pinrequestchange(userid,request,createdon,user_type) values(@userid,@request,@createdon,@UserType)";
                    await con.ExecuteAsync(query, new
                    {
                        userid = usr.id,
                        request = _genServ.RemoveSpecialCharacters(request),
                        createdon = DateTime.Now,
                        UserType=UserType
                    });
                }
                Console.WriteLine("request update successfully ..........");
                AssetCapitalInsuranceCustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getAssetCapitalInsuranceCustomerRelationService(con, (int)usr.id,UserType);
                SendMailObject sendMailObject = new SendMailObject();
                sendMailObject.Subject = "Pin Request";
                sendMailObject.Email = customerDataNotFromBvn.email;
                sendMailObject.Html = $@"Dear {usr.last_name} {usr.last_name},<br/> Your forgot-pin-request has been sent successfully.We will get back to you shortly.<br/>Thank you for banking with us";
                _logger.LogInformation("sending mail for request .....");
                Task.Run(() =>
                {
                    _genServ.SendMail(sendMailObject);
                });
                return new GenericResponse2() { Success = true, Response = EnumResponse.Successful };
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
                return new GenericResponse2() { Response = EnumResponse.NotSuccessful };
            }
        }

        public async Task<GenericResponse2> AssetCapitalInsuranceChangePin(CustomerPin customerPin,string UserType)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(customerPin.Session, UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }

                var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(customerPin.Username,UserType, con);
                if (usr == null)
                {
                    return new GenericResponse2() { Response = EnumResponse.UsernameNotFound };
                }
                //validate otp
                AssetCapitalInsuranceCustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getAssetCapitalInsuranceCustomerRelationService(con, (int)usr.id,UserType);
                _logger.LogInformation("customerDataNotFromBvn " + JsonConvert.SerializeObject(customerDataNotFromBvn));
                string otp = await _redisStorageService.GetCustomerAsync($"pin{customerDataNotFromBvn.phonenumber}");
                // string otp2 = await _redisStorageService.GetCacheDataAsync($"pin{customerDataNotFromBvn.PhoneNumber}");
                _logger.LogInformation("otp " + otp);
                OtpTransLimit otpTransLimit = JsonConvert.DeserializeObject<OtpTransLimit>(otp);
                _logger.LogInformation("otpTransLimit " + JsonConvert.SerializeObject(otpTransLimit));
                //compare otp 
                if (!otpTransLimit.otp.Equals(customerPin.otp))
                {
                    return new GenericResponse2() { Response = EnumResponse.InvalidOtp, Success = false };
                }
                DateTime parseddateTime = DateTime.Parse(otpTransLimit.DateTimeString);
                _logger.LogInformation("dateTimeString " + otpTransLimit.DateTimeString);
                DateTime dateTime = DateTime.Now;
                // Calculate the difference
                TimeSpan difference = dateTime - parseddateTime;
                // Check if the difference is not greater than 3 minutes
                if (Math.Abs(difference.TotalMinutes) >= 3)
                {
                    return new GenericResponse2() { Response = EnumResponse.OtpTimeOut, Success = false };
                }
                //validate pin
                var encriptedPin = _genServ.EncryptString(customerPin.oldpin);
                var transPin = await _genServ.GetAssetCapitalInsuranceUserCredential(CredentialType.TransactionPin, usr.id,UserType, con);
                //_logger.LogInformation();
                if (encriptedPin != transPin)
                {
                    return new GenericResponse2 { Response = EnumResponse.InvalidTransactionPin };
                }
                var encriptedNewPin = _genServ.EncryptString(customerPin.newpin);
                //update the pin
                //string query = $@"update user_credentials set credential=@cred,createdon=sysdate() where userid=@userid and credentialtype=@credentialtype,temporarypin='y'";
                //await con.ExecuteAsync(query, new { cred = encriptedNewPin, userid = usr.Id, credentialtype = CredentialType.TransactionPin });
                string query = $@"update asset_capital_insurance_user_credentials 
                  set temporarypin='n',credential=@cred, createdon=sysdate() 
                  where user_id=@userid and credential_type=@credentialtype and user_type=@UserType";
                await con.ExecuteAsync(query, new { cred = encriptedNewPin, userid = usr.id, credentialtype = CredentialType.TransactionPin, UserType=UserType });
                _logger.LogInformation("pin set successfully ......");
                return new GenericResponse2 { Response = EnumResponse.Successful, Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
                return new GenericResponse2() { Response = EnumResponse.NotSuccessful };
            }

        }

    }
}




























































































































































































































































































































































