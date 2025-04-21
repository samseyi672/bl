using Dapper;
using Google.Apis.Auth.OAuth2.Requests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlX.XDevAPI;
using MySqlX.XDevAPI.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Asn1.Ocsp;
using Org.BouncyCastle.Crypto;
using RestSharp;
using RestSharp.Extensions;
using Retailbanking.BL.IServices;
using Retailbanking.BL.templates;
using Retailbanking.BL.utils;
using Retailbanking.Common.CustomObj;
using Retailbanking.Common.DbObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using static System.Collections.Specialized.BitVector32;

namespace Retailbanking.BL.Services
{
    public class GenericAssetCapitalInsuranceCustomerService : IGenericAssetCapitalInsuranceCustomerService
    {
        private readonly ILogger<IGenericAssetCapitalInsuranceCustomerService> _logger;
        private readonly AssetSimplexConfig _settings;
        private readonly DapperContext _context;
        private readonly IFileService _fileService;
        private readonly ISmsBLService _smsBLService;
        private readonly IUserCacheService _userCacheService;
        private readonly IRedisStorageService _redisStorageService;
        private readonly IGeneric _genServ;
        private readonly IMemoryCache _cache;
        private readonly AppSettings _appSettings;
        private readonly SimplexConfig _simplexSettings;
        private readonly IRegistration _registrationService;
        private readonly TemplateService _templateService;

        public GenericAssetCapitalInsuranceCustomerService(TemplateService templateService, IRegistration registration, ILogger<IGenericAssetCapitalInsuranceCustomerService> logger, IOptions<AppSettings> appSettings, IOptions<SimplexConfig> _setting2, IOptions<AssetSimplexConfig> settings, DapperContext context, IFileService fileService, ISmsBLService smsBLService, IUserCacheService userCacheService, IRedisStorageService redisStorageService, IGeneric generic)
        {
            _logger = logger;
            _settings = settings.Value;
            _simplexSettings = _setting2.Value;
            _appSettings = appSettings.Value;
            _context = context;
            _fileService = fileService;
            _smsBLService = smsBLService;
            _userCacheService = userCacheService;
            _redisStorageService = redisStorageService;
            _genServ = generic;
            _registrationService = registration;
            _templateService = templateService;
        }


        public async Task<GenericResponse> CreateUsername(string clientKey, SetRegristationCredential Request, string userType)
        {
            try
            {
                var g = ValidateUserType(userType);
                if (!g.Success)
                {
                    return g;
                }
                using (IDbConnection con = _context.CreateConnection())
                {
                    var getReg = await ValidateRegSession(Request.ChannelId, userType, Request.Session, con);
                    if (getReg == 0)
                        return new GenericResponse() { Response = EnumResponse.InvalidRegSession };

                    var chkOtp = await this.ValidateRegistrationSessionOtp(OtpType.Registration, Request.Session, userType, con);
                    // if (chkOtp != null)
                    //    return new GenericResponse() { Response = EnumResponse.OtpNotValidated };
                    if (chkOtp == null)
                        return new GenericResponse() { Response = EnumResponse.OtpNotValidated };
                    var chkuser = await ValidateUsername(Request.SecretValue, userType, con);
                    if (!chkuser.Success)
                        return chkuser;
                    //await con.ExecuteAsync($"update registration set username=@usr where id = {getReg}", new { usr = Request.SecretValue.Trim().ToLower() });
                    //  await con.ExecuteAsync("update customerdatanotfrombvn set username=@username where regid=(select id from registration where requestReference=@Requestref)", new { Requestref = Request.RequestReference, username = Request.SecretValue.Trim().ToLower() });
                    await con.ExecuteAsync($"update asset_capital_insurance_registration set username=@usr where requestReference=@requestReference and user_type=@userType", new { requestReference = Request.RequestReference, usr = Request.SecretValue.Trim().ToLower(), userType = userType });
                    await con.ExecuteAsync("update asset_capital_insurance_custdatanotfrombvn set username=@username where regid=(select id from registration where requestReference=@Requestref and user_type=@userType)", new { Requestref = Request.RequestReference, username = Request.SecretValue.Trim().ToLower(), userType = userType });
                    return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        private async Task<long> ValidateRegSession(long ChannelId, string UserType, string Session, IDbConnection con)
        {
            try
            {
                _logger.LogInformation($"ChannelId {ChannelId} Session {Session}");
                var resp = await con.QueryAsync<SessionObj>($"select reg_id as regid, id, createdon as createdon, session from asset_capital_insurance_reg_session where channelId = {ChannelId} and status = 1 and session = '{Session}' and user_type='{UserType}'");
                if (!resp.Any() || (DateTime.Now.Subtract(resp.FirstOrDefault().createdon).TotalMinutes > _appSettings.RegMaxTime))
                {
                    // update the date to enable the user to start again 
                    await con.ExecuteAsync($"update asset_capital_insurance_reg_session set createdon=sysdate() where id = {resp.FirstOrDefault().Id} and user_type='{UserType}'");
                    return 0;
                }
                Console.WriteLine($" id of user id {resp.FirstOrDefault().Id} regid {resp.FirstOrDefault().RegId}");
                return long.Parse(resp.FirstOrDefault().RegId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return 0;
            }
        }

        public async Task<string> GetToken()
        {
            // get token from redis
            string token = null;
            if ((await _redisStorageService.GetCacheDataAsync<string>("TokenAndRefreshToken")) != null)
            {
                token = await _redisStorageService.GetCacheDataAsync<string>("TokenAndRefreshToken");
            }
            return token;
        }


        public async Task<LoginResponse> LoginUser(string clientKey, LoginRequest Request, string UserType, bool LoginWithFingerPrint = false)
        {
            try
            {
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new LoginResponse() { Response = g.Response, Success = g.Success };
                }

                if (string.IsNullOrEmpty(Request.Username))
                    return new LoginResponse() { Response = EnumResponse.UsernameOrPasswordRequired };

                using (IDbConnection con = _context.CreateConnection())
                {
                    string sess = _genServ.GetSession();
                    var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(Request.Username, UserType, con);
                    // _logger.LogInformation("usr status "+usr.Status);
                    string token2 = await this.GetToken();
                    if (string.IsNullOrEmpty(token2))
                    {
                        token2 = await this.SetAPIToken();
                    }
                    if (string.IsNullOrEmpty(token2))
                    {
                        return new LoginResponse() { Response = EnumResponse.RedisError, Success = false };
                    }
                    IDictionary<string, string> header = new Dictionary<string, string>();
                    header.Add("token", token2.Split(':')[0]);
                    header.Add("xibsapisecret", "");
                    var CustomerChecker = new SimplexCustomerChecker();
                    CustomerChecker.email = usr?.email;
                    CustomerChecker.UserType = UserType;
                    string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.middlewarecustomerurl + "api/Customer/CheckIfCustomerExist", CustomerChecker, true, header);
                    _logger.LogInformation("response from customer checker " + response);
                    var genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                    if (genericResponse2 == null)
                    {
                        return new LoginResponse() { Response = EnumResponse.CustomerValidationUnSuccessful, Success = false, Message = "This customer validation unsuccessful" };
                    }
                    _logger.LogInformation("CustomerChecker genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                    if (!genericResponse2.Success)
                    {
                        return new LoginResponse() { Response = EnumResponse.NotProfiledOnAsset, Success = false, Message = "This customer profile does not exists on Asset" };
                    }
                    if (usr.status == 2)
                    {
                        await _genServ.InsertLogs(usr.id, "", Request.Device, Request.GPS, $"{EnumResponse.InActiveProfile} on {Request.Username}", con);
                        return new LoginResponse() { Response = EnumResponse.InActiveProfile };
                    }
                    _logger.LogInformation($"LoginWithFingerPrint {!LoginWithFingerPrint}");
                    if (!LoginWithFingerPrint)
                    {
                        string enterpass = _genServ.EncryptString(Request.Password);
                        var pssd = await _genServ.GetAssetCapitalInsuranceUserCredential(CredentialType.Password, usr.id, UserType, con);
                        _logger.LogInformation("enterpass != pssd " + (enterpass != pssd));
                        if (enterpass != pssd)
                        {
                            var retry = await con.QueryAsync<string>($"select retries from asset_capital_insurance_user_credentials where user_id= {usr.id} and status =1 and credential_type = 1 and user_type='{UserType}'");
                            int retries = 0;
                            int.TryParse(retry.FirstOrDefault(), out retries);
                            _logger.LogInformation("retries " + retries);
                            if (retries >= _appSettings.Retries)
                            {
                                await con.ExecuteAsync($"update asset_capital_insurance_user set status = 2 where id = {usr.id} and user_type='{UserType}'");
                                await _genServ.InsertLogs(usr.id, "", Request.Device, Request.GPS, $"Password - {EnumResponse.InActiveProfile} on {Request.Username}", con);
                                return new LoginResponse() { Response = EnumResponse.InActiveProfile };
                            }

                            int retr = retries + 1;
                            await con.ExecuteAsync($"update asset_capital_insurance_user_credentials set retries = {retr} where user_id= {usr.id} and status =1 and credential_type = 1 and user_type='{UserType}'");
                            await _genServ.InsertLogs(usr.id, "", Request.Device, Request.GPS, $"Password - {EnumResponse.InvalidUsernameOrPassword} on {Request.Username}", con);
                            return new LoginResponse() { Response = EnumResponse.InvalidUsernameOrPassword };
                        }
                        await con.ExecuteAsync($"update asset_capital_insurance_user_credentials set retries = 0 where user_id= {usr.id} and status =1 and credential_type = 1 and user_type='{UserType}'");
                    }

                    if (Request.ChannelId == 1 && _appSettings.CheckDevice == "y")
                    {
                        var mobDev = await _genServ.GetAssetCapitalInsuranceListOfActiveMobileDevice(usr.id, UserType, con);
                        _logger.LogInformation("mobdev ....." + JsonConvert.SerializeObject(mobDev));
                        if (!mobDev.Any())
                        {
                            return new LoginResponse() { Response = EnumResponse.DeviceNoFound };
                        }
                        var itemdeviceindb = mobDev.Find(e => e.device.ToLower() == Request.Device);

                        _logger.LogInformation($"{Request.Username} Device token " + Request.DeviceToken);
                        // var task1= Task.Run(async () => {
                        var devicetoken = await con.QueryAsync<string>("select device_token as DeviceToken from asset_capital_insurance_mobile_device where device_token = @device and user_type=@UserType", new { device = Request.DeviceToken, UserType = UserType });
                        var token = devicetoken.ToList();
                        _logger.LogInformation(token + " devicetoken " + devicetoken.Any());
                        if (devicetoken.Any())
                        {
                            _logger.LogInformation($"{Request.Username} Device tokens " + JsonConvert.SerializeObject(token));
                            await con.ExecuteAsync("update asset_capital_insurance_mobile_device set device_token = null where device_token in @devtoken", new { devtoken = token });
                            _logger.LogInformation($"updating existing token .....for {Request.Username}");
                            await con.ExecuteAsync($"update asset_capital_insurance_mobile_device set device_token=@devtoken where user_id=@id and device=@Device", new { Device = Request.Device, devtoken = Request.DeviceToken, id = usr.id });
                        }
                        else
                        {
                            _logger.LogInformation($"updating token .....for {Request.Username}");
                            await con.ExecuteAsync("update asset_capital_insurance_mobile_device set device_token=@devtoken where user_id=@id and device=@Dev", new { Dev = Request.Device, devtoken = Request.DeviceToken, id = usr.id });
                        }
                        _logger.LogInformation($"task executed updating token .....for {Request.Username}");
                        if (string.IsNullOrEmpty(Request.Device) || mobDev == null || !mobDev.Any())
                        {
                            await _genServ.InsertLogs(usr.id, "", Request.Device, Request.GPS, $"Device - {EnumResponse.DeviceNotRegistered} on {Request.Device}", con);
                            return new LoginResponse() { Response = EnumResponse.DeviceNotRegistered };
                        }
                    }
                    await _genServ.SetAssetCapitalInsuranceUserSession(usr.id, UserType, sess, Request.ChannelId, con);
                    var CustomerDevice2 = await con.QueryAsync<AssetCapitalInsuranceCustomerDevice>("select * from asset_capital_insurance_customerdevice where login_status=1 and username = @Username and track_device='present' and device=@device and user_type=@UserType", new { username = Request.Username, device = Request.Device, UserType = UserType }); // logged in on any device at all    
                    bool logindevice = true;
                    if (!CustomerDevice2.Any())
                    {
                        _logger.LogInformation("CustomerDevice2.Any() " + CustomerDevice2.Any());
                        logindevice = false;
                    }
                    if (CustomerDevice2.Any()) // what if it is true.check if a different user has already logged in before and set logindevice to false 
                    {
                        _logger.LogInformation("has logged in on a device ....");                                                                                                                                                                    // is any user presently logged-in in this present device
                        var DifferentUserSameDevice = await con.QueryAsync<AssetCapitalInsuranceCustomerDevice>($"select * from asset_capital_insurance_customerdevice where login_status=1 and track_device='present' and device=@device and username!=@Username and user_type=@UserType", new { device = Request.Device, Username = Request.Username, UserType = UserType });
                        if (DifferentUserSameDevice.Any())
                        {
                            var deviceList = DifferentUserSameDevice.Select(d => d.device).ToList();
                            _logger.LogInformation("DifferentUserSameDevice " + DifferentUserSameDevice.Any());
                            if (deviceList.Any())
                            {
                                logindevice = false;
                            }
                        }
                    }
                    await _genServ.InsertLogs(usr.id, sess, Request.Device, Request.GPS, $"Login Successful", con);
                    //sending a mail
                    string DeviceName = (await con.QueryAsync<string>($"select device_name as devicename from asset_capital_insurance_mobile_device where device='{Request.Device}' and user_type='{UserType}'")).FirstOrDefault();
                    _logger.LogInformation("DeviceName " + DeviceName);
                    SendMailObject sendMailObject = new SendMailObject();
                    AssetCapitalInsuranceCustomerDataNotFromBvn customerDataNotFromBvn = (await con.QueryAsync<AssetCapitalInsuranceCustomerDataNotFromBvn>(
                                                                     $"SELECT * FROM asset_capital_insurance_custdatanotfrombvn WHERE user_type='{UserType}' and user_id={usr.id} and user_type='{UserType}'"
                                                                    )).FirstOrDefault();
                    if (customerDataNotFromBvn != null)
                    {
                        _logger.LogInformation("Customer Data Retrieved: " + customerDataNotFromBvn);

                        string updateSql = "UPDATE asset_capital_insurance_custdatanotfrombvn SET user_id = @UserId, username = @Username " +
                                           "WHERE user_type=@UserType and regid = (SELECT id FROM asset_capital_insurance_registration WHERE username = @Username and user_type=@User_Type limit 1)";

                        if (string.IsNullOrEmpty(customerDataNotFromBvn.user_id) || string.IsNullOrEmpty(customerDataNotFromBvn.username))
                        {
                            await con.ExecuteAsync(updateSql, new { UserId = usr.id, Username = usr.username, UserType = UserType, User_Type = UserType });
                        }
                    }
                    return new LoginResponse()
                    {
                        SessionID = sess,
                        Email = customerDataNotFromBvn != null ? customerDataNotFromBvn?.email : "",
                        Bvn = usr.bvn,
                        Username = usr.username,
                        Address = usr.address,
                        IsMigratedCustomer = false,
                        ProfilePic = Path.GetFileName(usr?.picture),
                        Firstname = usr.first_name,
                        Lastname = usr.last_name,
                        Currentlogindevice = logindevice,
                        PhoneNumber = customerDataNotFromBvn != null ? customerDataNotFromBvn?.phonenumber : usr?.PhoneNumber,
                        Response = EnumResponse.Successful,
                        simplex_client_unique_ref = (int)usr.client_unique_ref,
                        Success = true
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new LoginResponse() { Success = false, Response = EnumResponse.SystemError };
            }
        }

        public async Task<string> SetAPIToken()
        {
            var tokenrequest = new
            {
                ConsumerKey = _settings.ConsumerKey,
                ConsumerSecret = _settings.ConsumerSecret
            };
            _logger.LogInformation("tokenrequest " + JsonConvert.SerializeObject(tokenrequest));
            string response = await _genServ.CallServiceAsyncToString(Method.POST,
                  _settings.middlewarecustomerurl + "api/Customer/GetToken",
                  tokenrequest, true);
            _logger.LogInformation("response " + response);
            var genericResponse2 = JsonConvert.DeserializeObject<TokenGenericResponse<SimplexTokenResponse>>(response);
            _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
            if (!genericResponse2.Success)
            {
                _logger.LogInformation("data response " + JsonConvert.SerializeObject(genericResponse2.Data));
                //set check refreshtoken
                var TokenAndRefreshToken = await GetToken();
                var refreshtokenrequest = new
                {
                    accessToken = TokenAndRefreshToken.Split(':')[0],
                    refreshToken = TokenAndRefreshToken.Split(':')[1],
                };
                string response1 = await _genServ.CallServiceAsyncToString(Method.POST,
            _settings.middlewarecustomerurl + "api/Customer/RefreshToken",
            tokenrequest, true);
                _logger.LogInformation("response " + response1);
                // GenericResponse2 genericResponseREfreshToken = JsonConvert.DeserializeObject<GenericResponse2>(response);
                var genericResponseREfreshToken = JsonConvert.DeserializeObject<TokenGenericResponse<SimplexTokenResponse>>(response1);
                if (genericResponseREfreshToken.Success)
                {
                    if ((await _redisStorageService.GetCacheDataAsync<string>("TokenAndRefreshToken")) != null)
                    {
                        await _redisStorageService.RemoveCustomerAsync("TokenAndRefreshToken");
                    }
                    var SimplexTokenAndRefreshResponse = genericResponseREfreshToken.Data;
                    string refreshoken = SimplexTokenAndRefreshResponse.data.access_token + ":" +
                        SimplexTokenAndRefreshResponse.data.refreshToken;
                    await _redisStorageService.SetTokenAndRefreshTokenCacheDataAsync("TokenAndRefreshToken", refreshoken, 60);
                    return refreshoken;
                }
            }
            if ((await _redisStorageService.GetCacheDataAsync<string>("TokenAndRefreshToken")) != null)
            {
                await _redisStorageService.RemoveCustomerAsync("TokenAndRefreshToken");
            }
            //var simplexConvert = JsonConvert.DeserializeObject<SimplexTokenResponse>((string)genericResponse2.data);
            var SimplexTokenResponse = genericResponse2.Data;
            string tokenandrefreshoken = SimplexTokenResponse.data.access_token + ":" +
                SimplexTokenResponse.data.refreshToken;
            await _redisStorageService.SetTokenAndRefreshTokenCacheDataAsync("TokenAndRefreshToken", tokenandrefreshoken, 60);
            return tokenandrefreshoken;
        }
        public async Task<GenericResponse> ValidateUsername(string clientKey, string Username, string UserType)
        {
            try
            {
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return g;
                }
                if (string.IsNullOrEmpty(Username))
                    return new GenericResponse() { Response = EnumResponse.InvalidDetails };

                foreach (char c in Username)
                    if (!Char.IsLetterOrDigit(c))
                        return new GenericResponse() { Response = EnumResponse.UsernameStringDigitOnly };

                using (IDbConnection con = _context.CreateConnection())
                    return await ValidateUsername(Username, UserType, con);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }


        private async Task<GenericResponse> ValidateUsername(string Username, string usertype, IDbConnection con)
        {
            try
            {
                var usr = await con.QueryAsync<long>($"SELECT id FROM asset_capital_insurance_user where lower(username) = @usrs and user_type=@usertype", new { usrs = Username.ToLower().Trim(), usertype = usertype.ToLower().Trim() });
                if (usr.Any())
                    return new GenericResponse() { Response = EnumResponse.UsernameAlreadyExist };
                var usr2 = await con.QueryAsync<long>($"select id from asset_capital_insurance_registration where lower(username)=@uss and user_type=@usertype", new { uss = Username.ToLower().Trim(), usertype = usertype.ToLower().Trim() });
                if (usr2.Any())
                    return new GenericResponse() { Response = EnumResponse.UsernameAlreadyExist };
                return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }


        public async Task<AssetCapitalInsuranceRegistrationOtpSession> ValidateSessionOtp(OtpType otpType, string Session, string userType, IDbConnection con)
        {
            try
            {
                string sql = $@"select * from asset_capital_insurance_otp_session where otp_type= {(int)otpType} and status = 1 and session = @sess and user_type=@userType";
                var resp = await con.QueryAsync<AssetCapitalInsuranceRegistrationOtpSession>(sql, new { sess = Session, userType = userType });
                _logger.LogInformation($"resp opt session " + Session);
                return resp.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return null;
            }
        }

        public async Task<AssetCapitalInsuranceRegistrationOtpSession> ValidateRegistrationSessionOtp(OtpType otpType, string Session, string userType, IDbConnection con)
        {
            try
            {
                string sql = $@"select * from asset_capital_insurance_registration_otp_session where otp_type= {(int)otpType} and status = 1 and session = @sess and user_type=@userType";
                var resp = await con.QueryAsync<AssetCapitalInsuranceRegistrationOtpSession>(sql, new { sess = Session, userType = userType });
                _logger.LogInformation($"resp opt session " + Session);
                return resp.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return null;
            }
        }

        public async Task<AssetCapitalInsuranceOtpSession> ValidateAssetCapitalInsuranceSessionOtp(OtpType otpType, string Session, string UserType, IDbConnection con, string otp)
        {
            try
            {
                string sql = $@"select * from asset_capital_insurance_otp_session where otp_type= {(int)otpType} and status = 1 and session = @sess and user_type=@userType and otp=@otp";
                var resp = await con.QueryAsync<AssetCapitalInsuranceOtpSession>(sql, new { sess = Session, userType = UserType, otp = otp });
                _logger.LogInformation($"resp opt session " + Session);
                return resp.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return null;
            }
        }

        public async Task<GenericResponse> CreatePassword(string clientKey, SavePasswordRequest Request, string UserType)
        {
            try
            {
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return g;
                }
                if (string.IsNullOrEmpty(Request.SecretValue))
                    return new GenericResponse() { Response = EnumResponse.InvalidDetails };

                if (!_genServ.CheckPasswordCondition(Request.SecretValue))
                    return new GenericResponse() { Response = EnumResponse.PasswordConditionNotMet };

                using (IDbConnection con = _context.CreateConnection())
                {
                    var getReg = await ValidateRegSession(Request.ChannelId, UserType, Request.Session, con);
                    if (getReg == 0)
                        return new GenericResponse() { Response = EnumResponse.InvalidRegSession };

                    var chkOtp = await this.ValidateRegistrationSessionOtp(OtpType.Registration, Request.Session, UserType, con);
                    if (chkOtp == null)
                        return new GenericResponse() { Response = EnumResponse.OtpNotValidated };
                    if (string.IsNullOrEmpty(Request.DeviceId))
                        return new GenericResponse() { Response = EnumResponse.DeviceIdRequired };
                    //  await con.ExecuteAsync($"update registration set password=@psd,deviceId=@dv,devicename=@dvn where id ={getReg}", new { psd = _genServ.EncryptString(Request.SecretValue), dv = Request.DeviceId, dvn = Request.DeviceName });
                    _logger.LogInformation("create password " + _genServ.EncryptString(Request.SecretValue) + " ref " + Request.RequestReference);
                    await con.ExecuteAsync($"update asset_capital_insurance_registration set password=@psd,deviceId=@dv,devicename=@dvn where requestReference ='{Request.RequestReference}'", new { psd = _genServ.EncryptString(Request.SecretValue), dv = Request.DeviceId, dvn = Request.DeviceName });
                    return new GenericResponse() { Response = EnumResponse.Successful, Success = true };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        private async Task<bool> CreateProfile(long Regid, string RequestReference, int ChannelId, IDbConnection con, string secretvalue, string UserType)
        {
            try
            {
                // var chkUser = await con.QueryAsync<long>($"select id from users where customerid = (select customerid from registration where RequestReference = '{RequestReference}')");
                var chkUser = await con.QueryAsync<long>($"select id from asset_capital_insurance_user where username = (select username from asset_capital_insurance_registration where RequestReference = '{RequestReference}')");
                Console.WriteLine($"user table checked {chkUser.FirstOrDefault()}");
                var reg = await con.QueryAsync<AssetCapitalInsuranceRegistration>($"select * from asset_capital_insurance_registration where RequestReference='{RequestReference}'");
                _logger.LogInformation("chkUser ....." + chkUser?.FirstOrDefault());
                if (chkUser.Count() == 0)
                {
                    _logger.LogInformation($"inserting into user");
                    string sql = $@"insert into asset_capital_insurance_user (customerid, phonenumber,email, first_name,createdon,channelId,status,bvn,username,last_name,user_type)
                    select customerId,phonenumber,email,first_name,sysdate(),{ChannelId},1,bvn,username,last_name,user_type from asset_capital_insurance_registration where RequestReference=@RequestReference";
                    // Console.WriteLine($"inserted into user successfully");
                    await con.ExecuteAsync(sql, new { RequestReference = RequestReference });
                }
                _logger.LogInformation($"RequestReference {RequestReference}");
                var getCustomer = await _genServ.GetAssetCapitalInsuranceUserbyBvn(reg.FirstOrDefault().bvn, UserType, con);
                _genServ.LogRequestResponse($"customer checked {getCustomer}", "", "");
                await con.ExecuteAsync("update asset_capital_insurance_custdatanotfrombvn set user_id=@userid where regid=(select id from asset_capital_insurance_registration where requestReference=@Requestref)", new { Requestref = RequestReference, userid = getCustomer.id });
                //await _genServ.SetUserCredential(CredentialType.Password, getCustomer.id, reg.FirstOrDefault().password, con, false);
                await _genServ.SetAssetCapitalInsuranceUserCredential(CredentialType.Password, getCustomer.id, reg.FirstOrDefault().password, con, true, UserType); // this shd encript the password
                _logger.LogInformation("setting TransactionPin ....." + secretvalue);
                // await _genServ.SetUserCredential(CredentialType.TransactionPin, getCustomer.Id, reg.FirstOrDefault().TransPin, con, false);
                await _genServ.SetAssetCapitalInsuranceUserCredential(CredentialType.TransactionPin, getCustomer.id, secretvalue, con, true, UserType); // this shd encript the pin
                _logger.LogInformation("TransactionPin pin set....." + secretvalue);
                _logger.LogInformation("getCustomer.Id " + getCustomer.id + "reg ..." + JsonConvert.SerializeObject(reg.FirstOrDefault()));
                await _genServ.SetAssetCapitalInsuranceMobileDevice(getCustomer.id, reg.FirstOrDefault().DeviceId, reg.FirstOrDefault().Devicename, 1, con, UserType);
                Console.WriteLine("mobile device set.....");
                //  await con.ExecuteAsync($"update reg_session set status = 0 where status = 1 and regid ={Regid}");
                await con.ExecuteAsync($"update asset_capital_insurance_registration set ProfiledOpened = 1,password='',transpin='' where RequestReference = '{RequestReference}'");
                Console.WriteLine("successful..");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return false;
            }
        }

        public async Task<GenericResponse> CreateTransPin(string clientKey, SetRegristationCredential Request, string UserType)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var getReg = await ValidateRegSession(Request.ChannelId, UserType, Request.Session, con);
                    if (getReg == 0)
                        return new GenericResponse() { Response = EnumResponse.RestartRegistration };

                    var chkOtp = await ValidateRegistrationSessionOtp(OtpType.Registration, Request.Session, UserType, con);
                    if (chkOtp == null)
                        return new GenericResponse() { Response = EnumResponse.OtpNotValidated };
                    _logger.LogInformation($"{chkOtp} and {getReg}" + " pin " + _genServ.EncryptString(Request.SecretValue));
                    await con.ExecuteAsync($"update asset_capital_insurance_registration set transpin = @tpin where id={getReg} and user_type=@UserType", new { tpin = _genServ.EncryptString(Request.SecretValue), UserType = UserType });
                    var createprofile = await CreateProfile(getReg, Request.RequestReference, Request.ChannelId, con, Request.SecretValue, UserType);
                    string customerid = (await con.QueryAsync<string>($"select bvn from asset_capital_insurance_registration where RequestReference='{Request.RequestReference}'")).FirstOrDefault();
                    _logger.LogInformation("username " + customerid);
                    Thread thread = new Thread(async () =>
                    {
                        _logger.LogInformation("sending email for registration ......");
                        var BalanceEnq = await _genServ.GetAssetCapitalInsuranceUserbyBvn(customerid, UserType, con);
                        _logger.LogInformation("users ......" + JsonConvert.SerializeObject(BalanceEnq));
                        int id = (int)BalanceEnq.id;
                        AssetCapitalInsuranceCustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getAssetCapitalInsuranceCustomerRelationService(con, id, UserType);
                        SendMailObject sendMailObject = new SendMailObject();
                        // sendMailObject.Email = Users.Email;
                        sendMailObject.Email = customerDataNotFromBvn != null ? customerDataNotFromBvn.email : BalanceEnq.email;
                        sendMailObject.Subject = _appSettings.AssetMailSubject;
                        CultureInfo nigerianCulture = new CultureInfo("en-NG");
                        //var model = new { Firstname = FirstLetterUppercaseMaker.CapitalizeFirstLetter(BalanceEnq.first_name), LastName= FirstLetterUppercaseMaker.CapitalizeFirstLetter(BalanceEnq.last_name) };
                        var data = new
                        {
                            title = "Welcome Email",
                            firstname = FirstLetterUppercaseMaker.CapitalizeFirstLetter(BalanceEnq.first_name),
                            lastname = FirstLetterUppercaseMaker.CapitalizeFirstLetter(BalanceEnq.last_name),
                            year = DateTime.Now.Year // Dynamically pass the current year
                        };
                        string filepath = Path.Combine(_settings.PartialViews, "EmailPinTemplate.html");
                        Console.WriteLine("filepath " + filepath);
                        _logger.LogInformation("filepath " + filepath);
                        //string htmlContent = await _templateService.RenderTemplateAsync(filepath, model);
                        string htmlContent = _templateService.RenderScribanTemplate(filepath, data);
                        sendMailObject.Html = htmlContent;
                        _logger.LogInformation("mail sending");
                        _genServ.SendMail(sendMailObject);
                        _logger.LogInformation("mail sent");
                    });
                    thread.Start();
                    return new GenericResponse() { Response = createprofile ? EnumResponse.Successful : EnumResponse.ErrorCreatingProfile, Success = createprofile };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse2> CreateSimplexKycResponse([FromForm] IFormFile file, [FromForm] SimplexKycForm simplexKycForm, string UserType, string Session, int ChannelId)
        {
            var request = new RestRequest();
            using IDbConnection con = _context.CreateConnection();
            var SessionCheck = await _genServ.ValidateSessionForAssetCapitalInsurance(simplexKycForm.ucid, Session, ChannelId, con, UserType);
            if (!SessionCheck)
            {
                return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
            }
            string token = await this.GetToken();
            if (string.IsNullOrEmpty(token))
            {
                token = await this.SetAPIToken();
            }
            IDictionary<string, string> header = new Dictionary<string, string>();
            header.Add("token", token.Split(':')[0]);
            header.Add("xibsapisecret", "");
            if (file == null || file.Length == 0)
            {
                return new GenericResponse2() { Message = "No file uploaded.", Success = false, Response = EnumResponse.NoFileUploaded };
            }
            // Save the file to a specific location
            var filePath = Path.Combine(_simplexSettings.SimplexUploadedFile, file.FileName);
            request.AddParameter("ucid", simplexKycForm.ucid);
            request.AddParameter("Kycid", simplexKycForm.Kycid);
            request.AddParameter("Verified", simplexKycForm.Verified);
            string response = await _genServ.CallServiceAsyncForFileUploadToString(request, Method.POST, _settings.middlewarecustomerurl + "api/Customer/kyc", simplexKycForm, filePath, true, header);
            _logger.LogInformation("response " + response);
            GenericResponse2 genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
            _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
            if (genericResponse2.Success)
            {
                return genericResponse2;
            }
            return genericResponse2;//but check error message 
        }

        public async Task<GenericResponse2> GetClientPicture(ClientPictureRequest clientPictureRequest, string UserType, string Session, int ChannelId)
        {
            GenericResponse2 genericResponse2 = null;
            try
            {
                var request = new RestRequest();
                using IDbConnection con = _context.CreateConnection();

                var SessionCheck = await _genServ.ValidateSessionForAssetCapitalInsurance(clientPictureRequest.ucid, Session, ChannelId, con, UserType);
                if (!SessionCheck)
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await this.GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await this.SetAPIToken();
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                IFormFile file = clientPictureRequest.file;
                if (file == null || file.Length == 0)
                {
                    return new GenericResponse2() { Message = "No file uploaded.", Success = false, Response = EnumResponse.NoFileUploaded };
                }
                //update user credentials here with the picture
                // Save the file to a specific location
                var filePath = Path.Combine(_simplexSettings.SimplexUploadedFile, file.FileName);
                await con.ExecuteAsync($@"update asset_capital_insurance_user
                                          set picture=@picture where user_type=@usertype and client_unique_ref=@ucid
                                       ", new { usertype = UserType, ucid = clientPictureRequest.ucid, picture = filePath });
                request.AddParameter("ucid", clientPictureRequest.ucid);
                string response = await _genServ.CallServiceAsyncForFileUploadToString(request, Method.POST, _settings.middlewarecustomerurl + "api/Customer/kyc", null, filePath, true, header);
                _logger.LogInformation("response " + response);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    return genericResponse2;
                }
                //return genericResponse2;//but check error message 
            }
            catch (Exception ex)
            {
                _logger.LogInformation($"Error: {ex.Message}");
            }
            return genericResponse2;//but check error message 
        }

        public async Task<GenericResponse2> GetClientTitles(string session, string UserType)
        {
            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Success = g.Success, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(session, UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication is not available" };
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                string response = await _genServ.CallServiceAsyncToString(Method.GET, _settings.middlewarecustomerurl + "api/Customer/GetClientTitles", null, true, header);
                _logger.LogInformation("title response " + response);
                if (string.IsNullOrEmpty(response))
                {
                    return new GenericResponse2() { Response = EnumResponse.NotDataFound, Message = "No response from the source" };
                }
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (genericResponse2 == null)
                {
                    return new GenericResponse2() { Response = EnumResponse.NotDataFound, Message = "No response from the source" };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    ClientTitles clientTitles = JsonConvert.DeserializeObject<ClientTitles>(JsonConvert.SerializeObject(genericResponse2.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = clientTitles, Message = "data collection successful from source" };
                }

            }
            catch (Exception ex)
            {
                _logger.LogInformation("Message " + ex.Message);
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data found from the source";
            return genericResponse2;//but check error message 
        }

        public async Task<GenericResponse2> GetEmployers(string session, string UserType)
        {

            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Success = g.Success, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(session, UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not available" };
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                string response = await _genServ.CallServiceAsyncToString(Method.GET, _settings.middlewarecustomerurl + "api/Customer/GetEmployers", null, true, header);
                _logger.LogInformation("employers response " + response);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2 == null)
                {
                    return new GenericResponse2() { Response = EnumResponse.NotDataFound, Message = "No data was found from source" };
                }
                if (genericResponse2.Success)
                {
                    Employers Employers = JsonConvert.DeserializeObject<Employers>(JsonConvert.SerializeObject(genericResponse2.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = Employers, Message = "data collection successful from source" };
                    // return genericResponse2;
                }

            }
            catch (Exception ex)
            {
                _logger.LogInformation("Message " + ex.Message);
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            return genericResponse2;//but check error message
        }

        public async Task<GenericResponse2> GetFullDetails(int accountCode, string session, string UserType)
        {

            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Success = g.Success, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(session, UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "The authentication system is down" };
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                string response = await _genServ.CallServiceAsyncToString(Method.GET, _settings.middlewarecustomerurl + "api/Customer/GetFullDetails/" + accountCode, null, true, header);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2 == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    SimplexCustomerFullDetailsResponse simplexCustomerFullDetailsResponse = JsonConvert.DeserializeObject<SimplexCustomerFullDetailsResponse>(JsonConvert.SerializeObject(genericResponse2.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = simplexCustomerFullDetailsResponse, Message = "data collection successful from source" };

                }

            }
            catch (Exception ex)
            {
                _logger.LogInformation("Message " + ex.Message);
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            return genericResponse2;
        }

        private async Task InsertRegistrationSessionForAssetCapitalInsurance(long Id, string Session, string UserType, int ChannelId, IDbConnection con)
        {
            try
            {
                Console.WriteLine($" InsertRegistrationSession {Session} ChannelId {ChannelId} ");
                var regId = await con.QueryAsync<string>($"select reg_id as regid from asset_capital_insurance_reg_session where reg_id ={Id} and user_type='{UserType}'");
                var regIdlong = regId.FirstOrDefault();
                if (regIdlong != null)
                {
                    await con.ExecuteAsync($"update asset_capital_insurance_reg_session set status = 1, Session='{Session}' where reg_id = {Id} and user_type='{UserType}'");
                }
                else
                {
                    string sql = $@"insert into asset_capital_insurance_reg_session (reg_id, channelId, session,status, createdon,user_type) 
                    values ({Id},{ChannelId},'{Session}',1,sysdate(),'{UserType}')";
                    await con.ExecuteAsync(sql);
                }
                _logger.LogInformation("inserted successfully ....");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
            }
        }

        public async Task<GenericResponse2> QuickLyCheckIfCusotmerExist(string Email, string UserType)
        {
            string token2 = await this.GetToken();
            if (string.IsNullOrEmpty(token2))
            {
                token2 = await this.SetAPIToken();
            }
            if (string.IsNullOrEmpty(token2))
            {
                return new GenericResponse2() { Response = EnumResponse.RedisError, Success = false };
            }
            IDictionary<string, string> header = new Dictionary<string, string>();
            header.Add("token", token2.Split(':')[0]);
            header.Add("xibsapisecret", "");
            var CustomerChecker = new SimplexCustomerChecker();
            CustomerChecker.email = Email;
            CustomerChecker.UserType = UserType;
            string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.middlewarecustomerurl + "api/Customer/CheckIfCustomerExist", CustomerChecker, true, header);
            _logger.LogInformation("response from customer checker " + response);
            if (string.IsNullOrEmpty(response))
            {
                return new GenericResponse2() { Response = EnumResponse.CustomerValidationUnSuccessful, Success = false, Message = "This customer validation unsuccessful" };
            }
            // SimplexCustomerCheckerResponse simplexCustomerCheckerResponse4 = JsonConvert.DeserializeObject<SimplexCustomerCheckerResponse>(response);
            var genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
            SimplexCustomerCheckerResponse simplexCustomerCheckerResponse4 = JsonConvert.DeserializeObject<SimplexCustomerCheckerResponse>((string)genericResponse2.data);
            if (!simplexCustomerCheckerResponse4.data.exists && simplexCustomerCheckerResponse4.message.ToLower().Contains("Customer has multiple account with this email address".ToLower()))
            {
                return new GenericResponse2() { Response = EnumResponse.CustomerValidationUnSuccessful, Success = false, Message = "This customer validation unsuccessful", data = "multipleaccount" };
            }
            if (genericResponse2 == null)
            {
                return new GenericResponse2() { Response = EnumResponse.CustomerValidationUnSuccessful, Success = false, Message = "This customer validation unsuccessful" };
            }
            if (!genericResponse2.Success)
            {
                return new GenericResponse2() { Response = EnumResponse.CustomerValidationUnSuccessful, Success = false, Message = "This customer validation unsuccessful" };
            }
            return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = JsonConvert.SerializeObject(genericResponse2?.data), Message = "data collection successful from source" };
        }
        public async Task<RegistrationResponse> StartRegistration(AssetCapitalInsuranceRegistrationRequest Request)
        {
            try
            {
                var g = ValidateUserType(Request.UserType);
                if (!g.Success)
                {
                    return new RegistrationResponse() { Response = g.Response, Success = g.Success, Message = g.Message };
                }
                _logger.LogInformation("Reg Request: {Request}", JsonConvert.SerializeObject(Request));

                string sess = _genServ.GetSession();
                string otp = _genServ.GenerateOtp();
                _logger.LogInformation("registration otp " + otp);
                // Compile the regex for email validation
                Regex emailRegex = new Regex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$", RegexOptions.Compiled);

                // Validate email format
                if (!string.IsNullOrEmpty(Request.Email) && !emailRegex.IsMatch(Request.Email))
                {
                    return new RegistrationResponse { Response = EnumResponse.InvalidEmail };
                }

                // Generate unique reference
                string uniRef = $"Reg{new Random().Next(11111, 99999)}{DateTime.UtcNow:ddMMyyyyHHmm}";
                Request.ChannelId = Request.ChannelId == 0 ? 1 : Request.ChannelId;

                using (IDbConnection con = _context.CreateConnection())
                {
                    // Validate NIN if provided
                    if (!string.IsNullOrEmpty(Request.Nin))
                    {
                        GenericResponse ninResponse = await _genServ.ValidateNin(Request.Nin, con, Request.Bvn);
                        if (!ninResponse.Success)
                        {
                            return new RegistrationResponse { Response = ninResponse.Response, Success = false };
                        }
                    }

                    // Check if the registration already exists
                    var existingRegistration = (await con.QueryAsync<AssetCapitalInsuranceRegistration>("SELECT * FROM asset_capital_insurance_registration WHERE bvn = @Bvn and user_type=@UserType", new { Bvn = Request.Bvn, UserType = Request.UserType })).FirstOrDefault();

                    if (existingRegistration != null)
                    {
                        if (!existingRegistration.ValidBvn)
                        {
                            return new RegistrationResponse { Response = EnumResponse.InvalidBvn };
                        }
                        var genericResponse4 = await QuickLyCheckIfCusotmerExist(!string.IsNullOrEmpty(Request.Email) ? Request.Email : existingRegistration.email, Request.UserType);

                        if (!genericResponse4.Success)
                        {
                            if (genericResponse4.data is string)
                            {
                                var myflag = (string)genericResponse4.data;
                                if (myflag == "multipleaccount")
                                {
                                    return new RegistrationResponse() { Response = genericResponse4.Response, Message = genericResponse4.Message };
                                }
                            }

                        }
                        var user = await _genServ.GetAssetCapitalInsuraceUserbyUsername(existingRegistration?.username, Request?.UserType, con);
                        await _genServ.InsertOtpForAssetCapitalInsuranceOnRegistration(OtpType.Registration, Request.UserType, Request.Bvn, sess, otp, con);
                        await InsertRegistrationSessionForAssetCapitalInsurance(existingRegistration.id, sess, Request.UserType, Request.ChannelId, con);
                        // });
                        _logger.LogInformation("user " + JsonConvert.SerializeObject(user));
                        if (user == null)
                        {
                            await _genServ.SendOtp3(OtpType.Registration, otp, existingRegistration?.phonenumber, _smsBLService, "Registration", !string.IsNullOrEmpty(Request?.Email) ? Request.Email : existingRegistration?.email);
                        }
                        _logger.LogInformation("checking customer on simplxe " + JsonConvert.SerializeObject(genericResponse4));
                        SimplexCustomerCheckerResponse simplexCustomerCheckerResponse4 = JsonConvert.DeserializeObject<SimplexCustomerCheckerResponse>((string)(genericResponse4?.data));
                        var hasSimplexAccount4 = simplexCustomerCheckerResponse4 != null ? true : false;
                        var simplexClientId4 = simplexCustomerCheckerResponse4 != null ? simplexCustomerCheckerResponse4.data.client_unique_ref : 0;
                        var accountCheck = await AssetCapitalInsuranceCheckCbaByBvn(Request.Bvn);
                        var passwordCheck = user != null
                            ? await con.QueryFirstOrDefaultAsync<string>(
                                "SELECT credential FROM asset_capital_insurance_user_credentials WHERE (ucid = @ucid or user_id=@userid) AND credential_type = 1 and user_type=@UserType",
                                new { ucid = user.client_unique_ref, userid = user.id, UserType = Request.UserType })
                            : null;
                        /*
                        string query = "select email from asset_capital_insurance_custdatanotfrombvn where (email = @Email or phonenumber=@PhoneNumber) and user_type=@UserType";
                        var check = (await con.QueryAsync<string>(query, new { Email = existingRegistration?.email, PhoneNumber = existingRegistration?.phonenumber, UserType = Request.UserType })).FirstOrDefault();
                        _logger.LogInformation("checking mail ..." + check);
                        if (string.IsNullOrEmpty(check))
                        {
                            string custsql = "insert into asset_capital_insurance_custdatanotfrombvn(phonenumber,email,address, regid, phonenumberfrombvn,user_type) values (@PhoneNumber, @Email, @Address, @regid,@phonenumberfrombvn,@UserType)";
                            await con.ExecuteAsync(custsql, new { PhoneNumber = existingRegistration?.phonenumber, Email = string.IsNullOrEmpty(Request.Email) ? existingRegistration?.email : Request.Email, Address = "", regid = existingRegistration?.id, phonenumberfrombvn = existingRegistration?.phonenumber, UserType = Request.UserType });
                        }
                        */
                        return new RegistrationResponse
                        {
                            Email = existingRegistration.email,
                            Success = true,
                            PhoneNumber = existingRegistration.phonenumber,
                            Response = EnumResponse.Successful,
                            SessionID = sess,
                            RequestReference = existingRegistration.RequestReference,
                            IsUsernameExist = user != null,
                            IsAccountNumberExist = accountCheck?.success ?? false,
                            IsPasswordExist = passwordCheck != null,
                            assetcapitalinsuranceregid = existingRegistration.id,
                            hasSimplexAccount = hasSimplexAccount4,
                            simplexClientUniqueId = simplexClientId4
                        };
                    }
                    // Insert new registration entry
                    await con.ExecuteAsync(
                        @"INSERT INTO asset_capital_insurance_registration (channelid, bvn, requestreference, createdon, validbvn, nin,user_type)
                  VALUES (@ChannelId, @Bvn, @UniRef, sysdate(), 1, @Nin,@UserType)",
                        new { Request.ChannelId, Request.Bvn, UniRef = uniRef, Request.Nin, UserType = Request.UserType });

                    var registrationId = await con.QuerySingleAsync<long>(
                        "SELECT id FROM asset_capital_insurance_registration WHERE requestreference = @UniRef", new { UniRef = uniRef });

                    var username = await con.QueryFirstOrDefaultAsync<string>(
                        "SELECT username FROM asset_capital_insurance_registration WHERE requestreference = @UniRef and user_type=@UserType", new { UniRef = uniRef, UserType = Request.UserType });

                    // Check for existing CBA data
                    var cbaResponse = await AssetCapitalInsuranceCheckCbaByBvn(Request.Bvn);
                    _logger.LogInformation("cbaResponse " + JsonConvert.SerializeObject(cbaResponse));
                    if (cbaResponse?.success == true)
                    {
                        // var bvncheck = (await con.QueryAsync<string?>("select BVN from asset_capital_insurance_bvn_validation where BVN=@bvn", new { bvn = cbaResponse?.result?.bvn })).FirstOrDefault();
                        // if (bvncheck.HasValue()) {
                        // var result = cbaResponse?.result;
                        // if(!(await _registrationService.CheckAssetCapitalInsuranceBvn(Request.Bvn, con)).Success){
                        var bvnDetail2 = await _registrationService.ValidateAssetCapitalInsuranceBvn(Request.Bvn, con);
                        if (!bvnDetail2.Success)
                        {
                            return new RegistrationResponse { Response = EnumResponse.InvalidBvn };
                        }
                        // }
                        //}
                        await con.ExecuteAsync(
                            @"UPDATE asset_capital_insurance_registration SET last_name = @Lastname, phonenumber = @PhoneNumber,
                      CustomerId = @CustomerId, first_name = @FirstName, AccountOpened = 1, email = @Email, ValidBvn = 1
                      WHERE id = @Id",
                            new
                            {
                                Id = registrationId,
                                Lastname = cbaResponse.result.lastname ?? "",
                                PhoneNumber = cbaResponse.result?.mobile,
                                CustomerId = cbaResponse.result.customerID,
                                FirstName = cbaResponse.result.firstname,
                                Email = cbaResponse.result?.email ?? Request.Email
                            });
                        var genericResponse5 = await QuickLyCheckIfCusotmerExist(!string.IsNullOrEmpty(Request.Email) ? Request.Email : existingRegistration?.email, Request.UserType);
                        if (!genericResponse5.Success)
                        {
                            if (genericResponse5.data is string)
                            {
                                var myflag = (string)genericResponse5.data;
                                if (myflag == "multipleaccount")
                                {
                                    return new RegistrationResponse() { Response = genericResponse5.Response, Message = genericResponse5.Message };
                                }
                            }

                        }
                        SimplexCustomerCheckerResponse simplexCustomerCheckerResponse5 = JsonConvert.DeserializeObject<SimplexCustomerCheckerResponse>(JsonConvert.SerializeObject(genericResponse5?.data));
                        var hasSimplexAccount2 = simplexCustomerCheckerResponse5 != null ? true : false;
                        var simplexClientId2 = simplexCustomerCheckerResponse5 != null ? simplexCustomerCheckerResponse5.data.client_unique_ref : 0;
                        var usr = username != null ? await _genServ.GetAssetCapitalInsuraceUserbyUsername(username, Request.UserType, con) : null;
                        var IsPwdExists = usr != null ? (await con.QueryAsync<string>($"select credential from asset_capital_insurance_user_credentials where (ucid = @ucid or user_id=@userid) and credential_type=1 and user_type='{Request.UserType}'", new { ucid = usr.client_unique_ref, userid = usr.id })).FirstOrDefault() : null;
                        Task.Run(async () =>
                        {
                            await _genServ.InsertOtpForAssetCapitalInsuranceOnRegistration(OtpType.Registration, Request.UserType, Request.Bvn, sess, otp, con);
                            await InsertRegistrationSessionForAssetCapitalInsurance(registrationId, sess, Request.UserType, Request.ChannelId, con);
                            string queryCheck = "select email from asset_capital_insurance_custdatanotfrombvn where (email = @Email or phonenumber=@PhoneNumber) and user_type=@UserType";
                            var customerdatanotfrombvnCheck = (await con.QueryAsync<string>(queryCheck, new { Email = cbaResponse.result?.email, PhoneNumber = cbaResponse.result?.mobile, UserType = Request.UserType })).FirstOrDefault();
                            _logger.LogInformation("checking mail ..." + customerdatanotfrombvnCheck);
                            await _genServ.SendOtp3(OtpType.Registration, otp, cbaResponse.result?.mobile, _smsBLService, "Registration", !string.IsNullOrEmpty(Request?.Email) ? Request?.Email : cbaResponse.result?.email);
                            if (string.IsNullOrEmpty(customerdatanotfrombvnCheck))
                            {
                                _logger.LogInformation("user id from reg ");
                                string custsql = "insert into asset_capital_insurance_custdatanotfrombvn (phonenumber,email,address, regid, phonenumberfrombvn,user_type) values (@PhoneNumber, @Email, @Address, @regid,@phonenumberfrombvn,@UserType)";
                                await con.ExecuteAsync(custsql, new { PhoneNumber = cbaResponse.result?.mobile, Email = string.IsNullOrEmpty(Request.Email) ? cbaResponse.result?.email : Request.Email, Address = "", regid = registrationId, phonenumberfrombvn = cbaResponse.result?.mobile, UserType = Request.UserType });
                            }
                        });
                        return new RegistrationResponse
                        {
                            Email = cbaResponse.result.email,
                            Success = true,
                            PhoneNumber = cbaResponse.result.mobile,
                            Response = EnumResponse.Successful,
                            SessionID = sess,
                            RequestReference = uniRef,
                            IsUsernameExist = username != null,
                            IsAccountNumberExist = true,
                            IsPasswordExist = IsPwdExists != null,
                            assetcapitalinsuranceregid = registrationId,
                            hasSimplexAccount = hasSimplexAccount2,
                            simplexClientUniqueId = simplexClientId2
                        };
                    }

                    // Validate BVN if no CBA data is found
                    var bvnDetail = await _registrationService.ValidateAssetCapitalInsuranceBvn(Request.Bvn, con);

                    if (!bvnDetail.Success)
                    {
                        return new RegistrationResponse { Response = EnumResponse.InvalidBvn };
                    }
                    if (string.IsNullOrEmpty(bvnDetail.BvnDetails?.Email) || string.IsNullOrEmpty(Request?.Email))
                    {
                        return new RegistrationResponse() { Response = EnumResponse.NoValidEmail, Message = "Please provide an optional email" };
                    }
                    var genericResponse3 = await QuickLyCheckIfCusotmerExist(!string.IsNullOrEmpty(Request.Email) ? Request.Email : bvnDetail.BvnDetails?.PhoneNumber, Request.UserType);
                    if (!genericResponse3.Success)
                    {
                        if (genericResponse3.data is string)
                        {
                            var myflag = (string)genericResponse3.data;
                            if (myflag == "multipleaccount")
                            {
                                return new RegistrationResponse() { Response = genericResponse3.Response, Message = genericResponse3.Message };
                            }
                        }

                    }
                    var thread = new Thread(async () =>
                    {
                        await con.ExecuteAsync($"update asset_capital_insurance_registration set last_name= @lastname, phonenumber = @ph, email = @em,first_name = @fn,ValidBvn = 1 where id= {registrationId} and user_type=@UserType", new
                        {
                            lastname = bvnDetail.BvnDetails.Lastname,
                            ph = bvnDetail.BvnDetails.PhoneNumber,
                            em = bvnDetail.BvnDetails.Email,
                            fn = bvnDetail.BvnDetails.Firstname,
                            UserType = Request.UserType
                        });
                        await _genServ.InsertOtpForAssetCapitalInsuranceOnRegistration(OtpType.Registration, Request.UserType, Request.Bvn, sess, otp, con);
                        await InsertRegistrationSessionForAssetCapitalInsurance(registrationId, sess, Request.UserType, Request.ChannelId, con);
                        await _genServ.SendOtp3(OtpType.Registration, otp, bvnDetail.BvnDetails?.PhoneNumber, _smsBLService, "Registration", !string.IsNullOrEmpty(Request?.Email) ? Request?.Email : bvnDetail.BvnDetails?.Email);
                        string query = "select email from asset_capital_insurance_custdatanotfrombvn where (email = @Email or phonenumber=@PhoneNumber) and user_type=@UserType";
                        var check = (await con.QueryAsync<string>(query, new { Email = bvnDetail.BvnDetails?.Email, PhoneNumber = bvnDetail.BvnDetails?.PhoneNumber, UserType = Request.UserType })).FirstOrDefault();
                        _logger.LogInformation("checking mail ..." + check);
                        if (string.IsNullOrEmpty(check))
                        {
                            string custsql = "insert into asset_capital_insurance_custdatanotfrombvn(phonenumber,email,address, regid, phonenumberfrombvn,user_type) values (@PhoneNumber, @Email, @Address, @regid,@phonenumberfrombvn,@UserType)";
                            await con.ExecuteAsync(custsql, new { PhoneNumber = bvnDetail.BvnDetails?.PhoneNumber, Email = string.IsNullOrEmpty(Request.Email) ? bvnDetail.BvnDetails?.Email : Request.Email, Address = "", regid = registrationId, phonenumberfrombvn = bvnDetail.BvnDetails?.PhoneNumber, UserType = Request.UserType });
                        }
                    });
                    thread.Start();
                    SimplexCustomerCheckerResponse simplexCustomerCheckerResponse2 = JsonConvert.DeserializeObject<SimplexCustomerCheckerResponse>(JsonConvert.SerializeObject(genericResponse3?.data));
                    var hasSimplexAccount = simplexCustomerCheckerResponse2 != null ? true : false;
                    var simplexClientId = simplexCustomerCheckerResponse2 != null ? simplexCustomerCheckerResponse2.data.client_unique_ref : 0;
                    return new RegistrationResponse
                    {
                        Email = bvnDetail.BvnDetails?.Email,
                        Success = true,
                        PhoneNumber = bvnDetail.BvnDetails?.PhoneNumber,
                        Response = EnumResponse.Successful,
                        SessionID = sess,
                        RequestReference = uniRef,
                        IsUsernameExist = false,
                        IsAccountNumberExist = false,
                        IsPasswordExist = false,
                        assetcapitalinsuranceregid = registrationId,
                        hasSimplexAccount = hasSimplexAccount,
                        simplexClientUniqueId = simplexClientId
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in StartRegistration");
                return new RegistrationResponse { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }


        private async Task<FinedgeSearchBvn> AssetCapitalInsuranceCheckCbaByBvn(string Bvn)
        {
            try
            {
                var header = new Dictionary<string, string>
                    {
                        { "ClientKey",_appSettings.FinedgeKey }
                    };
                var resp = await _genServ.CallServiceAsync<FinedgeSearchBvn>(Method.GET, $"{_appSettings.FinedgeUrl}api/enquiry/SearchCustomerbyBvn/{Bvn}", null, true, header);
                Console.WriteLine($" resp.success {resp}");
                return resp;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return null;
            }
        }
        public GenericResponse ValidateUserType(string UserType)
        {
            if (string.IsNullOrEmpty(UserType))
            {
                return new GenericResponse() { Response = EnumResponse.WrongUserType, Success = false, Message = "Wrong userType" };
            }
            if (UserType != "asset" && UserType != "capital" && UserType != "insurance")
                return new GenericResponse() { Response = EnumResponse.WrongUserType, Success = false, Message = "Wrong userType" };
            return new GenericResponse() { Response = EnumResponse.Successful, Success = true, Message = "Correct userType" };
        }

        public async Task<GenericResponse2> OpenAccount(string UserType, GenericRegRequest Request, AssetCapitalInsuranceRegistration assetCapitalInsuranceRegistration)
        {
            try
            {
                var g = ValidateUserType(assetCapitalInsuranceRegistration.user_type);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Success = g.Success, Message = g.Message };
                }
                GenericResponse2 genericResponse2 = null;
                using (IDbConnection con = _context.CreateConnection())
                {
                    var getReg = await ValidateRegSession(Request.ChannelId, UserType, Request.Session, con);
                    if (getReg == 0)
                        return new GenericResponse2() { Response = EnumResponse.InvalidRegSession };
                    string token = await this.GetToken();
                    if (string.IsNullOrEmpty(token))
                    {
                        token = await this.SetAPIToken();
                    }
                    if (string.IsNullOrEmpty(token))
                    {
                        return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not available" };
                    }
                    _logger.LogInformation("token " + token);
                    IDictionary<string, string> header = new Dictionary<string, string>();
                    header.Add("token", token.Split(':')[0]);
                    header.Add("xibsapisecret", "");
                    var RequestObject = new SimplexCustomerRegistration();
                    if (string.IsNullOrEmpty(assetCapitalInsuranceRegistration.bvn) ||
                        string.IsNullOrEmpty(assetCapitalInsuranceRegistration.email) ||
                        string.IsNullOrEmpty(assetCapitalInsuranceRegistration.first_name) ||
                        string.IsNullOrEmpty(assetCapitalInsuranceRegistration.last_name) ||
                        string.IsNullOrEmpty(assetCapitalInsuranceRegistration.birth_date))
                    {
                        return new GenericResponse2()
                        {
                            Response = EnumResponse.InvalidBvnFirstNameLastnameEmailBirthDate,
                            Message = "Bvn, firstname, lastname, email, and birth date cannot be empty"
                        };
                    }

                    RequestObject.phoneNumber = assetCapitalInsuranceRegistration.phonenumber;
                    RequestObject.firstName = assetCapitalInsuranceRegistration.first_name;
                    RequestObject.lastName = assetCapitalInsuranceRegistration.last_name;
                    RequestObject.address = assetCapitalInsuranceRegistration.address;
                    RequestObject.email = assetCapitalInsuranceRegistration.email;
                    RequestObject.client_unique_ref = 0;
                    RequestObject.bvn = assetCapitalInsuranceRegistration.bvn;
                    RequestObject.otherNames = assetCapitalInsuranceRegistration.otherNames;
                    string dateString = assetCapitalInsuranceRegistration.birth_date; // MM/dd/yyyy format
                    DateTime date = DateTime.ParseExact(dateString, "MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
                    string formattedDate = date.ToString("dd/MM/yyyy");
                    RequestObject.birth_date = formattedDate;
                    string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.middlewarecustomerurl + "api/Customer/RegisterExtended", RequestObject, true, header);
                    _logger.LogInformation("response from register extended " + response);
                    genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                    if (!genericResponse2.Success)
                    {
                        genericResponse2.Response = EnumResponse.ProfileAlreadyExist;
                        return genericResponse2;
                    }
                    var genericResponse3 = JsonConvert.DeserializeObject<TokenGenericResponse<SimplexCustomerRegistrationResponse>>(response);
                    _logger.LogInformation("SimplexCustomerRegistrationResponse genericResponse2 " + JsonConvert.SerializeObject(genericResponse3));
                    if (genericResponse3 == null)
                    {
                        return new GenericResponse2() { Success = false, Response = EnumResponse.ProfileAlreadyExist, Message = "This customer has already been profiled/created" };
                    }
                    if (genericResponse3.Success)
                    {
                        SimplexCustomerRegistrationResponse data = genericResponse3?.Data;
                        return new GenericResponse2() { Response = EnumResponse.Successful, Success = genericResponse3.Success, data = JsonConvert.SerializeObject(data) };
                    }
                    genericResponse2 = new GenericResponse2() { Response = EnumResponse.NotSuccessful, Success = genericResponse3.Success, data = null };
                    return genericResponse2;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.NotSuccessful, Success = false };
            }
        }

        public async Task<GenericResponse2> GetRegistrationDataBeforeOpenAccount(string bvn, string UserType, string Session)
        {
            try
            {
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Success = g.Success, Message = g.Message };
                }
                using IDbConnection con = _context.CreateConnection();
                _logger.LogInformation("data at registration bvn " + bvn);
                var bvnDetails = await con.QueryAsync<BvnResponse>($@"select BVN as bvn,PhoneNumber as phoneNumber,PhoneNumber2 as secondaryPhoneNumber,Email as email,Gender as gender,LgaResidence,
                LgaOrigin as lgaOfOrigin,MaritalStatus as maritalStatus,Nationality as nationality,ResidentialAddress as residentialAddress,StateOrigin as stateOfOrigin,StateResidence as stateOfResidence,
                DOB as dateOfBirth,DateCreated,FirstName as firstName,MiddleName as middlename,LastName as lastName from asset_capital_insurance_bvn_validation where BVN=@bvn", new { bvn = bvn });
                if (!bvnDetails.Any())
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.BvnNotFound };
                }
                return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = bvnDetails.FirstOrDefault() };
            }
            catch (Exception ex)
            {
                _logger.LogInformation("ex " + ex.Message);
                return new GenericResponse2() { Success = false, Response = EnumResponse.NotSuccessful };
            }
        }

        public async Task<GenericResponse2> GetDataAtRegistrationWithReference(int Regid, string UserType, string session, string requestReference, int ChannelId)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var getReg = await ValidateRegSession(ChannelId, UserType, session, con);
                if (getReg == 0)
                    return new GenericResponse2() { Response = EnumResponse.RestartRegistration };
                return new GenericResponse2() { Response = EnumResponse.Successful, data = (await con.QueryAsync<AssetCapitalInsuranceRegistration>("select * from asset_capital_insurance_registration where id=@id and user_type=@UserType", new { id = Regid, UserType = UserType })).FirstOrDefault(), Message = "Registration data details", Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogInformation("exception " + ex.Message);
            }
            return new GenericResponse2() { Response = EnumResponse.Successful, Success = false };
        }

        public async Task<GenericResponse> ValidateDob(string v, SetRegristationCredential Request, string UserType)
        {
            try
            {
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return g;
                }
                using (IDbConnection con = _context.CreateConnection())
                {
                    var getReg = await ValidateRegSession(Request.ChannelId, UserType, Request.Session, con);
                    _logger.LogInformation(" getReg " + getReg);
                    if (getReg == 0)
                        return new BvnSubDetails() { Response = EnumResponse.InvalidRegSession };
                    var query = @"
                                SELECT *
                                FROM asset_capital_insurance_bvn_validation
                                WHERE bvn = (
                                    SELECT bvn
                                    FROM asset_capital_insurance_registration
                                    WHERE id = (
                                        SELECT id
                                        FROM asset_capital_insurance_registration
                                        WHERE requestReference = @RequestReference
                                    )
                                )";
                    var bvndetails = await con.QueryAsync<BvnValidation>(query, new { RequestReference = Request.RequestReference });
                    if (!bvndetails.Any())
                    {
                        return new BvnSubDetails() { Response = EnumResponse.DobNotFound };
                    }
                    // _logger.LogInformation("dob check " + bvndetails.FirstOrDefault().DOB.GetValueOrDefault().ToString("dd-MM-yyyy"));
                    if (bvndetails.FirstOrDefault().DOB.GetValueOrDefault().ToString("dd-MM-yyyy") != Request.SecretValue)
                        return new BvnSubDetails() { Response = EnumResponse.WrongDetails };
                    var bvn = bvndetails.FirstOrDefault();
                    return new BvnSubDetails()
                    {
                        Response = EnumResponse.Successful,
                        DOb = bvn.DOB.GetValueOrDefault().ToString("dd MMM, yyyy"),
                        FirstName = bvn.FIRSTNAME,
                        LastName = bvn.LASTNAME,
                        Success = true
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new BvnSubDetails() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<ValidateOtpResponse> ValidateOtp(string v, AssetCapitalInsuranceValidateOtp Request, string UserType)
        {
            try
            {
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new ValidateOtpResponse() { Response = g.Response, Success = g.Success, Message = g.Message };
                }
                using (IDbConnection con = _context.CreateConnection())
                {
                    var getReg = await ValidateRegSession(Request.ChannelId, UserType, Request.Session, con);
                    _logger.LogInformation($"getReg {getReg}");
                    if (getReg == 0)
                        return new ValidateOtpResponse() { Response = EnumResponse.RestartRegistration };

                    // var resp = await _genServ.ValidateSessionOtp(OtpType.Registration, Request.Session, con);
                    var resp = await ValidateRegistrationSessionOtp(OtpType.Registration, Request.Session, UserType, con);
                    Console.WriteLine($"resp {resp}");
                    if (resp == null || resp.otp != Request.Otp)
                        return new ValidateOtpResponse() { Response = EnumResponse.InvalidOtp };
                    //  await con.ExecuteAsync($"update otp_session set status = 0 where id = {resp.ID}");
                    _logger.LogInformation($"id of user {resp.bvn}");
                    // var reg = await con.QueryAsync<string>($"select bvn from asset_capital_insurance_registration where bvn ='{resp.bvn}' and user_type='{UserType}'");
                    var reg = await con.QueryAsync<string>(
                                              "SELECT bvn FROM asset_capital_insurance_registration WHERE bvn = @bvn AND user_type = @userType",
                                                   new { bvn = resp.bvn, userType = UserType });
                    var checkprofile = await con.QueryAsync<AssetCapitalInsuranceUsers>($"select * from asset_capital_insurance_user where bvn = @bv", new { bv = reg.FirstOrDefault() });
                    if (checkprofile.Any())
                        return new ValidateOtpResponse() { Success = true, Response = EnumResponse.Successful, ExistingAccount = true, ProfiledCustomer = true };

                    return new ValidateOtpResponse() { Success = true, Response = EnumResponse.Successful, ExistingAccount = true };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new ValidateOtpResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> ContactSupportForRegistration(string v, ContactSupport Request, string UserType)
        {
            try
            {
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new ValidateOtpResponse() { Response = g.Response, Success = g.Success, Message = g.Message };
                }
                using (IDbConnection con = _context.CreateConnection())
                {
                    var getReg = await ValidateRegSession(Request.ChannelId, UserType, Request.Session, con);
                    if (getReg == 0)
                        return new ValidateOtpResponse() { Response = EnumResponse.InvalidRegSession };
                    var username = (await con.QueryAsync<string>($"select username from asset_capital_insurance_registration where requestreference = '{Request.RequestReference}'")).FirstOrDefault();
                    Request.Comment = _genServ.RemoveSpecialCharacters(Request.Comment);
                    var userid = await _genServ.GetAssetCapitalInsuraceUserbyUsername(username, UserType, con);
                    string reqRef = (await con.QueryAsync<string>("select requestreference from contactsupport where requestreference=@requestreference", new { requestreference = Request.RequestReference })).FirstOrDefault();
                    if (!string.IsNullOrEmpty(reqRef))
                    {
                        _logger.LogInformation("updating comment " + Request.Comment);
                        await con.ExecuteAsync("update contactsupport set comment=@comment where requestreference=@requestreference ", new { requestreference = Request.RequestReference, comment = Request.Comment });
                    }
                    else
                    {
                        string sql = "insert into contactsupport(phonenumber,email,comment,requestreference,firstname,lastname,subject) " +
                            "values (@phonenumber,@email,@comment,@requestreference,@firstname,@lastname,@subject)";
                        _logger.LogInformation("username " + username + "userid " + userid);
                        await con.ExecuteAsync(sql, new
                        {
                            phonenumber = Request.PhoneNumber,
                            email = Request.Email,
                            comment = Request.Comment,
                            requestreference = Request.RequestReference,
                            firstname = Request.FirstName,
                            lastname = Request.LastName,
                            subject = Request.Subject
                        });
                        return new GenericResponse() { Success = true, Message = "successful", Response = EnumResponse.Successful };
                    }

                    new Thread(() =>
                    {
                        Thread.Sleep(50);
                        SendMailObject sendMailObject = new SendMailObject();
                        sendMailObject.Html = $@"<p>The Customer {userid.first_name.ToUpper()} {userid.last_name.ToUpper()} with phonenumber {Request.PhoneNumber} has the following comment on Mobile Registration on Otp:
                                                 </p>
                                                 <p> 
                                                  '{Request.Comment}'
                                                 </p>
                                                  <p>Kindly respond as soon as possible</p>
                                                 ";
                        // string email = !string.IsNullOrEmpty(_settings.CustomerServiceEmail) ? _settings.CustomerServiceEmail : "opeyemi.adubiaro@trustbancgroup.com";
                        _logger.LogInformation("email " + _appSettings.CustomerServiceEmail);
                        sendMailObject.Email = _appSettings.CustomerServiceEmail; // send mail to admin
                        sendMailObject.Subject = _appSettings.AssetMailSubject;
                        _genServ.SendMail(sendMailObject);
                    }).Start();
                    return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new RegistrationResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> CustomerReasonForNotReceivngOtp(string v, CustomerReasonForNotReceivngOtp Request, string UserType)
        {
            try
            {
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new ValidateOtpResponse() { Response = g.Response, Success = g.Success, Message = g.Message };
                }
                using (IDbConnection con = _context.CreateConnection())
                {
                    var getReg = await ValidateRegSession(Request.ChannelId, UserType, Request.Session, con);
                    if (getReg == 0)
                        return new ValidateOtpResponse() { Response = EnumResponse.InvalidRegSession };
                    string reqRef = (await con.QueryAsync<string>("select bvn from asset_capital_insurance_customerissues where bvn=@bvn and user_type=@UserType", new { bvn = Request.Bvn, UserType = UserType })).FirstOrDefault();
                    _logger.LogInformation("reqRef " + reqRef);
                    if (!string.IsNullOrEmpty(reqRef))
                    {
                        return new GenericResponse() { Success = true, Message = "Done already", Response = EnumResponse.Successful };
                    }
                    string sql = "insert into asset_capital_insurance_customerissues(bvn,reason,requestreference,user_type) " +
                            "values (@bvn,@reason,@requestreference,@UserType)";
                    //  _logger.LogInformation("username " + username + "userid " + userid);
                    await con.ExecuteAsync(sql, new
                    {
                        bvn = Request.Bvn,
                        reason = Request.Reason,
                        requestreference = Request.RequestReference,
                        UserType = UserType
                    });
                    return new GenericResponse() { Success = true, Message = "successful", Response = EnumResponse.Successful };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new RegistrationResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> ResendOtpToPhoneNumber(string v, GenericRegRequest2 Request, string UserType)
        {
            try
            {
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new ValidateOtpResponse() { Response = g.Response, Success = g.Success, Message = g.Message };
                }
                using (IDbConnection con = _context.CreateConnection())
                {
                    var getReg = await ValidateRegSession(Request.ChannelId, UserType, Request.Session, con);
                    if (getReg == 0)
                        return new RegistrationResponse() { Response = EnumResponse.InvalidRegSession };
                    string otp = _genServ.GenerateOtp2();
                    await con.ExecuteAsync("update asset_capital_insurance_registration_otp_session set otp=@otp where Session=@sess and user_type=@UserType", new { otp = otp, sess = Request.Session, UserType = UserType });
                    _logger.LogInformation("otp updated ..");
                    var userId = (await con.QueryAsync<int?>(
                                 "SELECT id FROM asset_capital_insurance_user WHERE username = (SELECT username FROM asset_capital_insurance_registration WHERE RequestReference = @RequestReference)",
                                 new { RequestReference = Request.RequestReference }
                             )).FirstOrDefault();
                    AssetCapitalInsuranceCustomerDataNotFromBvn customerDataNotFromBvn = null;
                    if (userId.HasValue)
                    {
                        customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getAssetCapitalInsuranceCustomerRelationService(con, userId.Value, UserType);
                    }
                    // await con.ExecuteAsync($"update otp_session set status = 0 where otp_type = {(int)OtpType.Registration} and objId = {getReg}");
                    var PhoneNumberCheker = (await con.QueryAsync<string>("select phonenumber from asset_capital_insurance_custdatanotfrombvn where PhoneNumber=@PhoneNumber and user_type=@UserType", new { PhoneNumber = Request.PhoneNumber, UserType = UserType })).FirstOrDefault();
                    if (!string.IsNullOrEmpty(PhoneNumberCheker))
                    {
                        _logger.LogInformation("PhoneNumberCheker " + PhoneNumberCheker);
                        Task.Run(async () =>
                        {
                            await _genServ.SendOtp3(OtpType.Registration, otp, PhoneNumberCheker, _smsBLService, "Registration", customerDataNotFromBvn?.email);
                        });
                        return new RegistrationResponse() { Success = true, Response = EnumResponse.Successful };
                    }
                    await con.ExecuteAsync($"update asset_capital_insurance_custdatanotfrombvn set phonenumber = @PhoneNumber where regid = {getReg} and user_type=@UserType", new { PhoneNumber = Request.PhoneNumber, UserType = UserType }); //to update incase they provide alternative PhoneNumber
                    await _genServ.InsertOtp(OtpType.Registration, getReg, Request.Session, otp, con);
                    var phoneNumber = (await con.QueryAsync<string>(
                        "SELECT phonenumber FROM asset_capital_insurance_registration WHERE RequestReference = @RequestReference",
                        new { RequestReference = Request.RequestReference }
                    )).FirstOrDefault();

                    // Determine the correct phone number to use
                    var targetPhoneNumber = customerDataNotFromBvn?.phonenumber ?? phoneNumber;
                    // Send OTP if targetPhoneNumber is valid
                    if (!string.IsNullOrEmpty(targetPhoneNumber))
                    {
                        await _genServ.SendOtp3(OtpType.Registration, otp, targetPhoneNumber, _smsBLService, "Registration", customerDataNotFromBvn.email);
                    }
                    return new RegistrationResponse() { Response = EnumResponse.Successful, Success = true };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new RegistrationResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> ClearRegistrationByBvn(string bvn, string userType)
        {
            try
            {
                var g = ValidateUserType(userType);
                if (!g.Success)
                {
                    return g;
                }
                using IDbConnection con = _context.CreateConnection();
                AssetCapitalInsuranceUsers usr = await _genServ.GetAssetCapitalInsuranceUserbyBvn(bvn, userType, con);
                if (usr == null)
                {
                    return new GenericResponse() { Response = EnumResponse.UserNotFound, Message = "User not found" };
                }
                await con.ExecuteAsync("delete from asset_capital_insurance_user where bvn=@bvn", new { bvn = bvn });
                await con.ExecuteAsync("delete from asset_capital_insurance_registration where bvn=@bvn", new { bvn = bvn });
                await con.ExecuteAsync("delete from asset_capital_insurance_custdatanotfrombvn where user_id=@userid", new { userid = usr.id });
                await con.ExecuteAsync("delete from asset_capital_insurance_customerdevice where username=@username", new { username = usr.username });
                await con.ExecuteAsync("delete from asset_capital_insurance_otp_session where user_id=@userid", new { userid = usr.id });
                await con.ExecuteAsync("delete from asset_capital_insurance_user_credentials where user_id=@userid", new { userid = usr.id });
                await con.ExecuteAsync("delete from asset_capital_insurance_mobile_device where user_id=@userid", new { userid = usr.id });
                //also call simplx here for data clearing
                return new GenericResponse() { Response = EnumResponse.Successful, Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogInformation("ex " + ex.Message);
            }
            throw new NotImplementedException();
        }

        public async Task<GenericResponse2> MailAfterLoginAndSuccessfulAccountFetch(string username, string session, int channelId, string deviceName, string UserType)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(session, UserType, con)))
                    {
                        return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                    }
                    var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(username, UserType, con);
                    AssetCapitalInsuranceCustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getAssetCapitalInsuranceCustomerRelationService(con, (int)usr.id, UserType);
                    SendMailObject sendMailObject = new SendMailObject();
                    sendMailObject.Email = customerDataNotFromBvn?.email;
                    sendMailObject.Firstname = usr.first_name + " " + usr.last_name;
                    var data = new
                    {
                        title = "Login Notification",
                        username = usr.username,
                        year = DateTime.Now.Year,
                        LoginTime = DateTime.Now,
                        DeviceName = deviceName,
                    };
                    string filepath = Path.Combine(_settings.PartialViews, "_loginTemplate.html");
                    Console.WriteLine("filepath " + filepath);
                    _logger.LogInformation("filepath " + filepath);
                    string htmlcontent = _templateService.RenderScribanTemplate(filepath, data);
                    sendMailObject.Html = htmlcontent;
                    sendMailObject.Subject = _appSettings.AssetMailSubjectHeader + " Login";
                    Thread thread = new Thread(() =>
                    {
                        //Console.WriteLine("sending mail in thread");
                        _genServ.LogRequestResponse("enter in thread to send email ", $" ", "");
                        _genServ.SendMail(sendMailObject);
                        Console.WriteLine("mail sent ....");
                    });
                    thread.Start();
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
        }


        public async Task<AssetCapitalInsuranceUsers> GetAssetCapitalInsuranceCustomerbyPhoneNumber(string phoneNumberOrEmail, string UserType, IDbConnection con)
        {
            _logger.LogInformation("GetAssetCapitalInsuranceCustomerbyPhoneNumber " + phoneNumberOrEmail);
            return (await con.QueryAsync<AssetCapitalInsuranceUsers>("select * from asset_capital_insurance_user where (phonenumber=@phoneNumberOrEmail or email=@phoneNumberOrEmail) and user_type=@UserType", new { phoneNumberOrEmail = phoneNumberOrEmail, UserType = UserType })).FirstOrDefault();
        }

        public async Task<GenericResponse> StartRetrival(string v, AssetCapitalInsuranceResetObj Request, string UserType)
        {
            try
            {
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new RetrivalResponse() { Response = g.Response, Success = g.Success, Message = g.Message };
                }
                if (string.IsNullOrEmpty(Request.PhoneNumberOrEmail) || string.IsNullOrEmpty(Request.TransactionPin))
                    return new RetrivalResponse() { Response = EnumResponse.InvalidDetails };

                using (IDbConnection con = _context.CreateConnection())
                {
                    var getCust = await GetAssetCapitalInsuranceCustomerbyPhoneNumber(Request.PhoneNumberOrEmail, UserType, con);
                    if (getCust == null)
                        return new RetrivalResponse() { Response = EnumResponse.UserNotFound };
                    string tpin = _genServ.EncryptString(Request.TransactionPin);
                    _logger.LogInformation("pin " + tpin);
                    var getPin = await _genServ.GetAssetCapitalInsuranceUserCredential(CredentialType.TransactionPin, getCust.id, UserType, con);
                    if (tpin != getPin)
                        return new RetrivalResponse() { Response = EnumResponse.WrongDetails };
                    string sess = _genServ.GetSession();
                    string otp = _genServ.GenerateOtp();
                    await _genServ.SetAssetCapitalInsuranceUserSession(getCust.id, UserType, sess, Request.ChannelId, con);
                    await _genServ.InsertLogs(getCust.id, sess, Request.Device, "", $"Retrival Process Start - " + (OtpType)Request.RetrivalType, con);
                    // _logger.LogInformation("getuser " + JsonConvert.SerializeObject(getuser));
                    AssetCapitalInsuranceCustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getAssetCapitalInsuranceCustomerRelationService(con, (int)getCust.id, UserType);
                    if ((OtpType)Request.RetrivalType == OtpType.PasswordReset)
                    {
                        _logger.LogInformation("OtpType.RetrivalType " + OtpType.PasswordReset);
                        // await _genServ.InsertOtp((OtpType)Request.RetrivalType, getuser.Id, sess, otp, con);
                        await _genServ.InsertOtpForAssetCapitalInsurance((OtpType)Request.RetrivalType, UserType, getCust.client_unique_ref, sess, otp, con, getCust.id);
                        await _genServ.SendOtp3((OtpType)Request.RetrivalType, otp, customerDataNotFromBvn?.phonenumber, _smsBLService, "Retrieval", customerDataNotFromBvn?.email);
                    }
                    else if ((OtpType)Request.RetrivalType == OtpType.RetrieveUsername)
                    {
                        _logger.LogInformation("OtpType.RetrieveUsername " + OtpType.RetrieveUsername);
                        // await _genServ.InsertOtp((OtpType)Request.RetrivalType, getuser.Id, sess, otp, con);
                        await _genServ.InsertOtpForAssetCapitalInsurance((OtpType)Request.RetrivalType, UserType, getCust.client_unique_ref, sess, otp, con, getCust.id);
                        await _genServ.SendOtp3((OtpType)Request.RetrivalType, otp, customerDataNotFromBvn?.phonenumber, _smsBLService, "Retrieval", customerDataNotFromBvn?.email);
                    }
                    //save in redis
                    if ((await _redisStorageService.GetCustomerAsync("asset_" + getCust.id) != null))
                    {
                        await _redisStorageService.RemoveCustomerAsync("asset_" + getCust.id);
                    }
                    DateTime dateTime = DateTime.Now;
                    string dateTimeString = dateTime.ToString("yyyy-MM-dd HH:mm:ss"); // Customize the format as needed
                    await _redisStorageService.SetCacheDataAsync("asset_" + getCust.id, otp + "_" + dateTimeString);
                    return new RetrivalResponse()
                    {
                        Success = true,
                        PhoneNumber = _genServ.MaskPhone(customerDataNotFromBvn?.phonenumber),
                        Response = EnumResponse.Successful,
                        SessionID = sess
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new RetrivalResponse() { Success = false, Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> ResetPassword(string v, AssetCapitalInsuranceResetPassword Request, string UserType)
        {
            try
            {
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new RetrivalResponse() { Response = g.Response, Success = g.Success, Message = g.Message };
                }
                if (string.IsNullOrEmpty(Request.PhoneNumberOrEmail) || string.IsNullOrEmpty(Request.NewPassword))
                    return new GenericResponse() { Response = EnumResponse.InvalidDetails };

                using (IDbConnection con = _context.CreateConnection())
                {
                    var getCust = await GetAssetCapitalInsuranceCustomerbyPhoneNumber(Request.PhoneNumberOrEmail, UserType, con);
                    if (getCust == null)
                        return new RetrivalResponse() { Response = EnumResponse.UserNotFound };
                    // var validateSession = await _genServ.ValidateSession(getuser.Username, Request.Session, Request.ChannelId, con);
                    var validateSession = await _genServ.ValidateSessionForAssetCapitalInsurance(Request.Session, UserType, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var checkpassword = _genServ.CheckPasswordCondition(Request.NewPassword);
                    if (!checkpassword)
                        return new GenericResponse() { Response = EnumResponse.PasswordConditionNotMet };
                    //await _genServ.SetUserCredential(CredentialType.Password, getuser.Id, Request.NewPassword, con, true);
                    // check data validity
                    /*
                    var otp = (await _redisStorageService.GetCustomerAsync("asset_" + getCust.id)).Split('_')[0];
                    var OtpTime = (await _redisStorageService.GetCustomerAsync("asset_" + getCust.id)).Split('_')[1];
                    DateTime parseddateTime = DateTime.Parse(OtpTime);
                    _logger.LogInformation("dateTimeString " + OtpTime);
                    DateTime dateTime = DateTime.Now;
                    // Calculate the difference
                    TimeSpan difference = dateTime - parseddateTime;
                    if (Math.Abs(difference.TotalMinutes) >= 3)
                    {
                        return new GenericResponse() { Response = EnumResponse.OtpTimeOut, Success = false };
                    }
                    */
                    await _genServ.SetAssetCapitalInsuranceUserCredential(CredentialType.Password, getCust.id, Request.NewPassword, con, true, UserType);
                    AssetCapitalInsuranceCustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getAssetCapitalInsuranceCustomerRelationService(con, (int)getCust.id, UserType);
                    Thread thread = new Thread(() =>
                    {
                        SendMailObject sendMailObject = new SendMailObject();
                        sendMailObject.Email = customerDataNotFromBvn?.email;
                        sendMailObject.Subject = _appSettings.AssetMailSubjectHeader + " Password Reset";
                        var data = new
                        {
                            title = "Password Reset",
                            username = getCust.username,
                            year = DateTime.Now.Year
                        };
                        string filepath = Path.Combine(_settings.PartialViews, "_passwordreset.html");
                        Console.WriteLine("filepath " + filepath);
                        _logger.LogInformation("filepath " + filepath);
                        string htmlcontent = _templateService.RenderScribanTemplate(filepath, data);
                        sendMailObject.Html = htmlcontent;
                        _genServ.SendMail(sendMailObject);
                        _genServ.LogRequestResponse("credentails email sent", "", "");
                    });
                    thread.Start();
                    return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Success = false, Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> ValidateOtherDeviceForOnBoarding(string v, AssetCapitalInsurancePhoneAndAccount Request, string UserType)
        {
            try
            {

                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new RetrivalResponse() { Response = g.Response, Success = g.Success, Message = g.Message };
                }
                if (!string.IsNullOrEmpty(Request.PhoneNumberOrEmail))
                {
                    // mobilenumber if the phonenumber is in dbs
                    using (IDbConnection con = _context.CreateConnection())
                    {
                        var getCust = await GetAssetCapitalInsuranceCustomerbyPhoneNumber(Request.PhoneNumberOrEmail, UserType, con);
                        if (getCust == null)
                            return new RetrivalResponse() { Response = EnumResponse.UserNotFound };
                        if (!string.IsNullOrEmpty(getCust.PhoneNumber))
                        {
                            string otp = _genServ.GenerateOtp();
                            // insert into otp_session 
                            string session = _genServ.GetSession();
                            int status = 1;
                            var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(Request.Username, UserType, con);
                            if (usr == null)
                            {
                                return new GenericResponse() { Response = EnumResponse.InvalidPhoneNumber };
                            }
                            await _genServ.InsertLogs(usr.id, session, Request.Device, "", $"Request - Device Unlock", con);
                            //2 stands probably means the user has more than  one device
                            // awaitcon.ExecuteAsync($"update mobiledevice set status = 2 where userid = {usr.Id}");
                            var presentdevice = (await con.QueryAsync<string>($"select device from asset_capital_insurance_mobile_device where user_id=@userid and user_type=@UserType", new { userid = usr.id, UserType = UserType })).ToList();
                            if (!presentdevice.Any())
                            {
                                string sql2 = $"insert into asset_capital_insurance_mobile_device (user_id, device, status, device_name, createdon,user_type) values(@userid,@dev,1,@devnam,sysdate(),'{UserType}')";
                                await con.ExecuteAsync(sql2, new { userid = usr.id, dev = Request.Device, devnam = Request.DeviceName });
                            }
                            string sql = "insert into asset_capital_insurance_otp_session(session, status, otp_type, otp, createdon,user_id,user_type) values (@session,1,@otp_type,@otp,sysdate(),@objid,@UserType)";
                            await con.ExecuteAsync(sql, new { session = session, otp_type = (int)OtpType.UnlockDevice, otp = otp, objid = usr.id, UserType = UserType });
                            await _genServ.SetAssetCapitalInsuranceUserSession(usr.id, UserType, session, 1, con);
                            AssetCapitalInsuranceCustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getAssetCapitalInsuranceCustomerRelationService(con, (int)usr.id, UserType);
                            await _genServ.SendOtp3(OtpType.UnlockDevice, otp, customerDataNotFromBvn?.phonenumber, _smsBLService, "Device Validatation", customerDataNotFromBvn?.email);
                            return new DeviceOnBoardReponse() { Session = session, Response = EnumResponse.Successful, Success = true };
                        }
                        else
                        {
                            return new GenericResponse() { Response = EnumResponse.InvalidPhoneNumber };
                        }
                    }
                }
                return new GenericResponse() { Response = EnumResponse.NotSuccessful };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> ValidateOtpToOnBoardOtherDevices(string v, DeviceOtpValidator Request, string UserType)
        {
            try
            {

                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new RetrivalResponse() { Response = g.Response, Success = g.Success, Message = g.Message };
                }
                using (IDbConnection con = _context.CreateConnection())
                {
                    //var resp = await _genServ.ValidateSessionOtp(OtpType.UnlockDevice, Request.Session, con);
                    var resp = await this.ValidateAssetCapitalInsuranceSessionOtp(OtpType.UnlockDevice, Request.Session, UserType, con, Request.Otp);
                    Console.WriteLine($"resp {resp}");
                    //check for otp time here 
                    if (resp == null || resp.otp != Request.Otp)
                        return new GenericResponse() { Response = EnumResponse.InvalidOtp };
                    _logger.LogInformation($"resp {JsonConvert.SerializeObject(resp)}");
                    DateTime createdat = resp.createdon;
                    // then insert into customer_device to onboard
                    var usr = (await con.QueryAsync<AssetCapitalInsuranceUsers>("select * from asset_capital_insurance_user where id = @id", new { id = resp.user_id })).FirstOrDefault();
                    Console.WriteLine("usr ...." + JsonConvert.SerializeObject(usr));
                    var devicecheck = (await con.QueryAsync<AssetCapitalInsuranceCustomerDevice>($"select * from asset_capital_insurance_customerdevice where username='{usr.username}' and login_status=1 and track_device='present' and user_type='{UserType}'"));
                    if (devicecheck.Any())
                    {
                        _logger.LogInformation("has logged in on a device ....");
                        var deviceCheckList = devicecheck.Select(d => d.device).ToList();
                        await con.ExecuteAsync("update asset_capital_insurance_customerdevice set track_device='recent',login_status=0 where device in @deviceCheckList and user_type=@UserType", new { deviceCheckList, UserType }); // unlog user on other device                                                                                                                                                                      // is any user presently logged-in in this present device
                        var DifferentUserSameDevice = await con.QueryAsync<AssetCapitalInsuranceCustomerDevice>($"select * from asset_capital_insurance_customerdevice where login_status=1 and track_device='present' and device=@device and username!='{usr.username}' and user_type='{UserType}'", new { device = Request.Device });
                        if (DifferentUserSameDevice.Any())
                        {
                            var deviceList = DifferentUserSameDevice.Select(d => d.device).ToList();
                            _logger.LogInformation("DifferentUserSameDevice " + DifferentUserSameDevice.Any());
                            await con.ExecuteAsync("update asset_capital_insurance_customerdevice set track_device='recent',login_status=0 where device in @deviceList and user_type=@UserType", new { deviceList, UserType });
                        }
                        // this device becomes the present device for the user
                        await con.ExecuteAsync("update asset_capital_insurance_customerdevice set track_device='present',login_status=1 where username=@Username and device=@device and user_type=@UserType", new { username = usr.username, device = Request.Device, UserType = UserType });
                    }
                    else
                    {
                        //has anybody logged in before.
                        var DifferentUserSameDevice = await con.QueryAsync<AssetCapitalInsuranceCustomerDevice>($"select * from asset_capital_insurance_customerdevice where login_status=1 and track_device='present' and device=@device and username!='{usr.username}' and user_type=@UserType", new { device = Request.Device, UserType = UserType });
                        if (DifferentUserSameDevice.Any())
                        {
                            var deviceList = DifferentUserSameDevice.Select(d => d.device).ToList();
                            _logger.LogInformation("DifferentUserSameDevice " + DifferentUserSameDevice.Any());
                            await con.ExecuteAsync("update asset_capital_insurance_customerdevice set track_device='recent',login_status=0 where device in @deviceList and user_type=@UserType", new { deviceList, UserType });
                        }
                        else
                        {
                            if (!(await con.QueryAsync<string?>("select username from asset_capital_insurance_customerdevice where username=@username and device=@device and user_type=@UserType", new { username = usr.username, device = Request.Device, UserType = UserType })).FirstOrDefault().HasValue())
                            {
                                await con.ExecuteAsync("insert into asset_capital_insurance_customerdevice(username,device,user_type,channelId,createdon) values(@username,@device,@usertype,1,UTC_TIMESTAMP())", new
                                {
                                    username = usr.username,
                                    usertype = UserType,
                                    device = Request.Device
                                });
                            }
                        }
                        // this device becomes the present device if he has never logged in before 
                        _logger.LogInformation("never logged in.just setting it to login");
                        await con.ExecuteAsync("update asset_capital_insurance_customerdevice set track_device='present',login_status=1 where username=@Username and device=@device and user_type=@UserType", new { username = usr.username, device = Request.Device, UserType = UserType });
                    }
                    //  await con.ExecuteAsync($"update otp_session set status = 0 where id = {resp.ID}");
                    Console.WriteLine($"id of user {resp.user_id}");
                    SendMailObject sendMailObject = new SendMailObject();
                    //sendMailObject.BvnEmail = usr.BvnEmail;
                    AssetCapitalInsuranceCustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getAssetCapitalInsuranceCustomerRelationService(con, (int)usr.id, UserType);
                    sendMailObject.Email = customerDataNotFromBvn?.email;
                    sendMailObject.Firstname = usr.first_name + " " + usr.last_name;
                    var DeviceName = (await con.QueryAsync<string>($"select device_name as devicename from  asset_capital_insurance_mobile_device where device='{Request.Device}' and user_type='{UserType}'")).FirstOrDefault();
                    _logger.LogInformation("DeviceName " + DeviceName);
                    //sendMailObject.Subject = "TrustBanc Bank Mobile App Device OnBoarding";
                    sendMailObject.Subject = _appSettings.AssetMailSubject + " Device OnBoarding";
                    var data = new
                    {
                        title = "Device Onboarding",
                        firstname = usr.first_name,
                        lastname = usr.last_name,
                        DeviceName = DeviceName,
                        LoginTime = DateTime.Now,
                        year = DateTime.Now.Year
                    };
                    string filepath = Path.Combine(_settings.PartialViews, "deviceonboarding.html");
                    Console.WriteLine("filepath " + filepath);
                    _logger.LogInformation("filepath " + filepath);
                    string htmlcontent = _templateService.RenderScribanTemplate(filepath, data);
                    sendMailObject.Html = htmlcontent;
                    Thread thread = new Thread(() =>
                    {
                        _genServ.LogRequestResponse("enter in thread to send email ", $" ", "");
                        _genServ.SendMail(sendMailObject);
                    });
                    thread.Start();
                    return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<ValidateOtpResponse> SendTypeOtp(OtpType registration, string username, string UserType, string typeOtp)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(username, UserType, con);
                    if (usr == null)
                    {
                        new ValidateOtpResponse() { Success = false, Response = EnumResponse.UsernameNotFound };
                    }
                    _logger.LogInformation("typeotp " + typeOtp);
                    string otp = _genServ.GenerateOtp();
                    //_logger.LogInformation();
                    AssetCapitalInsuranceCustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getAssetCapitalInsuranceCustomerRelationService(con, (int)usr.id, UserType);
                    OtpTransLimit otpTransLimit = new OtpTransLimit();
                    _logger.LogInformation("customerDataNotFromBvn " + JsonConvert.SerializeObject(customerDataNotFromBvn));
                    await _genServ.SendOtp3(OtpType.PinResetOrChange, otp, customerDataNotFromBvn.phonenumber, _smsBLService, "Confirmation", customerDataNotFromBvn?.email);
                    DateTime dateTime = DateTime.Now;
                    string dateTimeString = dateTime.ToString("yyyy-MM-dd HH:mm:ss"); // Customize the format as needed
                    _logger.LogInformation("dateTimeString " + dateTimeString);// Outputs: 2024-10-30 12:15:00
                    otpTransLimit.otp = otp;
                    otpTransLimit.PhoneNumber = customerDataNotFromBvn?.phonenumber;
                    otpTransLimit.DateTimeString = dateTimeString;
                    if (_redisStorageService.GetCustomerAsync($"{typeOtp}_{otpTransLimit.PhoneNumber}") != null)
                    {
                        _logger.LogInformation("removed key for pin");
                        await _redisStorageService.RemoveCustomerAsync($"{typeOtp}_{otpTransLimit.PhoneNumber}");
                    }
                    string key = $"{typeOtp}{otpTransLimit.PhoneNumber}";
                    _logger.LogInformation("key " + key + " otpTransLimit " + JsonConvert.SerializeObject(otpTransLimit));
                    await _redisStorageService.SetCacheDataAsync(key, otpTransLimit);
                    _logger.LogInformation("sent and saved successfully " + JsonConvert.SerializeObject(otpTransLimit));
                    return new ValidateOtpResponse() { Success = true, Response = EnumResponse.Successful };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new ValidateOtpResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse2> GetFullDetailsByClientReference(string UserName, string session, string UserType)
        {

            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Success = g.Success, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(session, UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not avaialble" };
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(UserName, UserType, con);
                string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.middlewarecustomerurl + "api/Customer/GetFullDetailsByClientRef/" + usr.client_unique_ref, "", true, header);
                _logger.LogInformation("response " + response);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (genericResponse2 == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    SimplexCustomerFullDetailsByClientRefResponse simplexCustomerFullDetailsByClientRefResponse = JsonConvert.DeserializeObject<SimplexCustomerFullDetailsByClientRefResponse>(JsonConvert.SerializeObject(genericResponse2.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = simplexCustomerFullDetailsByClientRefResponse, Message = "data collection successful from source" };

                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            return genericResponse2;
        }

        public async Task<GenericResponse2> GetRelationship(string session, string UserType)
        {

            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Success = g.Success, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(session, UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not avaialble" };
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                string response = await _genServ.CallServiceAsyncToString(Method.GET, _settings.middlewarecustomerurl + "api/Customer/GetRelationship", "", true, header);
                _logger.LogInformation("response " + response);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (genericResponse2 == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    SimplexCustomerRelationship simplexCustomerRelationship = JsonConvert.DeserializeObject<SimplexCustomerRelationship>(JsonConvert.SerializeObject(genericResponse2.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = simplexCustomerRelationship, Message = "data collection successful from source" };

                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            return genericResponse2;
        }

        public async Task<GenericResponse> ValidateOtpForOtherPurposes(string v, AssetCapitalInsuranceValidateOtpRetrival Request, string UserType)
        {
            try
            {
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return g;
                }
                if (string.IsNullOrEmpty(Request.PhoneNumberOrEmail))
                    return new GenericResponse() { Response = EnumResponse.InvalidDetails };

                using (IDbConnection con = _context.CreateConnection())
                {
                    //Check DB using PhoneNumber or email
                    var getCust = await GetAssetCapitalInsuranceCustomerbyPhoneNumber(Request.PhoneNumberOrEmail, UserType, con);
                    if (getCust == null)
                        return new RetrivalResponse() { Response = EnumResponse.UserNotFound };
                    if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(Request.Session, UserType, con)))
                    {
                        return new GenericResponse() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                    }
                    //save in redis
                    var otptimer = await _redisStorageService.GetCustomerAsync("asset_" + getCust.id);
                    if (otptimer == null)
                    {
                        return new GenericResponse()
                        {
                            Message = "Otp is no longer valid",
                            Response = EnumResponse.OtpTimeOut,
                            Success = false
                        };
                    }
                    // var otp  = otptimer.Split('_')[0];
                    // var datetimeString = otptimer.Split('_')[1];
                    var otp = otptimer.Split('_')[0];
                    _logger.LogInformation("otptimer " + otptimer);
                    var datetimeString = otptimer.Split('_')[1].Trim();
                    _logger.LogInformation($"dateTimeString {datetimeString}");
                    // Parse the string into a DateTime object
                    //  DateTime parseddateTime = DateTime.ParseExact(datetimeString.Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    //DateTime parseddateTime = DateTime.Parse(datetimeString);
                    // Remove unexpected characters (quotes, spaces)
                    datetimeString = datetimeString.Replace("\"", "").Trim();
                    // Validate date format before parsing
                    if (!DateTime.TryParseExact(datetimeString, "yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parseddateTime))
                    {
                        Console.WriteLine($"Error: Invalid date format - '{datetimeString}'");
                        return new GenericResponse() { Response = EnumResponse.InvalidDateformat, Message = "Invalid date format" };
                    }
                    DateTime dateTime = DateTime.Now;
                    // Calculate the difference
                    TimeSpan difference = dateTime - parseddateTime;
                    if (Math.Abs(difference.TotalMinutes) >= 10)
                    {
                        return new GenericResponse() { Response = EnumResponse.OtpTimeOut, Success = false };
                    }
                    var resp = await ValidateAssetCapitalInsuranceSessionOtp((OtpType)Request.RetrivalType, Request.Session, UserType, con, Request.Otp);
                    if (resp == null || resp.otp != Request.Otp)
                        return new ValidateOtpResponse() { Response = EnumResponse.InvalidOtp };
                    AssetCapitalInsuranceCustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getAssetCapitalInsuranceCustomerRelationService(con, (int)getCust.id, UserType);
                    if ((OtpType)Request.RetrivalType == OtpType.RetrieveUsername)
                    {
                        string msg = _appSettings.RetriveUsernameText;
                        msg = msg.Replace("{Username}", getCust?.username);
                        var request = new SendSmsRequest()
                        {
                            ClientKey = "",
                            Message = msg,
                            PhoneNumber = getCust?.PhoneNumber,
                            SmsReference = DateTime.Now.ToString("ddMMyyyyHHmmss") + "_" + new Random().Next(11111, 99999)
                        };
                        // await _genServ.SendSMS(request);
                        Task.Run(async () =>
                        {
                            customerDataNotFromBvn.phonenumber = !string.IsNullOrEmpty(customerDataNotFromBvn?.phonenumber) ? customerDataNotFromBvn?.phonenumber : getCust?.PhoneNumber;
                            var msg = $@"Dear {getCust?.first_name},your username is {getCust?.username}.Thank you for banking with us.";
                            customerDataNotFromBvn.phonenumber = CustomerServiceNotFromBvnService.ReplaceFirstDigit(customerDataNotFromBvn?.phonenumber, "234");
                            GenericResponse response = await _smsBLService.SendSmsNotificationToCustomer("Otp", customerDataNotFromBvn?.phonenumber, $@"{msg}", "AccountNumber Creation", _appSettings.SmsUrl);
                            _logger.LogInformation("response " + response.ResponseMessage + " message " + response.Message);
                        });
                        SendMailObject sendMailObject = new SendMailObject();
                        //sendMailObject.BvnEmail = getuser.BvnEmail;
                        sendMailObject.Email = customerDataNotFromBvn?.email;
                        sendMailObject.Subject = _appSettings.AssetMailSubject + "Password Retrieval";
                        var data = new
                        {
                            title = "Retrieval Email",
                            firstname = FirstLetterUppercaseMaker.CapitalizeFirstLetter(getCust.first_name),
                            lastname = FirstLetterUppercaseMaker.CapitalizeFirstLetter(getCust.last_name),
                            year = DateTime.Now.Year,
                            username = !string.IsNullOrEmpty(getCust.username) ? getCust.username : customerDataNotFromBvn.username // Dynamically pass the current year
                        };
                        string filepath = Path.Combine(_settings.PartialViews, "usernametemplate.html");
                        Console.WriteLine("filepath " + filepath);
                        _logger.LogInformation("filepath " + filepath);
                        string htmlContent = _templateService.RenderScribanTemplate(filepath, data);
                        sendMailObject.Html = htmlContent;
                        Thread thread = new Thread(() =>
                        {
                            _logger.LogInformation("mail sending");
                            _genServ.SendMail(sendMailObject);
                            _logger.LogInformation("mail sent");
                        });
                        thread.Start();
                    }
                    if ((OtpType)Request.RetrivalType == OtpType.UnlockDevice)
                        await con.ExecuteAsync($"update asset_capital_insurance_mobile_device set status = 1 where user_id = {getCust.id} and status = 3 and user_type='{UserType}'");
                    if ((OtpType)Request.RetrivalType == OtpType.UnlockProfile)
                        await con.ExecuteAsync($"update asset_capital_insurance_user set status = 1 where id = {getCust.id} and user_type='{UserType}'");
                    // await con.ExecuteAsync($"update otp_session set status = 0 where id = {resp.ID}");
                    return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Success = false, Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse2> GetInhouseBanks(string UserName, string session, string UserType)
        {
            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(session, UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not avaialble" };
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                string response = await _genServ.CallServiceAsyncToString(Method.GET, _settings.middlewarecustomerurl + "api/Customer/GetInhouseBanks", "", true, header);
                _logger.LogInformation("response " + response);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (genericResponse2 == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    SimplexInHouseBanksResponse simplexCustomerFullDetailsByClientRefResponse = JsonConvert.DeserializeObject<SimplexInHouseBanksResponse>(JsonConvert.SerializeObject(genericResponse2.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = simplexCustomerFullDetailsByClientRefResponse, Message = "data collection successful from source" };
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Success = false, Response = EnumResponse.SystemError };
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            return genericResponse2;
        }

        public async Task<GenericResponse2> GetOccupations(string UserName, string session, string UserType)
        {
            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(session, UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not avaialble" };
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                string response = await _genServ.CallServiceAsyncToString(Method.GET, _settings.middlewarecustomerurl + "api/Customer/GetOccupations", "", true, header);
                _logger.LogInformation("response " + response);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (genericResponse2 == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    Occupations simplexCustomerFullDetailsByClientRefResponse = JsonConvert.DeserializeObject<Occupations>(JsonConvert.SerializeObject(genericResponse2.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = simplexCustomerFullDetailsByClientRefResponse, Message = "data collection successful from source" };
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Success = false, Response = EnumResponse.SystemError };
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            return genericResponse2;
        }

        public async Task<GenericResponse2> GetReligious(string UserName, string Session, string UserType)
        {
            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(Session, UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not avaialble" };
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                string response = await _genServ.CallServiceAsyncToString(Method.GET, _settings.middlewarecustomerurl + "api/Customer/GetReligious", null, true, header);
                _logger.LogInformation("response " + response);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (genericResponse2 == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    Religions religions = JsonConvert.DeserializeObject<Religions>(JsonConvert.SerializeObject(genericResponse2.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = religions, Message = "data collection successful from source" };
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Success = false, Response = EnumResponse.SystemError };
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            return genericResponse2;
        }

        public async Task<GenericResponse2> GetWalletBalance(string UserName, string Session, string UserType, string currency, int clientid)
        {
            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(Session, UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not avaialble" };
                }
                var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(UserName, UserType, con);
                if (usr == null)
                {
                    new GenericResponse2() { Success = false, Response = EnumResponse.ClientIdNotFound, Message = "Client is not found" };
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                string response = await _genServ.CallServiceAsyncToString(Method.GET, _settings.middlewarecustomerurl + "api/Customer/GetWalletBalance/" + currency + "/" + usr.client_unique_ref, null, true, header);
                _logger.LogInformation("response " + response);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (genericResponse2 == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    SimplexWalletBalance SimplexWalletBalance = JsonConvert.DeserializeObject<SimplexWalletBalance>(JsonConvert.SerializeObject(genericResponse2.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = SimplexWalletBalance, Message = "data collection successful from source" };
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Success = false, Response = EnumResponse.SystemError };
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            return genericResponse2;
        }

        public async Task<GenericResponse2> AddBankOrUpdateForClient(SimplexClientBankDetailsDto simplexClientBankDetails)
        {
            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = ValidateUserType(simplexClientBankDetails.UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(simplexClientBankDetails.Session, simplexClientBankDetails.UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not avaialble" };
                }
                var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(simplexClientBankDetails.UserName, simplexClientBankDetails.UserType, con);
                if (usr == null)
                {
                    new GenericResponse2() { Success = false, Response = EnumResponse.ClientIdNotFound, Message = "Client is not found" };
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                int bankid = simplexClientBankDetails.idBank == 0 ? 0 : simplexClientBankDetails.idBank;
                var simplexClientBankDetails1 = new SimplexClientBankDetails()
                {
                    accountName = simplexClientBankDetails.accountName,
                    accountNumber = simplexClientBankDetails.accountNumber,
                    bvn = usr?.bvn,
                    client_unique_ref = (int)usr.client_unique_ref,
                    id = bankid,
                    idBank = simplexClientBankDetails.idOfTheBankFromSimplex
                };
                string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.middlewarecustomerurl + "api/Customer/AddBankOrUpdateForClient", simplexClientBankDetails1, true, header);
                _logger.LogInformation("response " + response);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (genericResponse2 == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    SimplexClientBankDetailsResponse SimplexClientBankDetailsResponse = JsonConvert.DeserializeObject<SimplexClientBankDetailsResponse>(JsonConvert.SerializeObject(genericResponse2?.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = SimplexClientBankDetailsResponse, Message = "data collection successful from source" };
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Success = false, Response = EnumResponse.SystemError };
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            SimplexClientBankDetailsResponse SimplexClientBankDetailsResponse1 = JsonConvert.DeserializeObject<SimplexClientBankDetailsResponse>((string)(genericResponse2?.data));
            genericResponse2.data = SimplexClientBankDetailsResponse1;
            return genericResponse2;
        }

        public async Task<ValidateAccountResponse> BankAccountNameEnquiry(string UserName, string Session, string UserType, string AccountNumber, string BankCode)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new ValidateAccountResponse() { Response = g.Response, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(Session, UserType, con)))
                {
                    return new ValidateAccountResponse() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                var result = new ValidateAccountResponse();
                var chkname = await _genServ.ValidateNumberOnly(AccountNumber, BankCode);
                result.Success = chkname.Success;
                result.Response = chkname.Response;
                result.AccountName = chkname.AccountName;
                var accountNameWords = chkname.AccountName.Split(' ').ToList();
                var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(UserName, UserType, con);
                int counter = 0;
                if (usr != null)
                {
                    bool isFirstNameInAccountName = accountNameWords.Contains(usr.first_name, StringComparer.OrdinalIgnoreCase);
                    bool isLastNameInAccountName = accountNameWords.Contains(usr.last_name, StringComparer.OrdinalIgnoreCase);
                    // bool isMiddleNameInAccountName = accountNameWords.Contains(usr., StringComparer.OrdinalIgnoreCase);
                    // Output the result
                    if (!isFirstNameInAccountName)
                    {
                        //Console.WriteLine($"The first name '{firstName}' is found in the account name.");
                        counter = counter + 1;
                    }
                    if (!isLastNameInAccountName)
                    {
                        //Console.WriteLine($"The first name '{firstName}' is found in the account name.");
                        counter = counter + 1;
                    }
                    result.AllowedForTransaction = true;
                    if (counter >= 2)
                    {
                        result.IsAccountOwner = false;
                    }
                }
                else
                {
                    result.IsAccountOwner = false;
                }
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
                return new ValidateAccountResponse() { Response = EnumResponse.NoAccountExist, Message = "Account not found" };
            }
            //return null;
        }

        public async Task<GenericResponse2> GetAllBanks(string userName, string session, string userType)
        {
            var resp = await _genServ.GetBanks();
            var banks = new List<Banks>();
            foreach (var n in resp)
                banks.Add(new Banks()
                {
                    Bankcode = n.BankCode,
                    BankName = n.Bankname.ToUpper()
                });
            //_genServ.LogRequestResponse("GetAllBanks", "", JsonConvert.SerializeObject(resp));
            int i = banks.RemoveAll(e => e.BankName.ToLower().Replace(" ", "").Equals("TRUSTBANC J6 MFB".ToLower().Replace(" ", ""), StringComparison.CurrentCultureIgnoreCase));
            _logger.LogInformation("removed successfully " + 1);
            return new GenericResponse2() { data = banks, Response = EnumResponse.Successful, Success = true };
        }
        private int GetLastDigit(string accountnumber, string bankcode)
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

        public async Task<BankList> GetPossibleBanks(string AccountNumber, string Session, string UserType)
        {
            try
            {
                // call api here first to get suggested banks
                var SuggestedBankResponse = await _genServ.CallServiceAsyncToString(Method.GET, _appSettings.SuggestedBankUrl + AccountNumber, "", true);
                GenericResponse2 SuggestedBank = JsonConvert.DeserializeObject<GenericResponse2>(SuggestedBankResponse);
                string json = JsonConvert.SerializeObject(SuggestedBank.data);
                List<Bank> banks = JsonConvert.DeserializeObject<List<Bank>>(json);
                var suggestedresult = new List<Banks>();
                if (banks.Any())
                {
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
                    result.Add(new Banks() { Bankcode = _appSettings.TrustBancBankCode, BankName = _appSettings.TrustBancBankName });

                var bnks = await _genServ.GetBanks();

                Console.WriteLine("bnks " + bnks);
                if (!bnks.Any())
                    return new BankList() { Banks = result, Response = EnumResponse.Successful, Success = result.Any() };
                // foreach (var n in bnks.Where(x => !string.IsNullOrEmpty(x.CbnCode)).OrderBy(p => p.Bankname))
                //foreach (var n in bnks)
                foreach (var n in bnks)
                {
                    Console.WriteLine("checking BankCode {0}", n.BankCode);
                    if (n.BankCode == _appSettings.TrustBancBankCode)
                        continue;

                    int lastdigit = GetLastDigit(AccountNumber, n.BankCode);
                    string serialNumber = AccountNumber.Substring(0, 9);
                    //int lastdigit2  = GenerateCheckDigit(serialNumber, n.BankCode);
                    if (lastdigit == 99)
                        continue;

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

        public async Task<GenericResponse2> AddOrUpdateClientMinor(SimplexClientMinorDto simplexClientMinor)
        {
            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = ValidateUserType(simplexClientMinor.UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(simplexClientMinor.Session, simplexClientMinor.UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not avaialble" };
                }
                var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(simplexClientMinor.UserName, simplexClientMinor.UserType, con);
                if (usr == null)
                {
                    new GenericResponse2() { Success = false, Response = EnumResponse.ClientIdNotFound, Message = "Client is not found" };
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                var SimplexClientMinor = new SimplexClientMinor()
                {
                    address_line1 = simplexClientMinor.address_line1,
                    address_line2 = simplexClientMinor.address_line2,
                    dateOfbirth = simplexClientMinor.dateOfbirth,
                    client_unique_ref = (int)usr.client_unique_ref,
                    email = simplexClientMinor.email,
                    firstName = simplexClientMinor.firstName,
                    lastName = simplexClientMinor.lastName,
                    guardianIsClient = simplexClientMinor.guardianIsClient,
                    mobile_phone = simplexClientMinor.mobile_phone,
                    otherName = simplexClientMinor.otherName,
                    Ucid = (int)usr.client_unique_ref
                };
                string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.middlewarecustomerurl + "api/Customer/AddOrUpdateClientMinor", SimplexClientMinor, true, header);
                _logger.LogInformation("response " + response);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (genericResponse2 == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    SimplexClientBankDetailsResponse SimplexClientBankDetailsResponse = JsonConvert.DeserializeObject<SimplexClientBankDetailsResponse>(JsonConvert.SerializeObject(genericResponse2?.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = SimplexClientBankDetailsResponse, Message = "data collection successful from source" };
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Success = false, Response = EnumResponse.SystemError };
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            return genericResponse2;
        }

        public async Task<GenericResponse2> AddOrUpdateClientNextOfKin(SimplexClientNextOfKinDto simplexClientNextOfKin)
        {
            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = ValidateUserType(simplexClientNextOfKin.UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(simplexClientNextOfKin.Session, simplexClientNextOfKin.UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not avaialble" };
                }
                var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(simplexClientNextOfKin.UserName, simplexClientNextOfKin.UserType, con);
                if (usr == null)
                {
                    new GenericResponse2() { Success = false, Response = EnumResponse.ClientIdNotFound, Message = "Client is not found" };
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                var SimplexClientNextOfKin = new SimplexClientNextOfKin()
                {

                    client_unique_ref = (int)usr.client_unique_ref,
                    otherName = simplexClientNextOfKin.otherName,
                    firstName = simplexClientNextOfKin.firstName,
                    lastName = simplexClientNextOfKin.lastName,
                    email = simplexClientNextOfKin.email,
                    idRelationship = simplexClientNextOfKin.idRelationship,
                    phoneNumber = simplexClientNextOfKin.phoneNumber,
                    id = simplexClientNextOfKin.id,
                    address=simplexClientNextOfKin.address
                };
                string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.middlewarecustomerurl + "api/Customer/AddOrUpdateClientNextOfKin", SimplexClientNextOfKin, true, header);
                _logger.LogInformation("response " + response);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (genericResponse2 == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    SimplexClientNextOfKinResponse simplexClientNextOfKinResponse = JsonConvert.DeserializeObject<SimplexClientNextOfKinResponse>(JsonConvert.SerializeObject(genericResponse2?.data));
                    //simplexClientNextOfKinResponse.data.Add(SimplexClientNextOfKin);
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = simplexClientNextOfKinResponse, Message = "data collection successful from source" };
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Success = false, Response = EnumResponse.SystemError };
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            genericResponse2.data= JsonConvert.DeserializeObject<SimplexClientNextOfKinResponse>(JsonConvert.SerializeObject(genericResponse2?.data));
            return genericResponse2;
        }

        static bool TryParseDate(string dateString, string[] formats, out DateTime date)
        {
            return DateTime.TryParseExact(dateString, formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out date);
        }

        public async Task<GenericResponse2> CompareNinAndBvnForValidation(string clientKey, string Session, string Username, int channelId, string nin, string UserType, string inputbvn = null)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var g = ValidateUserType(UserType);
                    if (!g.Success)
                    {
                        return new GenericResponse2() { Response = g.Response, Message = g.Message };
                    }
                    if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(Session, UserType, con)))
                    {
                        return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                    }
                    var requestobject = new
                    {
                        number_nin = nin
                    };
                    //get token
                    var loginobj = new
                    {
                        username = "itservices@trustbancgroup.com",
                        password = "trust@banc"
                    };
                    _logger.LogInformation("valloginusername " + _appSettings.authloginusername);
                    _logger.LogInformation("auth password " + _appSettings.authpassword);
                    _logger.LogInformation("about to  login for nin " + JsonConvert.SerializeObject(loginobj));
                    //http://localhost:8080/MFB_USSD/api/v1/user/login
                    //http://localhost:9001/api/v1/user/login
                    string Loginresponse = await _genServ.CallServiceAsyncToString(Method.POST, "http://localhost:8080/MFB_USSD/api/v1/user/login", loginobj, true);
                    JObject loginjson = (JObject)JToken.Parse(Loginresponse);
                    string accessToken = loginjson.ContainsKey("response") ? loginjson["response"]["accessToken"].ToString() : "";
                    if (string.IsNullOrEmpty(accessToken))
                    {
                        return new GenericResponse2() { Response = EnumResponse.NotSuccessful };
                    }
                    Dictionary<string, string> header = new Dictionary<string, string>
                        {
                            { "Authorization", "Bearer " + accessToken }
                        };
                    //  Console.WriteLine("CustNinUrlFromUssd ");
                    _logger.LogInformation("calling nin endpoint .....");
                    //http://localhost:8080/MFB_USSD/api/v1/verification/nin
                    // http://localhost:8080/MFB_USSD/api/v1/user/login
                    string response = await _genServ.CallServiceAsyncToString(Method.POST, "http://localhost:8080/MFB_USSD/api/v1/verification/nin", requestobject, true, header);
                    var settings = new JsonSerializerSettings
                    {
                        MissingMemberHandling = MissingMemberHandling.Ignore
                    };
                    NinValidationResponse ninValidation = JsonConvert.DeserializeObject<NinValidationResponse>(response, settings);
                    var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(Username, UserType, con);
                    if (!(ninValidation.response_code == "00" && ninValidation.ninData.residence_address.ToLower().Contains("suspended")))
                    {
                        string ninName = ninValidation.ninData.firstname + " " + ninValidation.ninData.surname + " " + ninValidation.ninData.middlename;
                        string ninDateOfBirth = ninValidation.ninData.birthdate;
                        string CustFirstName = usr.first_name;
                        string CustLastName = usr.last_name;
                        string custbvn = inputbvn == null ? usr.bvn : inputbvn;
                        Console.WriteLine("custbvn " + custbvn);
                        var result = Task.Run(async () =>
                        {
                            // http://localhost:8080/MFB_USSD/api/v1/verification/bvn
                            //http://localhost:9001/api/v1/verification/bvn
                            string bvnurl = "http://localhost:8080/MFB_USSD/api/v1/verification/bvn";
                            string testbvnurl = _appSettings.newbvnurl;
                            Console.WriteLine("testbvnurl ....." + testbvnurl);
                            var bvnobj = new
                            {
                                bvn = custbvn
                            };
                            string bvnresponse = await _genServ.CallServiceAsyncToString(Method.POST, bvnurl, bvnobj, true, header);
                            _logger.LogInformation("bvnresponse " + bvnresponse);
                            CustomerBvn customerBvn = JsonConvert.DeserializeObject<CustomerBvn>(bvnresponse, settings);
                            return customerBvn;
                        });
                        CustomerBvn customerBvn = await result;
                        if (customerBvn == null)
                        {
                            return new GenericResponse2() { Success = true, Response = EnumResponse.Successful };
                        }
                        _logger.LogInformation("customerBvn " + JsonConvert.SerializeObject(customerBvn));
                        string CustBirthDate = customerBvn.data.dateOfBirth;
                        string bvnFirstName = customerBvn.data.firstName.ToLower();
                        string bvnlastName = customerBvn.data.lastName.ToLower();
                        string bvnmiddleName = customerBvn.data.middleName.ToLower();
                        string bvnfirstName_LastName = bvnFirstName + "" + bvnlastName;
                        string bvnfirstName_middleName = bvnFirstName + " " + bvnmiddleName;
                        string bvnlastName_middleName = bvnlastName + " " + bvnmiddleName;
                        bool AtLeastTwoNamesPresent(string ninName, string firstname, string lastname, string middlename)
                        {
                            // Logic to check if at least two names are present
                            List<string> foundNames = new List<string>();
                            if (ninName.Contains(firstname, StringComparison.OrdinalIgnoreCase))
                            {
                                foundNames.Add(firstname.ToLower());
                            }

                            if (ninName.Contains(lastname, StringComparison.OrdinalIgnoreCase))
                            {
                                foundNames.Add(lastname.ToLower());
                            }

                            if (ninName.Contains(middlename, StringComparison.OrdinalIgnoreCase))
                            {
                                foundNames.Add(middlename.ToLower());
                            }

                            // Check if at least two unique names are present
                            int uniqueCount = new HashSet<string>(foundNames).Count;
                            return uniqueCount >= 2;
                        }
                        // bool NameCheck = (ninName.ToLower().Contains(CustFirstName.ToLower()) || ninName.ToLower().Contains(CustLastName.ToLower()));
                        bool NameCheck = AtLeastTwoNamesPresent(ninName, bvnFirstName, bvnlastName, bvnmiddleName);
                        string dateString1 = CustBirthDate;
                        string dateString2 = ninDateOfBirth;
                        string[] formats = { "dd-MMM-yyyy", "dd-MM-yyyy" };
                        if (NameCheck)
                        {
                            if (TryParseDate(dateString1, formats, out DateTime date1) && TryParseDate(dateString2, formats, out DateTime date2))
                            {
                                if (date1.Date == date2.Date)
                                {
                                    // _logger.LogInformation("The dates are equal.");
                                    // update nin in registration
                                    var ninInDB = (await con.QueryAsync<string>("select nin from asset_capital_insurance_registration where username=@usrname", new { usrname = Username })).FirstOrDefault();
                                    if (string.IsNullOrEmpty(ninInDB))
                                    {
                                        await con.ExecuteAsync("update asset_capital_insurance_registration set nin=@ninInDB where username=@usrname", new { ninInDB = nin, usrname = Username });
                                        // await con.ExecuteAsync("update registration set nin=@ninInDB where username=@usrname", new { ninInDB = nin, usrname = username });
                                    }
                                    return new GenericResponse2() { Success = true, Response = EnumResponse.Successful };
                                }
                                else
                                {
                                    // Console.WriteLine("The dates are not equal.");
                                    return new GenericResponse2() { Success = false, Response = EnumResponse.DateMismatch };
                                }
                            }
                        }
                        else
                        {
                            return new GenericResponse2() { Success = false, Response = EnumResponse.NameMisMatch };
                        }
                        // return new GenericResponse() { Success=true, Response = EnumResponse.ValidNin };
                    }
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InValidNin };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse2> ClientCountries(string UserType, string Session)
        {
            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(Session, UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not avaialble" };
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                string response = await _genServ.CallServiceAsyncToString(Method.GET, _settings.middlewarecustomerurl + "api/Customer/GetCountries", null, true, header);
                _logger.LogInformation("response " + response);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (genericResponse2 == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    ClientCountries ClientCountries = JsonConvert.DeserializeObject<ClientCountries>(JsonConvert.SerializeObject(genericResponse2.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = ClientCountries, Message = "data collection successful from source" };
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Success = false, Response = EnumResponse.SystemError };
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            return genericResponse2;
        }

        public async Task<GenericResponse2> ClientStates(string Session, string UserType)
        {
            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(Session, UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not avaialble" };
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                string response = await _genServ.CallServiceAsyncToString(Method.GET, _settings.middlewarecustomerurl + "api/Customer/GetStates", null, true, header);
                _logger.LogInformation("response " + response);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (genericResponse2 == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    ClientStates ClientStates = JsonConvert.DeserializeObject<ClientStates>(JsonConvert.SerializeObject(genericResponse2.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = ClientStates, Message = "data collection successful from source" };
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Success = false, Response = EnumResponse.SystemError };
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            return genericResponse2;
        }

        public async Task<GenericResponse2> ClientLga(string state, string Session, string UserType)
        {
            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(Session, UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not avaialble" };
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                string response = await _genServ.CallServiceAsyncToString(Method.GET, _settings.middlewarecustomerurl + "api/Customer/GetLga/" + state, null, true, header);
                _logger.LogInformation("response " + response);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (genericResponse2 == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    ClientLgas ClientStates = JsonConvert.DeserializeObject<ClientLgas>(JsonConvert.SerializeObject(genericResponse2.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = ClientStates, Message = "data collection successful from source" };
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Success = false, Response = EnumResponse.SystemError };
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            return genericResponse2;
        }

        public async Task<GenericResponse2> GetSourcesOffund(string Session, string UserType)
        {
            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(Session, UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not avaialble" };
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                string response = await _genServ.CallServiceAsyncToString(Method.GET, _settings.middlewarecustomerurl + "api/Customer/GetSourcesOffund", null, true, header);
                _logger.LogInformation("response " + response);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (genericResponse2 == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    SourceOfFund SourceOfFund = JsonConvert.DeserializeObject<SourceOfFund>(JsonConvert.SerializeObject(genericResponse2.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = SourceOfFund, Message = "data collection successful from source" };
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Success = false, Response = EnumResponse.SystemError };
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            return genericResponse2;
        }

        public async Task<GenericResponse> ValidatePin(PinValidator pinValidator, string UserType)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return g;
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(pinValidator.Session, UserType, con)))
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string encryptedPin = _genServ.EncryptString(pinValidator.Pin);
                var pin = (await con.QueryAsync<string>("select credential from asset_capital_insurance_user_credentials where credential_type=2 and status=1 and user_id=(select id from asset_capital_insurance_user where Username=@Username)", new { username = pinValidator.Username })).FirstOrDefault();
                _logger.LogInformation("encryptedPin " + encryptedPin + " Pin " + pin);
                var passwordCheck = encryptedPin == pin;
                if (encryptedPin == pin)
                {
                    return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
                }
                return new GenericResponse() { Response = EnumResponse.InvalidTransactionPin };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Success = false, Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse2> RemoveBankOrUpdateForClient(SimplexClientBankDetailsRemovalDto simplexClientBankDetailsRemovalDto,string UserType)
        {
            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(simplexClientBankDetailsRemovalDto.Session,UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not avaialble" };
                }
                var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(simplexClientBankDetailsRemovalDto.UserName,UserType, con);
                if (usr == null)
                {
                    new GenericResponse2() { Success = false, Response = EnumResponse.ClientIdNotFound, Message = "Client is not found" };
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.middlewarecustomerurl + "api/Customer/RemoveBankOrUpdateForClient/"+usr.client_unique_ref+"/"+simplexClientBankDetailsRemovalDto.idBankAccount,null, true, header);
                _logger.LogInformation("response " + response);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (genericResponse2 == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    SimplexGenericResponse simplexGenericResponse = JsonConvert.DeserializeObject<SimplexGenericResponse>(JsonConvert.SerializeObject(genericResponse2?.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = simplexGenericResponse, Message = "data collection successful from source" };
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Success = false, Response = EnumResponse.SystemError };
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            return genericResponse2;
        }
    }

}





