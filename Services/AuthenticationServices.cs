using Dapper;
//using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlX.XDevAPI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Asn1.Ocsp;
using RestSharp;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using Retailbanking.Common.DbObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using static Org.BouncyCastle.Bcpg.Attr.ImageAttrib;
using static System.Net.WebRequestMethods;
using iText.StyledXmlParser.Jsoup.Nodes;

namespace Retailbanking.BL.Services
{
    public class AuthenticationServices : IAuthentication
    {
        private readonly ILogger<AuthenticationServices> _logger;
        private readonly AppSettings _settings;
        private readonly Tier1AccountLimitInfo _tier1AccountLimitInfo;
        private readonly Tier2AccountLimitInfo _tier2AccountLimitInfo;
        private readonly Tier3AccountLimitInfo _tier3AccountLimitInfo;
        private readonly CustomerChannelLimit _customerChannelLimit;
        private readonly AccountChannelLimit _accountChannelLimit;
        private readonly AccountLimitType _accountLimitType;
        private readonly IndemnityType _indemnityType;
        private readonly SmtpDetails smtpDetails;
        private readonly IGeneric _genServ;
        private readonly IMemoryCache _cache;
        private readonly IBeneficiary _beneficiary;
        private readonly DapperContext _context;
        private readonly IFileService _fileService;
        private readonly ISmsBLService _smsBLService;
        private readonly IUserCacheService _userCacheService;
        private readonly IRedisStorageService _redisStorageService;
        public AuthenticationServices(IBeneficiary beneficiary,IOptions<IndemnityType> indemnityType, IOptions<AccountLimitType> accountLimitType, IOptions<AccountChannelLimit> accountChannelLimit, IOptions<CustomerChannelLimit> customerChannelLimit, IRedisStorageService redisStorageService, IUserCacheService userCacheService, IOptions<Tier3AccountLimitInfo> tier3AccountLimitInfo, IOptions<Tier2AccountLimitInfo> tier2AccountLimitInfo, IOptions<Tier1AccountLimitInfo> tier1AccountLimitInfo, ISmsBLService smsBLService, IFileService fileService, ILogger<AuthenticationServices> logger, IOptions<AppSettings> options, IOptions<SmtpDetails> options1, IGeneric genServ, IMemoryCache memoryCache, DapperContext context)
        {
             _accountLimitType=accountLimitType.Value;
            _indemnityType = indemnityType.Value;
            _accountChannelLimit = accountChannelLimit.Value;
            _customerChannelLimit = customerChannelLimit.Value;
            _userCacheService = userCacheService;
            _redisStorageService = redisStorageService;
            _logger = logger;
            _settings = options.Value;
           // _staffUserService = staffUserService;
            _tier1AccountLimitInfo = tier1AccountLimitInfo.Value;
            _tier2AccountLimitInfo = tier2AccountLimitInfo.Value;
            _tier3AccountLimitInfo = tier3AccountLimitInfo.Value;
            smtpDetails = options1.Value;
            _beneficiary = beneficiary;
            _genServ = genServ;
            _cache = memoryCache;
            _context = context;
            _fileService = fileService;
            _smsBLService = smsBLService;
        }

        public async Task<GenericResponse2> MigrateCustomerBeneficiaresToPrime2(long UserId,bool migrateduser,bool isbeneficiarymigrated, string Username)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var CheckIfuserHasBeneficiaries = (await con.QueryAsync<BeneficiaryModel?>("select UserId,Name,Value,ServiceName from beneficiary where UserId=@userid;", new { userid = UserId })).ToList();
                    List<CustomerBeneficiaries> customerBeneficiaries = null;
                    var response = await _genServ.CallServiceAsyncToString(Method.GET, $"{_settings.FinedgeUrl}api/Enquiry/MigrateCustomerBeneficiariesToPrime2/{Username}", null, true);
                    _logger.LogInformation("response " + response);
                    GenericResponse2 beneficiaries = JsonConvert.DeserializeObject<GenericResponse2>(response);
                    _logger.LogInformation("beneficiaries " + JsonConvert.SerializeObject(beneficiaries.data));
                    customerBeneficiaries = JsonConvert.DeserializeObject<List<CustomerBeneficiaries>>(JsonConvert.SerializeObject(beneficiaries.data));
                    _logger.LogInformation("customerBeneficiaries " + JsonConvert.SerializeObject(customerBeneficiaries));
                    if (!CheckIfuserHasBeneficiaries.Any())
                    {                      
                        foreach (var custben in customerBeneficiaries)
                        {
                            _logger.LogInformation(" custben "+ JsonConvert.SerializeObject(custben));
                            var savebn = new BeneficiaryModel()
                            {
                                Name = custben.BeneficiaryFullName,
                                Value = custben.BeneficiaryAccount,
                                ServiceName = custben.BeneficiaryBank,
                                Code = custben.BeneficiaryBankcode,
                                BeneficiaryType = 1
                            };
                           await _beneficiary.SaveBeneficiary(UserId,savebn,con,BeneficiaryType.Transfer);
                            _logger.LogInformation("inserted " + JsonConvert.SerializeObject(custben));
                        }
                        await con.ExecuteAsync("update users set isbeneficiarymigrated=true where id=@userid", new { userid = UserId });
                        _logger.LogInformation("user beneficiary status updated ....");
                        return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = customerBeneficiaries };
                    }
                    else {
                        _logger.LogInformation("CheckIfuserHasBeneficiaries " + CheckIfuserHasBeneficiaries.Any());
                        if (customerBeneficiaries!=null)
                        {
                            if (customerBeneficiaries.Any())
                            {
                                foreach (var checker in CheckIfuserHasBeneficiaries)
                                {
                                    var model = customerBeneficiaries
                                     .Where(e =>
                                     {                               
                                         _logger.LogInformation("BeneficiaryAccount " + e.BeneficiaryAccount + " checker value " + checker.Value);
                                         _logger.LogInformation("BeneficiaryAccount code " + e.BeneficiaryBankcode + " checker value " + checker.Code);
                                         return e.BeneficiaryAccount != checker.Value && e.BeneficiaryBankcode!=checker.Code;
                                     })
                                     .ToList(); // Convert the filtered results to a List
                                    _logger.LogInformation("model "+JsonConvert.SerializeObject(model));
                                    if(model.Any()) {
                                        foreach (var m in model)
                                        {
                                            _logger.LogInformation("m "+JsonConvert.SerializeObject(m));
                                            var savebn = new BeneficiaryModel()
                                            {
                                                Name = m.BeneficiaryFullName,
                                                Value = m.BeneficiaryAccount,
                                                ServiceName = m.BeneficiaryBank,
                                                Code = m.BeneficiaryBankcode,
                                                BeneficiaryType = 1
                                            };
                                            await _beneficiary.SaveBeneficiary(UserId, savebn, con, BeneficiaryType.Transfer);
                                            _logger.LogInformation("m inserted " + JsonConvert.SerializeObject(m));
                                        }
                                    }                                 
                                }
                            }
                      
                        }
                      //await con.ExecuteAsync("üpdate users set isbeneficiarymigrated=true where id=@userid",new { userid=UserId});
                        await con.ExecuteAsync("update users set isbeneficiarymigrated=true where id=@userid", new { userid = UserId });
                        _logger.LogInformation("user beneficiary status updated ....");
                        return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = CheckIfuserHasBeneficiaries.Any() };
                       }      
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Success = false, Response = EnumResponse.SystemError };
            }
        }
       public async Task<LoginResponse> LoginUser(string ClientKey, LoginRequest Request,bool LoginWithFingerPrint = false)
        {
            try
            {
                
               // _logger.LogInformation("LoginRequest "+JsonConvert.SerializeObject(Request));
                if (string.IsNullOrEmpty(Request.Username))
                    return new LoginResponse() { Response = EnumResponse.UsernameOrPasswordRequired };

                using (IDbConnection con = _context.CreateConnection())
                {
                    string sess = _genServ.GetSession();
                    var usr = await _genServ.GetUserbyUsername(Request.Username, con);
                    if (usr == null)
                    {
                        await _genServ.InsertLogs(0, "", Request.Device, Request.GPS, $"Username - {EnumResponse.InvalidUsernameOrPassword} on {Request.Username}", con);
                        // check core banking for existing users
                        MigratedCustomer CoreBankingUsercheck = await CheckingCoreBankingForMigratingUsers(Request);
                        _logger.LogInformation("CoreBankingUsercheck "+ JsonConvert.SerializeObject(CoreBankingUsercheck));
                        if (CoreBankingUsercheck!=null)
                        {
                            // send otp here 
                            string otp  =  _genServ.GenerateOtp();
                            _logger.LogInformation("otp "+otp);
                            await _genServ.SendOtp3(OtpType.Registration,otp,CoreBankingUsercheck.PhoneNumber,_smsBLService,"Registration",CoreBankingUsercheck.Email);
                            await _genServ.SendOtp3(OtpType.Registration, otp, CoreBankingUsercheck.PhoneNumber2, _smsBLService, "Registration", CoreBankingUsercheck.Email);
                            // await _genServ.SendOtp3(OtpType.Registration, otp,"08147971091", _smsBLService, "Registration");
                            CoreBankingUsercheck.Otp = otp;
                            CoreBankingUsercheck.Session = sess;
                            CoreBankingUsercheck.dateTime = DateTime.Now;
                            // this will store the data temporarily in memory for 30s
                            if (_userCacheService.GetUserData(CoreBankingUsercheck.PhoneNumber,_cache)!=null)
                            {
                                _userCacheService.ClearUserData(CoreBankingUsercheck.PhoneNumber, _cache);
                            }
                            _userCacheService.StoreUserData(CoreBankingUsercheck,20, _cache);
                            if ((await _redisStorageService.GetCacheDataAsync<MigratedCustomer>(CoreBankingUsercheck.PhoneNumber)) != null)
                            {
                                await _redisStorageService.RemoveCustomerAsync(CoreBankingUsercheck.PhoneNumber);
                            }
                            await _redisStorageService.SetCacheDataAsync($"{CoreBankingUsercheck.PhoneNumber}",CoreBankingUsercheck);
                            return new LoginResponse()
                            {
                                SessionID=sess,
                                Email = CoreBankingUsercheck?.Email,
                                Bvn = CoreBankingUsercheck.Bvn,
                                IsMigratedCustomer=true,
                                Username = CoreBankingUsercheck?.username,
                                Firstname = CoreBankingUsercheck?.FirstName,
                                Lastname = CoreBankingUsercheck?.SurName,
                                Currentlogindevice = false,
                                PhoneNumber = CoreBankingUsercheck?.PhoneNumber,
                                Response = EnumResponse.Successful,
                                Success = true
                            };
                        }
                        return new LoginResponse() { Response = EnumResponse.InvalidUsernameOrPassword };                  
                    }
                   // _logger.LogInformation("usr status "+usr.Status);
                    if (usr.Status == 2)
                    {
                        await _genServ.InsertLogs(usr.Id, "", Request.Device, Request.GPS, $"{EnumResponse.InActiveProfile} on {Request.Username}", con);
                        return new LoginResponse() { Response = EnumResponse.InActiveProfile };
                    }
                    _logger.LogInformation($"LoginWithFingerPrint {!LoginWithFingerPrint}");
                    if (!LoginWithFingerPrint)
                    {
                        
                        string enterpass = _genServ.EncryptString(Request.Password);
                        var pssd = await _genServ.GetUserCredential(CredentialType.Password, usr.Id, con);
                       // _logger.LogInformation("enterpass "+ enterpass);
                       // _logger.LogInformation("pssd " + pssd);
                        _logger.LogInformation("enterpass != pssd " + (enterpass != pssd));
                        if (enterpass != pssd)
                        {
                            var retry = await con.QueryAsync<string>($"select retries from user_credentials where userid= {usr.Id} and status =1 and credentialtype = 1");
                            int retries = 0;
                            int.TryParse(retry.FirstOrDefault(), out retries);
                            _logger.LogInformation("retries "+ retries);
                            if (retries >= _settings.Retries)
                            {
                                await con.ExecuteAsync($"update users set status = 2 where id = {usr.Id}");
                                await _genServ.InsertLogs(usr.Id, "", Request.Device, Request.GPS, $"Password - {EnumResponse.InActiveProfile} on {Request.Username}", con);
                                return new LoginResponse() { Response = EnumResponse.InActiveProfile };
                            }

                            int retr = retries + 1;
                            await con.ExecuteAsync($"update user_credentials set retries = {retr} where userid= {usr.Id} and status =1 and credentialtype = 1");
                            await _genServ.InsertLogs(usr.Id, "", Request.Device, Request.GPS, $"Password - {EnumResponse.InvalidUsernameOrPassword} on {Request.Username}", con);
                            return new LoginResponse() { Response = EnumResponse.InvalidUsernameOrPassword };
                        }
                        await con.ExecuteAsync($"update user_credentials set retries = 0 where userid= {usr.Id} and status =1 and credentialtype = 1");
                    }

                    if (Request.ChannelId == 1 && _settings.CheckDevice == "y")
                    {

                        //var mobDev = await _genServ.GetActiveMobileDevice(usr.Id, con);
                        var mobDev = await _genServ.GetListOfActiveMobileDevice(usr.Id, con);
                        _logger.LogInformation("mobdev ....." + JsonConvert.SerializeObject(mobDev));
                        if (!mobDev.Any())
                        {
                            return new LoginResponse() { Response = EnumResponse.DeviceNoFound };
                        }
                        var itemdeviceindb = mobDev.Find(e => e.Device.ToLower() == Request.Device);
                        _logger.LogInformation($"{Request.Username} Device token " + Request.DeviceToken);
                        // var task1= Task.Run(async () => {
                        var devicetoken = await con.QueryAsync<string>("select DeviceToken from mobiledevice where DeviceToken = @device", new { device = Request.DeviceToken });
                        var token = devicetoken.ToList();
                        _logger.LogInformation(token + " devicetoken " + devicetoken.Any());
                        if (devicetoken.Any())
                        {
                            _logger.LogInformation($"{Request.Username} Device tokens " + JsonConvert.SerializeObject(token));
                            await con.ExecuteAsync("update mobiledevice set DeviceToken = null where DeviceToken in @devtoken", new { devtoken = token });
                            _logger.LogInformation($"updating existing token .....for {Request.Username}");
                            await con.ExecuteAsync($"update mobiledevice set DeviceToken=@devtoken where UserId=@id and Device=@Device", new { Device=Request.Device, devtoken = Request.DeviceToken, id = usr.Id });
                        }
                        else
                        {
                            _logger.LogInformation($"updating token .....for {Request.Username}");
                            await con.ExecuteAsync("update mobiledevice set DeviceToken=@devtoken where UserId=@id and Device=@Dev", new { Dev = Request.Device, devtoken = Request.DeviceToken, id = usr.Id });
                        }
                        _logger.LogInformation($"task executed updating token .....for {Request.Username}");
                        if (string.IsNullOrEmpty(Request.Device) || mobDev == null || !mobDev.Any())
                        {
                            await _genServ.InsertLogs(usr.Id, "", Request.Device, Request.GPS, $"Device - {EnumResponse.DeviceNotRegistered} on {Request.Device}", con);
                            return new LoginResponse() { Response = EnumResponse.DeviceNotRegistered };
                        }
                    }
                    await _genServ.SetUserSession(usr.Id, sess, Request.ChannelId, con);
                    var CustomerDevice2 = await con.QueryAsync<CustomerDevices>("select * from customer_devices where loginstatus=1 and Username = @Username and trackdevice='present' and device=@device", new { username = Request.Username, device = Request.Device }); // logged in on any device at all    
                    bool logindevice = true;
                    if (!CustomerDevice2.Any())
                    {
                        _logger.LogInformation("CustomerDevice2.Any() " + CustomerDevice2.Any());
                        logindevice = false;
                    }
                    if (CustomerDevice2.Any()) // what if it is true.check if a different user has already logged in before and set logindevice to false 
                    {
                        _logger.LogInformation("has logged in on a device ....");                                                                                                                                                                    // is any user presently logged-in in this present device
                        var DifferentUserSameDevice = await con.QueryAsync<CustomerDevices>($"select * from customer_devices where loginstatus=1 and trackdevice='present' and device=@device and username!=@Username", new { device = Request.Device,Username = Request.Username });
                        if (DifferentUserSameDevice.Any()) { 
                        var deviceList = DifferentUserSameDevice.Select(d => d.Device).ToList();
                        _logger.LogInformation("DifferentUserSameDevice " + DifferentUserSameDevice.Any());
                        if (deviceList.Any())
                        {
                         logindevice = false;
                        }
                          }
                    }
                    await _genServ.InsertLogs(usr.Id, sess, Request.Device, Request.GPS, $"Login Successful", con);
                    //sending a mail
                    string DeviceName = (await con.QueryAsync<string>($"select devicename from mobiledevice where device='{Request.Device}'")).FirstOrDefault();
                    _logger.LogInformation("DeviceName " + DeviceName);
                    SendMailObject sendMailObject = new SendMailObject();
                    CustomerDataNotFromBvn customerDataNotFromBvn = (await con.QueryAsync<CustomerDataNotFromBvn>(
                                                                     "SELECT PhoneNumber, Email FROM customerdatanotfrombvn WHERE username = (SELECT username FROM users WHERE id = @id)",
                                                                     new { id = usr.Id }
                                                                    )).FirstOrDefault();
                    _logger.LogInformation("proceeding to check for beneficiaries "+usr.isbeneficiarymigrated+" and migrated"+usr.migrateduser);
                    Task.Run(async () =>
                    {
                        if (!usr.isbeneficiarymigrated&&usr.migrateduser) {
                        await MigrateCustomerBeneficiaresToPrime2(usr.Id,usr.migrateduser,usr.isbeneficiarymigrated, Request.Username);     
                        }
                    });

                    if (logindevice)
                    {
                        /*
                        sendMailObject.Email = customerDataNotFromBvn?.Email;
                        sendMailObject.Firstname = usr.Firstname + " " + usr.LastName;
                        sendMailObject.Html = $@"<p>Hello Dear {Request.Username}</p>,
                                      <p>You logged into your TrustBanc Mobile Platform from a device {DeviceName} at {DateTime.Now}.</p>
                                     <p>If this login did not originate from you, please let us know by sending an email to support@trustbancgroup.com</p>
                                     <p>Alternatively, you can call 07004446147 immediately.Thanks.</p>
                                     <p>Thank you for choosing TrustBanc J6 MfB.</p>";
                        sendMailObject.Subject = "TrustBanc Bank Mobile App Login Notification";
                        Thread thread = new Thread(() =>
                        {
                            //Console.WriteLine("sending mail in thread");
                            _genServ.LogRequestResponse("enter in thread to send email ", $" ", "");
                            _genServ.SendMail(sendMailObject);
                            Console.WriteLine("mail sent ....");
                        });
                        thread.Start();
                        */
                    }
                    if (customerDataNotFromBvn != null)
                    {
                        _logger.LogInformation("Customer Data Retrieved: " + customerDataNotFromBvn);

                        string updateSql = "UPDATE customerdatanotfrombvn SET userId = @UserId, username = @Username " +
                                           "WHERE regid = (SELECT id FROM registration WHERE username = @Username limit 1)";

                        if (string.IsNullOrEmpty(customerDataNotFromBvn.userid) || string.IsNullOrEmpty(customerDataNotFromBvn.username))
                        {
                            await con.ExecuteAsync(updateSql, new { UserId = usr.Id, Username = usr.Username });
                        }
                    }
                    return new LoginResponse()
                    {
                        SessionID = sess,
                        Email = customerDataNotFromBvn != null ? customerDataNotFromBvn?.Email : "",
                        Bvn = usr.Bvn,
                        Username = usr.Username,
                        Address = usr.Address,
                        IsMigratedCustomer = false,
                        ProfilePic = Path.GetFileName(usr.ProfilePic),
                        Firstname = usr.Firstname,
                        Lastname = usr.LastName,
                        Currentlogindevice = logindevice,
                        PhoneNumber = customerDataNotFromBvn != null ? customerDataNotFromBvn?.PhoneNumber : usr?.PhoneNumber,
                        Response = EnumResponse.Successful,
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

        private async Task<MigratedCustomer> CheckingCoreBankingForMigratingUsers(LoginRequest request)
        {
            _logger.LogInformation($"finedge url {_settings.FinedgeUrl}");
            string url = $"{_settings.FinedgeUrl}api/Customer/GetMobileCustomerByUserNameForMigrationToPrime2/{request.Username}";
            _logger.LogInformation("url "+url);
            string resp = await _genServ.CallServiceAsyncToString(Method.GET,url,null,true);
            GenericResponse2 genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(resp);
            _logger.LogInformation("genericResponse2.data " + JsonConvert.SerializeObject(genericResponse2?.data));
            if (resp == null || resp == "")
            {
                return null;
            }
            MigratedCustomer migratedCustomer = JsonConvert.DeserializeObject<MigratedCustomer>(JsonConvert.SerializeObject(genericResponse2.data));
            migratedCustomer.Device = request.Device;
           // migratedCustomer.DeviceName=request.De
            return migratedCustomer;
        }

        public async Task<GenericResponse> ResetPassword(string ClientKey, ResetPassword Request)
        {
            try
            {
                if (string.IsNullOrEmpty(Request.AccountNumber) || string.IsNullOrEmpty(Request.NewPassword))
                    return new GenericResponse() { Response = EnumResponse.InvalidDetails };

                //Check CBA using Account No
                var getCust = await _genServ.GetCustomerbyAccountNo(Request.AccountNumber);
                if (!getCust.success)
                    return new GenericResponse() { Response = EnumResponse.UserNotFound };

                using (IDbConnection con = _context.CreateConnection())
                {
                    var getuser = await _genServ.GetUserbyCustomerId(getCust.result.customerID, con);
                    if (getuser == null)
                        return new GenericResponse() { Response = EnumResponse.UserNotFound };

                    var validateSession = await _genServ.ValidateSession(getuser.Username, Request.Session, Request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };

                    var checkpassword = _genServ.CheckPasswordCondition(Request.NewPassword);
                    if (!checkpassword)
                        return new GenericResponse() { Response = EnumResponse.PasswordConditionNotMet };
                    await _genServ.SetUserCredential(CredentialType.Password, getuser.Id, Request.NewPassword, con, true);
                    CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)getuser.Id);
                    Thread thread = new Thread(() =>
                    {
                        // var Cdetails = await _genServ.GetCustomerbyAccountNo(listOfData.ElementAtOrDefault(0).SourceAccount);
                        SendMailObject sendMailObject = new SendMailObject();
                        // sendMailObject.BvnEmail = Cdetails.Result.result.email;
                        sendMailObject.Email = customerDataNotFromBvn?.Email;
                        sendMailObject.Subject = "TrustBanc Mobile App Password Reset";
                        sendMailObject.Html = $@"<p>Dear {getCust.result.firstname}</p>
                                             <p>Your new Password is set successfully</p>
                                             <p>For any feedback or enquiries, please call our Contact Center on 07004446147 or send an email to support@trustbancgroup.com.</p>                                             
                                             <p>Thank you for Choosing TrustBanc J6 MfB</p>";
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

        public async Task<RegistrationResponse> UnlockDevice(string ClientKey, UnlockDevice Request)
        {
            try
            {
                if (Request.ChannelId != 1)
                    return new RegistrationResponse() { Response = EnumResponse.InvalidDetails };

                using (IDbConnection con = _context.CreateConnection())
                {
                    var usr = await _genServ.GetUserbyUsername(Request.Username, con);
                    if (usr == null)
                        return new RegistrationResponse() { Response = EnumResponse.InvalidUsernameOrPassword };

                    string sess = _genServ.GetSession();
                    string otp = _genServ.GenerateOtp();

                    var resp = await _genServ.GetCustomer2(usr.CustomerId);
                    if (!resp.Success)
                        return new RegistrationResponse() { Response = EnumResponse.InvalidUsernameOrPassword };

                    await _genServ.InsertOtp(OtpType.UnlockDevice, usr.Id, sess, otp, con);
                    await _genServ.SendOtp(OtpType.UnlockDevice, otp, resp.Customer.Mobile, resp.Customer.Email);
                    await _genServ.InsertLogs(usr.Id, sess, Request.DeviceID, Request.GPS, $"Request - Device Unlock", con);
                    await con.ExecuteAsync($"update mobiledevice set status = 2 where userid = {usr.Id}");

                    string sql = $"insert into mobiledevice (userid, device, status, devicename, createdon) values({usr.Id},@dev,3,@devnam,sysdate())";
                    await con.ExecuteAsync(sql, new { dev = Request.DeviceID, devnam = Request.DeviceName });

                    return new RegistrationResponse() { Email = resp.Customer.Email, PhoneNumber = resp.Customer.Mobile, Response = EnumResponse.Successful, Success = true, SessionID = sess };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new RegistrationResponse() { Success = false, Response = EnumResponse.SystemError };
            }
        }

        public async Task<RegistrationResponse> UnlockProfile(string ClientKey, ResetObj Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var usr = await _genServ.GetUserbyUsername(Request.Username, con);
                    if (usr == null)
                        return new RegistrationResponse() { Response = EnumResponse.InvalidUsernameOrPassword };

                    if (Request.ChannelId == 1)
                    {
                        var mobDev = await _genServ.GetActiveMobileDevice(usr.Id, con);
                        if (mobDev == null || Request.DeviceID != mobDev.Device)
                        {
                            await _genServ.InsertLogs(usr.Id, "", Request.DeviceID, Request.GPS, $"Device - {EnumResponse.DeviceNotRegistered} on {Request.DeviceID}", con);
                            return new RegistrationResponse() { Response = EnumResponse.DeviceNotRegistered };
                        }
                    }

                    string sess = _genServ.GetSession();
                    string otp = _genServ.GenerateOtp();
                    var resp = await _genServ.GetCustomer2(usr.CustomerId);
                    if (!resp.Success)
                        return new RegistrationResponse() { Response = EnumResponse.InvalidUsernameOrPassword };

                    await _genServ.InsertOtp(OtpType.UnlockProfile, usr.Id, sess, otp, con);
                    await _genServ.SendOtp(OtpType.UnlockProfile, otp, resp.Customer.Mobile, resp.Customer.Email);
                    await _genServ.InsertLogs(usr.Id, sess, Request.DeviceID, Request.GPS, $"Request - Profile Unlock", con);
                    return new RegistrationResponse() { Email = resp.Customer.Email, PhoneNumber = resp.Customer.Mobile, Response = EnumResponse.Successful, Success = true, SessionID = sess };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new RegistrationResponse() { Success = false, Response = EnumResponse.SystemError };
            }
        }

        public async Task<RegistrationResponse> ForgotPassword(string ClientKey, ResetObj Request)
        {
            try
            {

                using (IDbConnection con = _context.CreateConnection())
                {
                    var usr = await _genServ.GetUserbyUsername(Request.Username, con);
                    if (usr == null)
                        return new RegistrationResponse() { Response = EnumResponse.InvalidUsernameOrPassword };

                    if (Request.ChannelId == 1)
                    {
                        var mobDev = await _genServ.GetActiveMobileDevice(usr.Id, con);
                        if (mobDev == null || Request.DeviceID != mobDev.Device)
                        {
                            await _genServ.InsertLogs(usr.Id, "", Request.DeviceID, Request.GPS, $"Device - {EnumResponse.DeviceNotRegistered} on {Request.DeviceID}", con);
                            return new RegistrationResponse() { Response = EnumResponse.DeviceNotRegistered };
                        }
                    }

                    string sess = _genServ.GetSession();
                    string otp = _genServ.GenerateOtp();
                    var resp = await _genServ.GetCustomer2(usr.CustomerId);
                    if (!resp.Success)
                        return new RegistrationResponse() { Response = EnumResponse.InvalidUsernameOrPassword };
                    await _genServ.InsertOtp(OtpType.PasswordReset, usr.Id, sess, otp, con);
                    await _genServ.SendOtp(OtpType.PasswordReset, otp, resp.Customer.Mobile, resp.Customer.Email);
                    await _genServ.InsertLogs(usr.Id, sess, Request.DeviceID, Request.GPS, $"Request - Password Reset", con);

                    return new RegistrationResponse() { Email = resp.Customer.Email, PhoneNumber = resp.Customer.Mobile, Response = EnumResponse.Successful, Success = true, SessionID = sess };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new RegistrationResponse() { Success = false, Response = EnumResponse.SystemError };
            }
        }

        public async Task<RetrivalResponse> StartRetrival(string ClientKey, ResetObj2 Request)
        {
            try
            {
                if (string.IsNullOrEmpty(Request.AccountNo) || string.IsNullOrEmpty(Request.TransactionPin))
                    return new RetrivalResponse() { Response = EnumResponse.InvalidDetails };

                //Check CBA using Account No
                var getCust = await _genServ.GetCustomerbyAccountNo(Request.AccountNo);
                if (!getCust.success)
                    return new RetrivalResponse() { Response = EnumResponse.UserNotFound };

                using (IDbConnection con = _context.CreateConnection())
                {
                    var getuser = await _genServ.GetUserbyCustomerId(getCust.result.customerID, con);
                    if (getuser == null)
                        return new RetrivalResponse() { Response = EnumResponse.UserNotFound };

                    string tpin = _genServ.EncryptString(Request.TransactionPin);
                    var getPin = await _genServ.GetUserCredential(CredentialType.TransactionPin, getuser.Id, con);
                    if (tpin != getPin)
                        return new RetrivalResponse() { Response = EnumResponse.WrongDetails };

                    string sess = _genServ.GetSession();
                    string otp = _genServ.GenerateOtp();
                    await _genServ.SetUserSession(getuser.Id, sess, Request.ChannelId, con);
                    await _genServ.InsertLogs(getuser.Id, sess, Request.Device, "", $"Retrival Process Start - " + (OtpType)Request.RetrivalType, con);
                    /*
                    if ((OtpType)Request.RetrivalType == OtpType.UnlockDevice)
                    {
                        await con.ExecuteAsync($"update mobiledevice set status = 2 where userid = {getuser.Id} and status = 1");
                        await _genServ.SetMobileDevice(getuser.Id, Request.Device, Request.DeviceName, 3, con);
                    }
                    */
                    _logger.LogInformation("getuser " + JsonConvert.SerializeObject(getuser));
                    CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)getuser.Id);
                    if ((OtpType)Request.RetrivalType == OtpType.PasswordReset)
                    {
                        _logger.LogInformation("OtpType.RetrivalType " + OtpType.PasswordReset);
                        await _genServ.InsertOtp((OtpType)Request.RetrivalType, getuser.Id, sess, otp, con);
                        await _genServ.SendOtp3((OtpType)Request.RetrivalType, otp, customerDataNotFromBvn?.PhoneNumber, _smsBLService, "Retrieval", customerDataNotFromBvn?.Email);
                    }
                    else if ((OtpType)Request.RetrivalType == OtpType.RetrieveUsername)
                    {
                        _logger.LogInformation("OtpType.RetrieveUsername " + OtpType.RetrieveUsername);
                        await _genServ.InsertOtp((OtpType)Request.RetrivalType, getuser.Id, sess, otp, con);
                        await _genServ.SendOtp3((OtpType)Request.RetrivalType, otp, customerDataNotFromBvn.PhoneNumber, _smsBLService, "Retrieval", customerDataNotFromBvn.Email);
                    }
                    return new RetrivalResponse()
                    {
                        Success = true,
                        PhoneNumber = _genServ.MaskPhone(customerDataNotFromBvn.PhoneNumber),
                        Response = EnumResponse.Successful,
                        SessionID = sess
                    };
                    //PhoneNumber = _genServ.MaskPhone(getuser.PhoneNumber),
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new RetrivalResponse() { Success = false, Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> ValidateOtp(string ClientKey, ValidateOtpRetrival Request)
        {
            try
            {
                if (string.IsNullOrEmpty(Request.AccountNumber))
                    return new GenericResponse() { Response = EnumResponse.InvalidDetails };

                //Check CBA using Account No
                var getCust = await _genServ.GetCustomerbyAccountNo(Request.AccountNumber);
                if (!getCust.success)
                    return new GenericResponse() { Response = EnumResponse.UserNotFound };

                using (IDbConnection con = _context.CreateConnection())
                {
                    var getuser = await _genServ.GetUserbyCustomerId(getCust.result.customerID, con);
                    if (getuser == null)
                        return new GenericResponse() { Response = EnumResponse.UserNotFound };

                    var validateSession = await _genServ.ValidateSession(getuser.Username, Request.Session, Request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    //var resp = await _genServ.ValidateSessionOtp(OtpType.UnlockDevice, Request.Session, con);
                    // Console.WriteLine($"resp {resp}");
                    var resp = await _genServ.ValidateSessionOtp((OtpType)Request.RetrivalType, Request.Session, con);
                    // var resp = await _genServ.ValidateSessionOtp(Request.RetrivalType, Request.Session, con);
                    if (resp == null || resp.OTP != Request.Otp)
                        return new ValidateOtpResponse() { Response = EnumResponse.InvalidOtp };
                    CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)getuser.Id);
                    if ((OtpType)Request.RetrivalType == OtpType.RetrieveUsername)
                    {
                        string msg = _settings.RetriveUsernameText;
                        msg = msg.Replace("{Username}", getuser.Username);
                        var request = new SendSmsRequest()
                        {
                            ClientKey = "",
                            Message = msg,
                            PhoneNumber = getuser.PhoneNumber,
                            SmsReference = DateTime.Now.ToString("ddMMyyyyHHmmss") + "_" + new Random().Next(11111, 99999)
                        };
                        // await _genServ.SendSMS(request);
                        Task.Run(async () =>
                        {
                            customerDataNotFromBvn.PhoneNumber = !string.IsNullOrEmpty(customerDataNotFromBvn.PhoneNumber) ? customerDataNotFromBvn.PhoneNumber : getuser.PhoneNumber;
                            var msg = $@"Dear {getuser.Firstname},your username is {getuser.Username}.Thank you for banking with us.";
                            customerDataNotFromBvn.PhoneNumber = CustomerServiceNotFromBvnService.ReplaceFirstDigit(customerDataNotFromBvn.PhoneNumber,"234");
                            GenericResponse response = await _smsBLService.SendSmsNotificationToCustomer("Otp",customerDataNotFromBvn.PhoneNumber, $@"{msg}", "AccountNumber Creation", _settings.SmsUrl);
                            _logger.LogInformation("response " + response.ResponseMessage + " message " + response.Message);
                        });
                        SendMailObject sendMailObject = new SendMailObject();
                        //sendMailObject.BvnEmail = getuser.BvnEmail;
                        sendMailObject.Email = customerDataNotFromBvn?.Email;
                        sendMailObject.Subject = "TrustBanc Mobile App Username Retrieval";
                        sendMailObject.Html = $@"
                            <p>Dear {getuser.Firstname.ToUpper()} {getuser.LastName.ToUpper()},
                             </p>
                             <p>Your Username is {getuser.Username}.</p>
                            <p>Thank your for Banking with us.</p>
                                            ";
                        Thread thread = new Thread(() =>
                        {
                            _logger.LogInformation("mail sending");
                            _genServ.SendMail(sendMailObject);
                            _logger.LogInformation("mail sent");
                        });
                        thread.Start();
                    }

                    if ((OtpType)Request.RetrivalType == OtpType.UnlockDevice)
                        await con.ExecuteAsync($"update mobiledevice set status = 1 where userid = {getuser.Id} and status = 3");

                    if ((OtpType)Request.RetrivalType == OtpType.UnlockProfile)
                        await con.ExecuteAsync($"update users set status = 1 where id = {getuser.Id}");
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

        public async Task<GenericResponse> CheckOtp(string ClientKey, ValidateOtpRetrival Request)
        {
            try
            {
                if (string.IsNullOrEmpty(Request.AccountNumber))
                    return new GenericResponse() { Response = EnumResponse.InvalidDetails };

                //Check CBA using Account No
                var getCust = await _genServ.GetCustomerbyAccountNo(Request.AccountNumber);
                if (!getCust.success)
                    return new GenericResponse() { Response = EnumResponse.UserNotFound };

                using (IDbConnection con = _context.CreateConnection())
                {
                    var getuser = await _genServ.GetUserbyCustomerId(getCust.result.customerID, con);
                    if (getuser == null)
                        return new GenericResponse() { Response = EnumResponse.UserNotFound };

                    var validateSession = await _genServ.ValidateSession(getuser.Username, Request.Session, Request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };

                    var resp = await _genServ.ValidateSessionOtp((OtpType)Request.RetrivalType, Request.Session, con);
                    if (resp == null || resp.OTP != Request.Otp)
                        return new ValidateOtpResponse() { Response = EnumResponse.InvalidOtp };

                    if ((OtpType)Request.RetrivalType == OtpType.RetrieveUsername)
                    {
                        string msg = _settings.RetriveUsernameText;
                        msg = msg.Replace("{Username}", getuser.Username);
                        var request = new SendSmsRequest()
                        {
                            ClientKey = "",
                            Message = msg,
                            PhoneNumber = getuser.PhoneNumber,
                            SmsReference = DateTime.Now.ToString("ddMMyyyyHHmmss") + "_" + new Random().Next(11111, 99999)
                        };
                        await _genServ.SendSMS(request);
                    }

                    if ((OtpType)Request.RetrivalType == OtpType.UnlockDevice)
                        await con.ExecuteAsync($"update mobiledevice set status = 1 where userid = {getuser.Id} and status = 3");

                    if ((OtpType)Request.RetrivalType == OtpType.UnlockProfile)
                        await con.ExecuteAsync($"update users set status = 1 where id = {getuser.Id}");

                    await con.ExecuteAsync($"update otp_session set status = 0 where id = {resp.ID}");
                    return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Success = false, Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse2> UploadProfilePicture(string clientKey, Picture Request, IFormFile file)
        {
           // string path = _settings.PicFileUploadPath + "/" + file.FileName;
            string uploadPath = _settings.PicFileUploadPath;
            _logger.LogInformation("_settings.PicFileUploadPath " + _settings.PicFileUploadPath);
            string fileName = Path.GetRandomFileName() + Path.GetExtension(file.FileName);
            string path = Path.Combine(uploadPath, fileName);
            _logger.LogInformation("path " + path + " and _settings.PicFileUploadPath " + _settings.PicFileUploadPath, null);
            _logger.LogInformation("passport name " + Path.GetFileName(path), null);
          //  Console.WriteLine("path ... " + path + " " + " " + file.FileName.EndsWith(".jpg"));
            if (file.FileName.EndsWith(".jpeg") || file.FileName.EndsWith(".png") || file.FileName.EndsWith(".jpg"))
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Request.Username, Request.Session, Request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse2() { Response = EnumResponse.InvalidSession };
                    using (var stream = new FileStream(path, FileMode.Create, access: FileAccess.ReadWrite))
                    {
                        await con.ExecuteAsync($"update registration set imagepath = @path where Username = @username", new { username = Request.Username, path = path });
                        await con.ExecuteAsync($"update users set profilepic = @path where Username = @username",new { username=Request.Username,path=path});
                        await file.CopyToAsync(stream);
                        await _fileService.SaveFileAsyncForProfilePicture(file); // save to external folder
                        _logger.LogInformation("inserted into users table successfully ....", null);
                    }

                }
                return new GenericResponse2() { Response = EnumResponse.Successful, Success = true,data= Path.GetFileName(path) };
            }
            else
            {
                return new GenericResponse2() { Response = EnumResponse.InvalidFileformat };
            }
        }

        public int UpdateDeviceLoginStatus(string ClientKey, string Username)
        {
            throw new NotImplementedException();
        }

        public async Task<GenericResponse> ValidateOtherDeviceForOnBoarding(string ClientKey, PhoneAndAccount Request)
        {
            try
            {
                if (!string.IsNullOrEmpty(Request.AccountNumber))
                {
                    bool AccountNumbercheck = Request.AccountNumber.All(char.IsDigit);
                    bool AccountNUmbercount = Request.AccountNumber.Length == 10;
                    if (!AccountNUmbercount || !AccountNumbercheck)
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidAccount };
                    }
                    // mobilenumber if the iucormeteraccount number is dbs
                    using (IDbConnection con = _context.CreateConnection())
                    {
                        FinedgeSearchBvn finedgeSearchBvn = await _genServ.GetCustomerbyAccountNo(Request.AccountNumber);
                        var mobilenumber = finedgeSearchBvn.success ? finedgeSearchBvn.result.mobile.ToString() : null;
                        Console.WriteLine("mobilenumber.FirstOrDefault() " + mobilenumber.FirstOrDefault());
                        if (!string.IsNullOrEmpty(mobilenumber))
                        {
                            // send otp if valid 
                            // insert into otp_session 
                            // int status = 1;
                            string otp = _genServ.GenerateOtp();
                            string session = _genServ.GetSession();
                            var usr = (await con.QueryAsync<Users>("select * from users where bvn = @bvn", new { bvn = finedgeSearchBvn.result.bvn })).FirstOrDefault();
                            await _genServ.InsertLogs(usr.Id, session, Request.Device, "", $"Request - Device Unlock", con);
                            var presentdevice = (await con.QueryAsync<string>($"select distinct device from mobiledevice where device=@device and userid=@userid",new { device=Request.Device,userid=usr.Id})).ToList();
                            if (!presentdevice.Any())
                            {
                                string sql2 = $"insert into mobiledevice (userid, device, status, devicename, createdon) values({usr.Id},@dev,1,@devnam,sysdate())";
                                await con.ExecuteAsync(sql2, new { dev = Request.Device, devnam = Request.DeviceName });
                            }
                            string sql = "insert into otp_session(session, status, otp_type, otp, createdon, objid) values (@session,1,@otp_type,@otp,sysdate(),@objid)";
                            await con.ExecuteAsync(sql, new { session = session, otp_type = (int)OtpType.UnlockDevice, otp = otp, ObjId = usr.Id });
                            var customerdevice = (await con.QueryAsync<string>($"select device from customer_devices where device='{Request.Device}' and username='{usr.Username}'"));
                            if (!customerdevice.Any())
                            {
                                string sql2 = $"insert into customer_devices(device,loginstatus,trackdevice,username) values('{Request.Device}',0,'recent','{usr.Username}')";
                                await con.ExecuteAsync(sql2);
                            }
                            CustomerDataNotFromBvn customerDataNotFromBvn =await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)usr.Id);
                            //await _genServ.SendOtp(OtpType.UnlockDevice, otp, mobilenumber);
                            _logger.LogInformation("sending otp for device validation for other devices");
                            await _genServ.SendOtp3(OtpType.UnlockDevice,otp,customerDataNotFromBvn?.PhoneNumber,_smsBLService,"Device Validatation",customerDataNotFromBvn?.Email);
                            return new DeviceOnBoardReponse() { Session = session, Response = EnumResponse.Successful, Success = true };
                        }
                        else
                        {
                            return new GenericResponse() { Response = EnumResponse.InvalidAccount };
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(Request.PhoneNumber))
                {
                    bool allDigits = Request.PhoneNumber.All(char.IsDigit);
                    if (!allDigits)
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidPhoneNumber };
                    }
                    // mobilenumber if the phonenumber is in dbs
                    using (IDbConnection con = _context.CreateConnection())
                    {
                        string query = "select PhoneNumber from customerdatanotfrombvn where PhoneNumber = @PhoneNumber";
                        var mobilenumber = await con.QueryAsync<string>(query, new { PhoneNumber = Request.PhoneNumber });
                        if (!string.IsNullOrEmpty(mobilenumber.FirstOrDefault()))
                        {
                            string otp = _genServ.GenerateOtp();
                            // insert into otp_session 
                            string session = _genServ.GetSession();
                            int status = 1;
                            //  var usr = (await con.QueryAsync<Users>("select * from users where phonenumber = @phonenumber", new { phonenumber = Request.BvnPhoneNumber })).FirstOrDefault();
                            var usr = await _genServ.GetUserbyUsername(Request.Username, con);
                            if (usr == null)
                            {
                                return new GenericResponse() { Response = EnumResponse.InvalidPhoneNumber };
                            }
                            await _genServ.InsertLogs(usr.Id, session, Request.Device, "", $"Request - Device Unlock", con);
                            //2 stands probably means the user has more than  one device
                            // awaitcon.ExecuteAsync($"update mobiledevice set status = 2 where userid = {usr.Id}");
                            var presentdevice = (await con.QueryAsync<string>($"select device from mobiledevice where UserId=@userid",new { userid=usr.Id})).ToList();
                            if (!presentdevice.Any())
                            {
                                string sql2 = $"insert into mobiledevice (userid, device, status, devicename, createdon) values(@userid,@dev,1,@devnam,sysdate())";
                                await con.ExecuteAsync(sql2, new {userid=usr.Id, dev = Request.Device, devnam = Request.DeviceName });
                            }
                            string sql = "insert into otp_session(session, status, otp_type, otp, createdon, objid) values (@session,1,@otp_type,@otp,sysdate(),@objid)";
                            await con.ExecuteAsync(sql, new { session = session, otp_type = (int)OtpType.UnlockDevice, otp = otp, ObjId = usr.Id });
                            // await _genServ.SendOtp(OtpType.UnlockDevice, otp, Request.PhoneNumber);
                            CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)usr.Id);
                            //await _genServ.SendOtp(OtpType.UnlockDevice, otp, mobilenumber);
                            await _genServ.SendOtp3(OtpType.UnlockDevice, otp, customerDataNotFromBvn.PhoneNumber, _smsBLService, "Device Validatation",customerDataNotFromBvn.Email);
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

        //pls mobilenumber .Is this accurate?
        public async Task<GenericResponse> ValidateOtpToOnBoardOtherDevices(string ClientKey, DeviceOtpValidator Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    /*
                    var otpsession = (await con.QueryAsync<OtpSession>($"select * from otp_session where otp = @otp and session='{Request.Session}'", new { otp = Request.Otp })).FirstOrDefault();
                    if (otpsession == null)
                    {
                        return new GenericResponse() { Response = EnumResponse.OtpNotValidated };
                    }
                    */
                    var resp = await _genServ.ValidateSessionOtp(OtpType.UnlockDevice, Request.Session, con);
                    Console.WriteLine($"resp {resp}");
                    if (resp == null || resp.OTP != Request.Otp)
                        return new GenericResponse() { Response = EnumResponse.InvalidOtp };
                    // then insert into customer_device to onboard
                    var usr = (await con.QueryAsync<Users>("select * from users where id = @id", new { id = resp.ObjId })).FirstOrDefault();
                    Console.WriteLine("usr ...." + JsonConvert.SerializeObject(usr));
                    var devicecheck = (await con.QueryAsync<CustomerDevices>($"select * from customer_devices where Username='{usr.Username}' and loginstatus=1 and trackdevice='present'"));
                    if (devicecheck.Any())
                    {
                        _logger.LogInformation("has logged in on a device ....");
                        var deviceCheckList = devicecheck.Select(d => d.Device).ToList();
                        await con.ExecuteAsync("update customer_devices set trackdevice='recent',loginstatus=0 where device in @deviceCheckList", new { deviceCheckList }); // unlog user on other device                                                                                                                                                                      // is any user presently logged-in in this present device
                        var DifferentUserSameDevice = await con.QueryAsync<CustomerDevices>($"select * from customer_devices where loginstatus=1 and trackdevice='present' and device=@device and username!='{usr.Username}'", new { device = Request.Device });
                        if (DifferentUserSameDevice.Any())
                        {
                            var deviceList = DifferentUserSameDevice.Select(d => d.Device).ToList();
                            _logger.LogInformation("DifferentUserSameDevice " + DifferentUserSameDevice.Any());
                            await con.ExecuteAsync("update customer_devices set trackdevice='recent',loginstatus=0 where device in @deviceList", new { deviceList });
                        }
                        // this device becomes the present device for the user
                        await con.ExecuteAsync("update customer_devices set trackdevice='present',loginstatus=1 where Username=@Username and device=@device", new { username = usr.Username, device = Request.Device });
                    }
                    else
                    {  
                        //has anybody logged in before.
                        var DifferentUserSameDevice = await con.QueryAsync<CustomerDevices>($"select * from customer_devices where loginstatus=1 and trackdevice='present' and device=@device and username!='{usr.Username}'", new { device = Request.Device });
                        if (DifferentUserSameDevice.Any())
                        {
                            var deviceList = DifferentUserSameDevice.Select(d => d.Device).ToList();
                            _logger.LogInformation("DifferentUserSameDevice " + DifferentUserSameDevice.Any());
                            await con.ExecuteAsync("update customer_devices set trackdevice='recent',loginstatus=0 where device in @deviceList", new { deviceList });
                        }
                        // this device becomes the present device if he has never logged in before 
                        _logger.LogInformation("never logged in.just setting it to login");
                        await con.ExecuteAsync("update customer_devices set trackdevice='present',loginstatus=1 where Username=@Username and device=@device", new { username = usr.Username, device = Request.Device });
                    }
                  //  await con.ExecuteAsync($"update otp_session set status = 0 where id = {resp.ID}");
                    Console.WriteLine($"id of user {resp.ObjId}");
                    SendMailObject sendMailObject = new SendMailObject();
                    //sendMailObject.BvnEmail = usr.BvnEmail;
                    CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)usr.Id);
                    sendMailObject.Email = customerDataNotFromBvn?.Email;
                    sendMailObject.Firstname = usr.Firstname + " " + usr.LastName;
                    var DeviceName = (await con.QueryAsync<string>($"select devicename from  mobiledevice where device='{Request.Device}'")).FirstOrDefault();
                    _logger.LogInformation("DeviceName " + DeviceName);
                    sendMailObject.Html = $"<p>Hello Dear {sendMailObject.Firstname},You have just onboarded the device {DeviceName} for Mobile App Banking today at {DateTime.Now} </p>" +
                        $"<p>If this did not originate from you, please let us know by sending an email to support@trustbancgroup.com</p>" +
                        $"<p>Alternatively, you can call 07004446147 immediately.</p>" +
                        $"<p>Thank you for choosing TrustBanc J6 MfB. </p>";
                    sendMailObject.Subject = "TrustBanc Bank Mobile App Device OnBoarding";
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

        public async Task<GenericResponse> ValidatePIn(string clientKey, int ChannelId, string Username, string userPin, string session)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Username, session, ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    string encryptedPin = _genServ.EncryptString(userPin);
                    var pin = (await con.QueryAsync<string>("select credential from user_credentials where credentialtype=2 and status=1 and userid=(select id from users where Username=@Username)", new { username = Username })).FirstOrDefault();
                    _logger.LogInformation("encryptedPin " + encryptedPin + " Pin " + pin);
                    var passwordCheck = encryptedPin == pin;
                    // Console.WriteLine("comparing passwords {}",passwordCheck);
                    if (encryptedPin == pin)
                    {
                        return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
                    }
                    return new GenericResponse() { Response = EnumResponse.InvalidTransactionPin };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
            throw new NotImplementedException();
        }

        public async Task<GenericResponse> SetEmploymentInfo(string clientKey, int channelId, string username, EmploymentInfo employmentInfo)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(employmentInfo.Username, employmentInfo.Session, employmentInfo.ChannelId, con);
                    if (!validateSession)
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    }
                    var myuserid = (await con.QueryAsync<string>("select userid from user_credentials where credentialtype=2 and status=1 and userid=(select id from users where Username=@Username)", new { username = employmentInfo.Username })).FirstOrDefault();
                    if (string.IsNullOrEmpty(myuserid))
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidUsernameOrPassword };
                    }
                    var myuserid2 = (await con.QueryAsync<string>("select userid from customeremploymentinfo where userid=@userid", new { userid = myuserid })).FirstOrDefault();
                    if (!string.IsNullOrEmpty(myuserid2))
                    {
                        await (con.ExecuteAsync($"delete from customeremploymentinfo where userid={myuserid2}"));
                    }
                    string sql = $@"INSERT INTO customeremploymentinfo
                                    (
                                     employeraddress,
                                     employername,
                                     employersphonenumber,
                                     occupation,
                                     userid,
                                     employmentstatus,
                                     sourceoffund,
                                     expectedannualturnover,
                                     CreatedOn)
                                     VALUES(@employeraddress,@employername,@employersphonenumber,
                                     @occupation,@userid,@employmentstatus,@sourceoffund,@expectedannualturnover,'sysdate()')
                                     ";
                    //  string sql = "insert into customeremploymentinfo(Username,device,trackdevice,loginstatus) values(@Username, @device, 'present',1)";
                    await con.ExecuteAsync(sql, new
                    {
                        employeraddress = employmentInfo.EmployerAddress,
                        employername = employmentInfo.EmploymentName,
                        employersphonenumber = employmentInfo.EmployerPhoneNumber,
                        occupation = employmentInfo.Occupation,
                        userid = myuserid,
                        employmentstatus = employmentInfo.EmploymentStatus,
                        sourceoffund = employmentInfo.SourceOfFound,
                        expectedannualturnover = employmentInfo.ExpectedAnnualTurnOver
                    });
                    var usr = await _genServ.GetUserbyUsername(employmentInfo.Username, con);
                    await con.ExecuteAsync("update registration set nin=@nin where CustomerId=@custid", new { custid = usr.CustomerId, nin = employmentInfo.Nin });
                    return new GenericResponse() { Response = EnumResponse.Successful };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }
        /*
        StartDate = DateTime.ParseExact(StartDate, "dd-MM-yyyy", CultureInfo.InvariantCulture)
                       .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        EndDate = DateTime.ParseExact(EndDate, "dd-MM-yyyy", CultureInfo.InvariantCulture).AddDays(1)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        */
        public async Task<GenericResponse> SetNextOfKinInfo(string clientKey, int channelId, NextOfKinInfo nextOfKinInfo)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(nextOfKinInfo.Username, nextOfKinInfo.Session, nextOfKinInfo.ChannelId, con);
                    if (!validateSession)
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    }
                    var myuserid = (await con.QueryAsync<string>("select userid from user_credentials where status=1 and userid=(select id from users where username=@username)", new { username = nextOfKinInfo.Username })).FirstOrDefault();
                    if (string.IsNullOrEmpty(myuserid))
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidUsernameOrPassword };
                    }
                    var myuserid2 = (await con.QueryAsync<string>("select userid from next_kin_information where userid=@userid", new { userid = myuserid })).FirstOrDefault();
                    if (!string.IsNullOrEmpty(myuserid2))
                    {
                        await (con.ExecuteAsync($"delete from next_kin_information where userid={myuserid2}"));
                    }
                    string sql = @$"INSERT INTO next_kin_information
                                        (
                                         UserId,
                                         FirstName,
                                         Lastname,
                                         middlename,
                                         Gender,
                                         DateBirth,
                                         Relationship,
                                         Address,
                                         PhoneNumber,
                                         EmailAddress,
                                         CreatedOn)
                                         VALUES(@UserId,@FirstName,@Lastname,@middlename,@Gender
                                               ,@DateBirth,@Relationship,
                                                @Address,@PhoneNumber,@EmailAddress,
                                                sysdate())";
                    await con.ExecuteAsync(sql, new
                    {
                        UserId = myuserid,
                        Firstname = nextOfKinInfo.FirstName,
                        Lastname = nextOfKinInfo.LastName,
                        middlename = nextOfKinInfo.MiddleName,
                        Gender = nextOfKinInfo.Gender,
                        DateBirth = nextOfKinInfo.DateBirth,
                        Relationship = nextOfKinInfo.Relationship,
                        Address = nextOfKinInfo.Address,
                        PhoneNumber = nextOfKinInfo.PhoneNumber,
                        EmailAddress = nextOfKinInfo.EmailAddress
                    });
                    return new GenericResponse() { Response = EnumResponse.Successful };
                }
            }
            catch (Exception ex)
            {

                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> AddCustomerIdCard(string clientKey, CustomerIdCard customerIdCard, IFormFile idCard)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    
                    var validateSession = await _genServ.ValidateSession(customerIdCard.Username, customerIdCard.Session, customerIdCard.ChannelId, con);
                    if (!validateSession)
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidSession, Message = "Invalid Session" };
                    }
                    
                    var myuserid = (await con.QueryAsync<string>("select userid from user_credentials where credentialtype=2 and status=1 and userid=(select id from users where username=@username)", new { username = customerIdCard.Username })).FirstOrDefault();
                    if (string.IsNullOrEmpty(myuserid))
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidUsernameOrPassword, Message = "Invalid Customer" };
                    }
                    string path = null; ;
                    if (_settings.FileUploadPath=="wwwroot")
                    {
                        path = "./" + _settings.FileUploadPath + "/";
                    }
                    else
                    {
                        path = _settings.FileUploadPath + "\\"  ;
                    }
                   // string path = "./" + _settings.FileUploadPath + "/" + idCard.FileName;
                    _logger.LogInformation("path " + path + " and _settings.FileUploadPath " + _settings.FileUploadPath, null);
                   // Console.WriteLine("idcard name " + Path.GetFileName(path));
                    //_logger.LogInformation("idcard name " + Path.GetFileName(path), null);
                    // var KycDocumentStatus = (await con.QueryAsync<KycDocumentStatus>($"select (select UserName from users where id={myuserid}) as UserName,utilityapprovalstatus,signatureapprovalstatus,idcardapprovalstatus,passportapprovalstatus,passportstatus,signaturestatus,utlitybillstatus,idcardstatus,actionid", new { userid = myuserid })).FirstOrDefault();
                    /*
                    var KycDocumentStatus = (await con.QueryAsync<KycDocumentStatus>(
                                                                @"SELECT 
                                                                    u.Username as UserName, 
                                                                    k.utilityapprovalstatus, 
                                                                    k.signatureapprovalstatus, 
                                                                    k.idcardapprovalstatus, 
                                                                    k.passportapprovalstatus, 
                                                                    k.passportstatus, 
                                                                    k.signaturestatus, 
                                                                    k.utlitybillstatus, 
                                                                    k.idcardstatus, 
                                                                    k.actionid 
                                                                  FROM kycdocumentstatus k
                                                                  JOIN users u ON u.id = k.userid
                                                                  WHERE k.userid = @userid;",
                                                                new { userid = myuserid }
                                                            )).FirstOrDefault();
                        */
                    string query = $@"SELECT 
                            u.Username as UserName, 
                            k.utilityapprovalstatus, 
                            k.signatureapprovalstatus, 
                            k.idcardapprovalstatus, 
                            k.passportapprovalstatus, 
                            k.passportstatus, 
                            k.signaturestatus, 
                            k.utlitybillstatus, 
                            k.idcardstatus, 
                            k.actionid 
                            FROM kycdocumentstatus k
                            JOIN users u ON u.id = k.userid
                            WHERE k.userid = @userid and k.typeofdocument=@typeofdocument";
                    KycDocumentStatus icardkycDocumentStatus = null;
                    KycDocumentStatus passportkycDocumentStatus = null;
                    KycDocumentStatus signaturekycDocumentStatus = null;
                    KycDocumentStatus utilitybillkycDocumentStatus = null;
                    var mylist = new List<string>() { "idcard", "passport", "signature", "utilitybill"};
                    foreach (var docs in mylist)
                    {
                        if (docs == "idcard")
                        {
                            icardkycDocumentStatus = (await con.QueryAsync<KycDocumentStatus>(query, new { userid = myuserid, typeofdocument = "idcard" })).FirstOrDefault();
                        }
                        else
                        if (docs == "passport")
                        {
                            passportkycDocumentStatus = (await con.QueryAsync<KycDocumentStatus>(query, new { userid = myuserid, typeofdocument = "passport" })).FirstOrDefault();
                        }
                        else if (docs == "signature")
                        {
                            signaturekycDocumentStatus = (await con.QueryAsync<KycDocumentStatus>(query, new { userid = myuserid, typeofdocument = "signature" })).FirstOrDefault();
                        }
                        else if (docs == "utilitybill")
                        {
                            utilitybillkycDocumentStatus = (await con.QueryAsync<KycDocumentStatus>(query, new { userid = myuserid, typeofdocument = "utilitybill" })).FirstOrDefault();
                        }
                    }
                    if (icardkycDocumentStatus?.idcardapprovalstatus == true &&
                        icardkycDocumentStatus?.idcardstatus=="accept"&&
                        utilitybillkycDocumentStatus?.utilityapprovalstatus == true &&
                        utilitybillkycDocumentStatus?.utlitybillstatus=="accept"&&
                        passportkycDocumentStatus?.passportapprovalstatus == true &&
                        passportkycDocumentStatus?.passportstatus=="accept"&&
                        signaturekycDocumentStatus?.signatureapprovalstatus == true
                        &&signaturekycDocumentStatus?.signaturestatus=="accept")
                    {
                        return new GenericResponse() { Success = false, Response = EnumResponse.AllApprovedAll };  // that is if all approved
                    }
                    /*
                    if (KycDocumentStatus.signatureapprovalstatus && KycDocumentStatus.utilityapprovalstatus &&
                        KycDocumentStatus.passportapprovalstatus && KycDocumentStatus.idcardapprovalstatus)
                    {
                        return new GenericResponse() {Success=false,Response=EnumResponse.AllApprovedAll};  // that is if all approved
                    }
                    */
                    if ( idCard.FileName.EndsWith(".jpeg") || idCard.FileName.EndsWith(".png") || idCard.FileName.EndsWith(".jpg"))
                    {
                        var myuserid2 = (await con.QueryAsync<string>("select userid from idcard_upload where userid=@userid", new { userid = myuserid })).FirstOrDefault();
                        if (!string.IsNullOrEmpty(myuserid))
                        {
                            var IdCardKycStatus = (await con.QueryAsync<IdCardKycStatus>("select idcardapprovalstatus,idcardstatus from kycdocumentstatus where userid=@id and typeofdocument='idcard'", new { id = myuserid2 })).FirstOrDefault();
                            if (IdCardKycStatus != null)
                            {
                                await con.ExecuteAsync("update kycdocumentstatus set idcardapprovalstatus=false,idcardstatus='Awaiting review' where userid=@id", new { id = myuserid2 });
                                await con.ExecuteAsync("update customerkycstatus set actionid=0,kycstatus=false where username=@id", new { id = customerIdCard.Username });
                            }
                            else
                            {
                                await con.ExecuteAsync($@"insert into kycdocumentstatus(idcardapprovalstatus,idcardstatus,typeofdocument,userid)
                          values(@idcardapprovalstatus,@idcardstatus,@typeofdocument,@userid)", new
                                {
                                    idcardapprovalstatus = false,
                                    idcardstatus = "Awaiting review",
                                    typeofdocument = "idcard",
                                    userid = long.Parse(myuserid2)
                                });
                            }
                            await con.ExecuteAsync($"delete from idcard_upload where userid={myuserid}");
                          //  await con.ExecuteAsync("update kycdocumentstatus set idcardapprovalstatus=false,idcardstatus='Awaiting review' where userid=@id", new { id = myuserid2 });
                          //  await con.ExecuteAsync("update customerkycstatus set actionid=0,kycstatus=false where username=@id", new { id = customerIdCard.Username });                     
                        }
                        /*
                        string sql = $@"INSERT INTO idcard_upload
                                 (
                                 UserId,
                                 IdNumber,
                                 IssueDate,
                                 ExpiryDate,
                                 Filelocation,
                                 IdType,
                                 CreatedOn)
                                VALUES(@UserId,@IdNumber,@IssueDate,@ExpiryDate,@Filelocation,now(),@IdType)";
                        await con.ExecuteAsync(sql, new
                        {
                            UserId = myuserid,
                            IdNumber = customerIdCard.IdNumber,
                            IssueDate = customerIdCard.IssueDate,
                            ExpiryDate = customerIdCard.ExpiryDate,
                            Filelocation = path+idCard.FileName,
                            IdType = customerIdCard.IdType
                        });
                        */
                        string sql = $@"INSERT INTO idcard_upload
                                          (
                                          UserId,
                                          IdNumber,
                                          IssueDate,
                                          ExpiryDate,
                                          Filelocation,
                                          IdType,
                                          CreatedOn,
                                          submittedrequest)
                                         VALUES(@UserId, @IdNumber, @IssueDate, @ExpiryDate, @Filelocation, @IdType, NOW(),0)";

                        await con.ExecuteAsync(sql, new
                        {
                            UserId = myuserid,
                            IdNumber = customerIdCard.IdNumber,
                            IssueDate = customerIdCard.IssueDate,
                            ExpiryDate = customerIdCard.ExpiryDate,
                            Filelocation = path + idCard.FileName,
                            IdType = customerIdCard.IdType
                        });

                        await _fileService.SaveFileAsync(idCard, path); // save to external folder
                        return new GenericResponse() { Response = EnumResponse.Successful, Success = true, Message = "Idcard uploaded successfully" };
                    }
                    else
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidFileformat };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> SetCustomerDocument(string clientKey, CustomerDocuments customerDocuments, IFormFile passport, IFormFile signature, IFormFile utilityBill)
        {
            //document type should be around utilitybill,passport,signature
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(customerDocuments.Username, customerDocuments.Session, customerDocuments.ChannelId, con);
                    if (!validateSession)
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidSession, Message = "Invalid Session" };
                    }
                    var myuserid = (await con.QueryAsync<string>("select userid from user_credentials where credentialtype=2 and status=1 and userid=(select id from users where Username=@Username)", new { username = customerDocuments.Username })).FirstOrDefault();
                    CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con,int.Parse(myuserid));
                    if (string.IsNullOrEmpty(myuserid))
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidUsernameOrPassword, Message = "Invalid Customer" };
                    }
                    string path = "./" + _settings.FileUploadPath + "/";
                    _logger.LogInformation("path " + path + " and _settings.FileUploadPath " + _settings.FileUploadPath, null);
                    Console.WriteLine("customer documents name " + Path.GetFileName(path));
                    _logger.LogInformation("customer documents name " + Path.GetFileName(path), null);
                    if (!passport.FileName.EndsWith(".jpeg") && !passport.FileName.EndsWith(".png") && !passport.FileName.EndsWith(".jpg"))
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidFileformat, Message = "All file must be in either .jpeg,png,jpg" };
                    }
                    if (!signature.FileName.EndsWith(".jpeg") && !signature.FileName.EndsWith(".png") && !signature.FileName.EndsWith(".jpg"))
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidFileformat, Message = "All file must be in either .jpeg,png,jpg" };
                    }
                    if(!utilityBill.FileName.EndsWith(".jpeg") && !utilityBill.FileName.EndsWith(".png") && !utilityBill.FileName.EndsWith(".jpg"))
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidFileformat, Message = "All file must be in either .jpeg,png,jpg" };
                    }
                    int passportresult = await processFileUpload("passport", myuserid, customerDocuments, con, passport, path + passport.FileName);
                    int signatureresult = await processFileUpload("signature", myuserid, customerDocuments, con, signature, path + signature.FileName);
                    int utilityBillresult = await processFileUpload("utilityBill", myuserid, customerDocuments, con, utilityBill, path + utilityBill.FileName);
                    // send email .
                    await _fileService.SaveFileAsync(passport, path); // save to external folder
                    await _fileService.SaveFileAsync(utilityBill, path); // save to external folder
                    await _fileService.SaveFileAsync(signature, path); // save to external folder
                    Thread thread = new Thread(async () =>
                    {
                        var firstname = (await con.QueryAsync<string>("select firstname from users where Username=@Username)", new { username = customerDocuments.Username })).FirstOrDefault();
                        SendMailObject sendMailObject = new SendMailObject();
                        sendMailObject.Subject = "Notification of Account Upgrade In Process";
                        sendMailObject.Email = customerDataNotFromBvn?.Email;
                        sendMailObject.Html = $@"Dear {firstname.ToUpper()}
                                              <p>Thank you for providing the necessary details and documents for upgrading your mobile banking account. Your request has been received and is currently being processed by our customer support team. </p>
                                              <p>Should we require any additional information or documentation, we will reach out to you promptly. Otherwise, you can expect to receive confirmation of your upgraded mobile banking account within next 24 hours.
                                              For any feedback or enquiries, please call our Contact Center on 07004446147 or send an email to support@trustbancgroup.com.</p> 
                                              <p>Thank you for choosing TrustBanc J6 MfB</p>
                                              ";
                        _genServ.SendMail(sendMailObject);
                        /*
                        sendMailObject.Email = "opeyemi.adubiaro@trustbancgroup.com"; //for mgt
                        sendMailObject.Html = $@"This customer Mr/Mrs {firstname.ToUpper()} has just uploaded his/her credentials this day at {new DateTime()}.Kindly check and upgrade the iucormeteraccount on her behalf";
                        _genServ.SendMail(sendMailObject);
                        */
                        var usr = await _genServ.GetUserbyUsername(customerDocuments.Username, con);                       
                        sendMailObject.Email = _settings.CustomerServiceEmail; //for mgt
                        sendMailObject.Html = $@"This customer Mr/Mrs {firstname.ToUpper()} {usr.LastName.ToUpper()} with Account {customerDocuments.AccountNumberTobeUpgraded} has just uploaded his/her credentials this day at {new DateTime()}.Kindly check and upgrade the account on his/her behalf";
                        _genServ.SendMail(sendMailObject);
                    });
                    thread.Start();
                    return new GenericResponse() { Response = EnumResponse.Successful, Message = "All files uploaded successfully" };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }


        private async Task<int> processUploadDocuments(string documentType, string myuserid, IDbConnection con, string path, string tablename)
        {
            var myuserid2 = (await con.QueryAsync<string>($"select userid from {tablename} where userid=@userid", new { userid = myuserid })).FirstOrDefault();
            if (!string.IsNullOrEmpty(myuserid2))
            {
                await con.ExecuteAsync($"delete from {tablename} where userid={myuserid}");
            }
            string sql = $@"INSERT INTO {tablename}
                                    (
                                     document,
                                     userid,
                                     filelocation)
                                    VALUES(@Document,@USERID,@FILELOCATION)";
            await con.ExecuteAsync(sql, new
            {
                Document = documentType,
                USERID = myuserid,
                FILELOCATION = path
            });
            return 1;
        }

        private async Task<int> processFileUpload(string documentType, string myuserid, CustomerDocuments customerDocuments, IDbConnection con, IFormFile passport, string path)
        {
            var myuserid2 = (await con.QueryAsync<string>($"select userid from document_type where userid=@userid and document='{documentType}'", new { userid = myuserid })).FirstOrDefault();
            //var KycDocumentStatus = (await con.QueryAsync<KycDocumentStatus>($"select (select UserName from users where id={myuserid2}) as UserName,utilityapprovalstatus,signatureapprovalstatus,idcardapprovalstatus,passportapprovalstatus,passportstatus,signaturestatus,utlitybillstatus,idcardstatus,actionid", new { userid = myuserid2 })).FirstOrDefault();
            // var KycDocumentStatus = (await con.QueryAsync<KycDocumentStatus>($"select (select UserName from users where id={myuserid}) as UserName,utilityapprovalstatus,signatureapprovalstatus,idcardapprovalstatus,passportapprovalstatus,passportstatus,signaturestatus,utlitybillstatus,idcardstatus,actionid", new { userid = myuserid })).FirstOrDefault();
            string query = $@"SELECT 
                            u.Username as UserName, 
                            k.utilityapprovalstatus, 
                            k.signatureapprovalstatus, 
                            k.idcardapprovalstatus, 
                            k.passportapprovalstatus, 
                            k.passportstatus, 
                            k.signaturestatus, 
                            k.utlitybillstatus, 
                            k.idcardstatus, 
                            k.actionid 
                            FROM kycdocumentstatus k
                            JOIN users u ON u.id = k.userid
                            WHERE k.userid = @userid and k.typeofdocument=@typeofdocument";
            KycDocumentStatus icardkycDocumentStatus = null;
            KycDocumentStatus passportkycDocumentStatus = null;
            KycDocumentStatus signaturekycDocumentStatus = null;
            KycDocumentStatus utilitybillkycDocumentStatus = null;
            var mylist = new List<string>() {"idcard","passport","signature","utilitybill" };
            foreach (var docs in mylist)
            {
                if (docs=="idcard") {
                    icardkycDocumentStatus = (await con.QueryAsync<KycDocumentStatus>(query,new { userid = myuserid,typeofdocument="idcard"})).FirstOrDefault();
                }else
                if (docs == "passport")
                {
                    passportkycDocumentStatus = (await con.QueryAsync<KycDocumentStatus>(query, new { userid = myuserid, typeofdocument = "passport" })).FirstOrDefault();
                }
                else if (docs == "signature")
                {
                    signaturekycDocumentStatus = (await con.QueryAsync<KycDocumentStatus>(query, new { userid = myuserid, typeofdocument = "signature" })).FirstOrDefault();
                }
                else if (docs == "utilitybill")
                {
                    utilitybillkycDocumentStatus = (await con.QueryAsync<KycDocumentStatus>(query, new { userid = myuserid, typeofdocument = "utilitybill" })).FirstOrDefault();
                }
            }
            if (icardkycDocumentStatus?.idcardapprovalstatus == true &&
                icardkycDocumentStatus?.idcardstatus=="accept"&&
                 utilitybillkycDocumentStatus?.utilityapprovalstatus == true &&
                 utilitybillkycDocumentStatus?.utlitybillstatus=="accept" &&
                 passportkycDocumentStatus?.passportapprovalstatus == true &&
                 passportkycDocumentStatus?.passportstatus=="accept"
                 &&
                 signaturekycDocumentStatus?.signatureapprovalstatus == true&&
                 signaturekycDocumentStatus?.signaturestatus=="accept"
                 )
            {
                return -1;  // That is if all are approved
            }
            if (documentType.Equals("utilityBill", StringComparison.CurrentCultureIgnoreCase))
            {
                var UtiityKycStatus = (await con.QueryAsync<UtiityKycStatus>("select utilityapprovalstatus,utlitybillstatus from kycdocumentstatus where userid=@id and typeofdocument='utilityBill'", new { id = myuserid2 })).FirstOrDefault();
                if (UtiityKycStatus != null)
                {
                    await con.ExecuteAsync("update kycdocumentstatus set utilityapprovalstatus=false,utlitybillstatus='Awaiting review' where userid=@id", new { id = myuserid2 });
                    await con.ExecuteAsync("update customerkycstatus set actionid=0,kycstatus=false where username=@id", new { id = customerDocuments.Username });
                }
                else
                {
                    await con.ExecuteAsync($@"insert into kycdocumentstatus(utilityapprovalstatus,utlitybillstatus,typeofdocument,userid)
                          values(@utilityapprovalstatus,@utlitybillstatus,@typeofdocument,@userid)", new
                    {
                        utilityapprovalstatus = false,
                        utlitybillstatus = "Awaiting review",
                        typeofdocument = "utilityBill",
                        userid=long.Parse(myuserid2)
                    });
                }
            }else if (documentType.Equals("passport", StringComparison.CurrentCultureIgnoreCase))
            {
                var passportData = (await con.QueryAsync<PassportKycStatus>("select passportapprovalstatus,passportstatus from kycdocumentstatus where userid=@id and typeofdocument='passport'", new {id= myuserid2 })).FirstOrDefault();
                if(passportData!=null) {
                    await con.ExecuteAsync("update kycdocumentstatus set passportapprovalstatus=false,passportstatus='Awaiting review' where userid=@id", new { id = myuserid2 });
                    await con.ExecuteAsync("update customerkycstatus set actionid=0,kycstatus=false where username=@id", new { id = customerDocuments.Username });
                }
                else
                {
                    await con.ExecuteAsync($@"insert into kycdocumentstatus(passportapprovalstatus,passportstatus,typeofdocument,userid)
                          values(@passportapprovalstatus,@passportstatus,@typeofdocument,@userid)",new
                    {
                        passportapprovalstatus=false,
                        passportstatus= "Awaiting review",
                        typeofdocument="passport",
                        userid = long.Parse(myuserid2)
                    });
                }
            }
            else if (documentType.Equals("signature", StringComparison.CurrentCultureIgnoreCase))
            {
                var SignatureKycStatus = (await con.QueryAsync<SignatureKycStatus>("select signatureapprovalstatus,signaturestatus from kycdocumentstatus where userid=@id and typeofdocument='signature'", new { id = myuserid2 })).FirstOrDefault();
                if (SignatureKycStatus != null)
                {
                    await con.ExecuteAsync("update kycdocumentstatus set signatureapprovalstatus=false,signaturestatus='Awaiting review' where userid=@id", new { id = myuserid2 });
                    await con.ExecuteAsync("update customerkycstatus set actionid=0,kycstatus=false where username=@id", new { id = customerDocuments.Username });
                }
                else
                {
                    await con.ExecuteAsync($@"insert into kycdocumentstatus(signatureapprovalstatus,signaturestatus,typeofdocument,userid)
                          values(@signatureapprovalstatus,@signaturestatus,@typeofdocument,@userid)", new
                    {
                        signatureapprovalstatus = false,
                        signaturestatus = "Awaiting review",
                        typeofdocument = "signature",
                        userid = long.Parse(myuserid2)
                    });
                }
            }
            else if (documentType.Equals("idcard", StringComparison.CurrentCultureIgnoreCase))
            {
                var IdCardKycStatus = (await con.QueryAsync<IdCardKycStatus>("select signatureapprovalstatus,signaturestatus from kycdocumentstatus where userid=@id and typeofdocument='signature'", new { id = myuserid2 })).FirstOrDefault();
                if (IdCardKycStatus != null)
                {
                    await con.ExecuteAsync("update kycdocumentstatus set idcardapprovalstatus=false,idcardstatus='Awaiting review' where userid=@id", new { id = myuserid2 });
                    await con.ExecuteAsync("update customerkycstatus set actionid=0,kycstatus=false where username=@id", new { id = customerDocuments.Username });
                }
                else
                {
                    await con.ExecuteAsync($@"insert into kycdocumentstatus(idcardapprovalstatus,idcardstatus,typeofdocument,userid)
                          values(@idcardapprovalstatus,@idcardstatus,@typeofdocument,@userid)", new
                    {
                        idcardapprovalstatus = false,
                        idcardstatus = "Awaiting review",
                        typeofdocument = "idcard",
                        userid = long.Parse(myuserid2)
                    });
                }
            }
            if (!string.IsNullOrEmpty(myuserid2))
            {
                await con.ExecuteAsync($"delete from document_type where userid={myuserid} and Document='{documentType}'");
            }
            string sql = $@"INSERT INTO document_type
                                    (
                                     Document,
                                     USERID,
                                     FILELOCATION,
                                     submittedrequest)
                                    VALUES(@Document,@USERID,@FILELOCATION,0)";
            _logger.LogInformation("documentType " + documentType);
            await con.ExecuteAsync(sql, new
            {
                Document = documentType,
                USERID = myuserid,
                FILELOCATION = path
            });
            _logger.LogInformation("path successfully inserted " + path);
            return 1;
        }

        public async Task<GenericResponse> Kyc(string clientKey, string session, string Username, int ChannelId)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Username, session, ChannelId, con);
                    if (!validateSession)
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidSession, Message = "Invalid Session" };
                    }
                    //check if it is a migrated user
                    var usr = await _genServ.GetUserbyUsername(Username, con);
                    var IsMigratedUserId = (await con.QueryAsync<string>("select id from users where username=@username and migrateduser=true", new { username = Username })).FirstOrDefault();
                    var myuserid = usr.Id;
                    var listOfDocumentType = (await con.QueryAsync<KycDocumentType>($"select * from document_type where userid=@userid", new { userid = myuserid }));
                    var idNumber = (await con.QueryAsync<string>($"select IdNumber from idcard_upload where userid=@userid", new { userid = myuserid })).FirstOrDefault();
                    var Nin = (await con.QueryAsync<string>($"select nin from registration where username=@username", new { username = Username })).FirstOrDefault();
                    _logger.LogInformation("customer nin " + Nin);
                    var userid_customeremploymentinfo = (await con.QueryAsync<string>($"select * from customeremploymentinfo where userid=@userid", new { userid = myuserid })).FirstOrDefault();
                    var userid_nextofkininfo = (await con.QueryAsync<string>($"select * from next_kin_information where userid=@userid", new { userid = myuserid })).FirstOrDefault();
                    KycResponse kycResponse = new KycResponse();
                    BalanceEnquiryResponse balanceEnquiryResponse = await _genServ.GetAccountbyCustomerId(usr.CustomerId);
                    string kycTier = balanceEnquiryResponse.balances.ElementAtOrDefault(0).kycLevel;
                    if (!string.IsNullOrEmpty(IsMigratedUserId))
                    {
                       if (kycTier.Equals("003", StringComparison.CurrentCultureIgnoreCase))
                        {
                        kycResponse.isFilledEmploymentInfo = true;
                        kycResponse.isFillNextOfKininfo = true;
                        kycResponse.idCard = true;
                        kycResponse.IsNin = !string.IsNullOrEmpty(Nin) ? true : false;
                        kycResponse.ProfileCompleted = kycResponse.isFilledEmploymentInfo
                            && kycResponse.isFillNextOfKininfo && kycResponse.idCard && kycResponse.IsNin ? true : false;
                        kycResponse.Response = EnumResponse.Successful;
                        kycResponse.Success = true;
                        return kycResponse;
                        }
                    }
                    if (listOfDocumentType.Any())
                    {
                        foreach (var item in listOfDocumentType)
                        {
                            if (item.document.ToLower() == "signature")
                            {
                                kycResponse.Signature = true;
                            }
                            if (item.document.ToLower() == "passport")
                            {
                                kycResponse.Passport = true;
                            }
                            if (item.document.ToLower() == "utilityBill".ToLower())
                            {
                                kycResponse.UtilityBill = true;
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(idNumber))
                    {
                        kycResponse.idCard = true;
                    }
                    if (!string.IsNullOrEmpty(userid_customeremploymentinfo))
                    {
                        kycResponse.isFilledEmploymentInfo = true;
                    }
                    if (!string.IsNullOrEmpty(userid_nextofkininfo))
                    {
                        kycResponse.isFillNextOfKininfo = true;
                    }
                    if (!string.IsNullOrEmpty(Nin))
                    {
                        kycResponse.IsNin = true;
                    }
                    if (kycResponse.idCard && kycResponse.Signature &&
                        kycResponse.Passport && kycResponse.isFilledEmploymentInfo &&
                        kycResponse.isFillNextOfKininfo && kycResponse.IsNin)
                    {
                        kycResponse.ProfileCompleted = true;
                        kycResponse.Success = true;
                        //update the kyc status for admin
                        var checkedUserName = (await con.QueryAsync<string>(
                           "SELECT username FROM customerkycstatus WHERE username = @username",
                            new { username = Username })).FirstOrDefault();
                        _logger.LogInformation("check kyc status-checkedUserName " + checkedUserName);
                        if (string.IsNullOrEmpty(checkedUserName))
                        {
                            await con.ExecuteAsync(
                                "INSERT INTO customerkycstatus(username,kycstatus,created_at) VALUES(@username,@status,@created_at)",
                                new { username = Username, status = false, created_at = DateTime.Now });
                        }
                    }
                    kycResponse.Response = EnumResponse.Successful;
                    return kycResponse;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> CompareNinAndBvnForValidation(string clientKey, string session, string username, int channelId, string nin, string inputbvn = null)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(username, session, channelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession, Message = "Invalid Session" };
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
                    _logger.LogInformation("valloginusername " + _settings.authloginusername);
                    _logger.LogInformation("auth password " + _settings.authpassword);
                    _logger.LogInformation("about to  login for nin " + JsonConvert.SerializeObject(loginobj));
                    //http://localhost:8080/MFB_USSD/api/v1/user/login
                    //http://localhost:9001/api/v1/user/login
                    string Loginresponse = await _genServ.CallServiceAsyncToString(Method.POST, "http://localhost:8080/MFB_USSD/api/v1/user/login", loginobj, true);
                    JObject loginjson = (JObject)JToken.Parse(Loginresponse);
                    string accessToken = loginjson.ContainsKey("response") ? loginjson["response"]["accessToken"].ToString() : "";
                    if (string.IsNullOrEmpty(accessToken))
                    {
                        return new GenericResponse() { Response = EnumResponse.NotSuccessful };
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
                    var usr = await _genServ.GetUserbyUsername(username, con);
                    if (!(ninValidation.response_code == "00" && ninValidation.ninData.residence_address.ToLower().Contains("suspended")))
                    {
                        string ninName = ninValidation.ninData.firstname + " " + ninValidation.ninData.surname + " " + ninValidation.ninData.middlename;
                        string ninDateOfBirth = ninValidation.ninData.birthdate;
                        string CustFirstName = usr.Firstname;
                        string CustLastName = usr.LastName;
                        string custbvn = inputbvn == null ? usr.Bvn : inputbvn;
                        Console.WriteLine("custbvn " + custbvn);
                        var result = Task.Run(async () =>
                        {
                            // http://localhost:8080/MFB_USSD/api/v1/verification/bvn
                            //http://localhost:9001/api/v1/verification/bvn
                            string bvnurl = "http://localhost:8080/MFB_USSD/api/v1/verification/bvn";
                            string testbvnurl = _settings.newbvnurl;
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
                            return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
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
                            /*
                            if (uniqueCount >= 2)
                            {
                                Console.WriteLine("At least two of the names are present in ninName.");
                            }
                            else
                            {
                                Console.WriteLine("Less than two of the names are present in ninName.");
                            }
                            */
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
                                    var ninInDB = (await con.QueryAsync<string>("select nin from registration where username=@usrname", new { usrname = username })).FirstOrDefault();
                                    if (string.IsNullOrEmpty(ninInDB))
                                    {
                                        await con.ExecuteAsync("update registration set nin=@ninInDB where username=@usrname", new { ninInDB = nin, usrname = username });
                                        // await con.ExecuteAsync("update registration set nin=@ninInDB where username=@usrname", new { ninInDB = nin, usrname = username });
                                    }
                                    return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
                                }
                                else
                                {
                                    // Console.WriteLine("The dates are not equal.");
                                    return new GenericResponse() { Success = false, Response = EnumResponse.DateMismatch };
                                }
                            }
                        }
                        else
                        {
                            return new GenericResponse() { Success = false, Response = EnumResponse.NameMisMatch };
                        }
                        // return new GenericResponse() { Success=true, Response = EnumResponse.ValidNin };
                    }
                    return new GenericResponse() { Success = false, Response = EnumResponse.InValidNin };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }
        static bool TryParseDate(string dateString, string[] formats, out DateTime date)
        {
            return DateTime.TryParseExact(dateString, formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out date);
        }

        public Task<GenericResponse> SetAdvertImageOnMobile(string clientKey, AdvertImageOnMobile request)
        {
            throw new NotImplementedException();
        }

        public async Task<GenericResponse> SetUtilityBill(string clientKey, CustomerDocuments customerDocuments, IFormFile utilityBill)
        {
            //document type should be around utilitybill,passport,signature
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    
                  
                    var validateSession = await _genServ.ValidateSession(customerDocuments.Username, customerDocuments.Session, customerDocuments.ChannelId, con);
                    if (!validateSession)
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidSession, Message = "Invalid Session" };
                    }
                   
                    var myuserid = (await con.QueryAsync<string>("select userid from user_credentials where credentialtype=2 and status=1 and userid=(select id from users where Username=@Username)", new { username = customerDocuments.Username })).FirstOrDefault();
                    if (string.IsNullOrEmpty(myuserid))
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidUsernameOrPassword, Message = "Invalid Customer" };
                    }
                    CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con,int.Parse(myuserid));
                    string path = null;
                    if (_settings.FileUploadPath=="wwwroot") {
                        path = "./" + _settings.FileUploadPath + "/";
                    }
                    else
                    {
                        path = _settings.FileUploadPath+"\\";
                    }
                    _logger.LogInformation("path " + path + " and _settings.FileUploadPath " + _settings.FileUploadPath, null);
                    Console.WriteLine("utilitybill name " + Path.GetFileName(path));
                    _logger.LogInformation("utilitybill name " + Path.GetFileName(path), null);
                    if (!utilityBill.FileName.EndsWith(".jpeg") && !utilityBill.FileName.EndsWith(".png") && !utilityBill.FileName.EndsWith(".jpg"))
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidFileformat, Message = "All file must be in either .jpeg,png,jpg" };
                    }
                    int utilityBillresult = await processFileUpload("utilityBill", myuserid, customerDocuments, con, utilityBill, path + utilityBill.FileName);
                    if (utilityBillresult==-1)
                    {
                        return new GenericResponse() { Success = false, Response = EnumResponse.AllApprovedAll };
                    }
                    await _fileService.SaveFileAsync(utilityBill, path); // save to external folder
                    // send email .
                    Thread thread = new Thread(async () =>
                    {
                        var usr = await _genServ.GetUserbyUsername(customerDocuments.Username, con);
                        SendMailObject sendMailObject = new SendMailObject();
                        sendMailObject.Subject = "Notification of Kyc Account Upgrade In Process";
                        sendMailObject.Email = customerDataNotFromBvn?.Email;
                        sendMailObject.Html = $@"Dear {usr.Firstname.ToUpper()}
                                              <p>Thank you for providing the necessary details and utilitybill documents for upgrading your mobile banking account. Your request has been received and is currently being processed by our customer support team. </p>
                                              <p>Should we require any additional information or documentation, we will reach out to you promptly. Otherwise, you can expect to receive confirmation of your upgraded mobile banking account within next 24 hours.
                                              For any feedback or enquiries, please call our Contact Center on 07004446147 or send an email to support@trustbancgroup.com.</p> 
                                              <p>Thank you for choosing TrustBanc J6 MfB</p>
                                              ";
                        _genServ.SendMail(sendMailObject);
                        sendMailObject.Email = _settings.CustomerServiceEmail; //for mgt
                        sendMailObject.Html = $@"This customer Mr/Mrs {usr.Firstname.ToUpper()} {usr.LastName.ToUpper()} with Account {customerDocuments.AccountNumberTobeUpgraded} has just uploaded his/her credentials this day at {new DateTime()}.Kindly check and upgrade the account on his/her behalf";
                        _genServ.SendMail(sendMailObject);
                    });
                    thread.Start();
                    return new GenericResponse() { Response = EnumResponse.Successful, Message = "All files uploaded successfully" };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> SetSignatureAndPassport(string clientKey, CustomerDocuments customerDocuments, IFormFile passport, IFormFile signature)
        {
            //document type should be around utilitybill,passport,signature
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(customerDocuments.Username, customerDocuments.Session, customerDocuments.ChannelId, con);
                    if (!validateSession)
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidSession, Message = "Invalid Session" };
                    }
                    var myuserid = (await con.QueryAsync<string>("select userid from user_credentials where credentialtype=2 and status=1 and userid=(select id from users where Username=@Username)", new { username = customerDocuments.Username })).FirstOrDefault();
                    if (string.IsNullOrEmpty(myuserid))
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidUsernameOrPassword, Message = "Invalid Customer" };
                    }
                    CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con,int.Parse(myuserid));
                    string path = "./" + _settings.FileUploadPath + "/";
                    _logger.LogInformation("path " + path + " and _settings.FileUploadPath " + _settings.FileUploadPath, null);
                    Console.WriteLine("passport name " + Path.GetFileName(path));
                    _logger.LogInformation("passport name " + Path.GetFileName(path), null);
                    if (!(passport.FileName.EndsWith(".jpeg") || passport.FileName.EndsWith(".png") || passport.FileName.EndsWith(".jpg") || passport.FileName.EndsWith(".pdf")))
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidFileformat, Message = "All file must be in either .jpeg,.jpg,png,pdf" };
                    }
                    if (!(signature.FileName.EndsWith(".jpeg") || signature.FileName.EndsWith(".png") || signature.FileName.EndsWith(".jpg") || signature.FileName.EndsWith(".pdf")))
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidFileformat, Message = "All file must be in either .jpeg,.jpg,png,pdf" };
                    }
                    int passportresult = await processFileUpload("passport", myuserid, customerDocuments, con, passport, path + passport.FileName);
                    int signatureresult = await processFileUpload("signature", myuserid, customerDocuments, con, signature, path + signature.FileName);
                    await _fileService.SaveFileAsync(passport, path); // save to external folder
                    await _fileService.SaveFileAsync(signature, path); // save to external folder
                    // send email .
                    Thread thread = new Thread(async () =>
                    {
                        var firstname = (await con.QueryAsync<string>("select firstname from users where Username=@Username)", new { username = customerDocuments.Username })).FirstOrDefault();
                        SendMailObject sendMailObject = new SendMailObject();
                        sendMailObject.Subject = "Notification of Account Upgrade In Process";
                        sendMailObject.Email = customerDataNotFromBvn?.Email;
                        sendMailObject.Html = $@"Dear {firstname.ToUpper()}
                                              <p>Thank you for providing the necessary details and documents for upgrading your mobile banking account. Your request has been received and is currently being processed by our customer support team. </p>
                                              <p>Should we require any additional information or documentation, we will reach out to you promptly. Otherwise, you can expect to receive confirmation of your upgraded mobile banking account within next 24 hours.
                                              For any feedback or enquiries, please call our Contact Center on 07004446147 or send an email to support@trustbancgroup.com.</p> 
                                              <p>Thank you for choosing TrustBanc J6 MfB</p>
                                              ";
                        _genServ.SendMail(sendMailObject);
                        /*
                        sendMailObject.Email = "opeyemi.adubiaro@trustbancgroup.com"; //for mgt
                        sendMailObject.Html = $@"This customer Mr/Mrs {firstname.ToUpper()} has just uploaded his/her credentials this day at {new DateTime()}.Kindly check and upgrade the iucormeteraccount on her behalf";
                        _genServ.SendMail(sendMailObject);
                        */
                        var usr = await _genServ.GetUserbyUsername(customerDocuments.Username, con);
                        sendMailObject.Email = _settings.CustomerServiceEmail; //for mgt
                        sendMailObject.Html = $@"This customer Mr/Mrs {firstname.ToUpper()} {usr.LastName.ToUpper()} with Account {customerDocuments.AccountNumberTobeUpgraded} has just uploaded his/her credentials this day at {new DateTime()}.Kindly check and upgrade the account on his/her behalf";
                        _genServ.SendMail(sendMailObject);
                    });
                    thread.Start();
                    return new GenericResponse() { Response = EnumResponse.Successful, Message = "All files uploaded successfully" };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> AddPassport(string clientKey, CustomerDocuments customerDocuments, IFormFile passport)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    
                    var validateSession = await _genServ.ValidateSession(customerDocuments.Username, customerDocuments.Session, customerDocuments.ChannelId, con);
                    if (!validateSession)
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidSession, Message = "Invalid Session" };
                    }
                    
                    var myuserid = (await con.QueryAsync<string>("select userid from user_credentials where credentialtype=2 and status=1 and userid=(select id from users where Username=@Username)", new { username = customerDocuments.Username })).FirstOrDefault();
                    if (string.IsNullOrEmpty(myuserid))
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidUsernameOrPassword, Message = "Invalid Customer" };
                    }
                    CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, int.Parse(myuserid));
                    string path = null;
                    if (_settings.FileUploadPath == "wwwroot")
                    {
                        path = "./" + _settings.FileUploadPath + "/";
                    }
                    else {
                        path = _settings.FileUploadPath+"\\";
                    }
                    _logger.LogInformation("path " + path + " and _settings.FileUploadPath " + _settings.FileUploadPath, null);
                    Console.WriteLine("passport name " + Path.GetFileName(path));
                    _logger.LogInformation("passport name " + Path.GetFileName(path), null);
                    if (!passport.FileName.EndsWith(".jpeg") && !passport.FileName.EndsWith(".png") && !passport.FileName.EndsWith(".jpg"))
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidFileformat, Message = "All file must be in either .jpeg,png,jpg" };
                    }
                    int passportresult = await processFileUpload("passport", myuserid, customerDocuments, con, passport, path + passport.FileName);
                    if (passportresult == -1)
                    {
                        return new GenericResponse() { Success = false, Response = EnumResponse.AllApprovedAll, Message = "already approved all" };
                    }
                    await _fileService.SaveFileAsync(passport, path); // save to external folder
                                                                      // send email .
                    var usr = await _genServ.GetUserbyUsername(customerDocuments.Username, con);
                    Thread thread = new Thread(async () =>
                    {
                        var firstname = (await con.QueryAsync<string>("select firstname from users where Username=@Username", new { Username = customerDocuments.Username })).FirstOrDefault();
                        SendMailObject sendMailObject = new SendMailObject();
                        sendMailObject.Subject = "Notification of Kyc Account Upgrade In Process";
                        sendMailObject.Email = customerDataNotFromBvn?.Email;
                        sendMailObject.Html = $@"Dear {usr.Firstname.ToUpper()}
                                              <p>Thank you for providing the necessary details and passport documents for upgrading your mobile banking account. Your request has been received and is currently being processed by our customer support team. </p>
                                              <p>Should we require any additional information or documentation, we will reach out to you promptly. Otherwise, you can expect to receive confirmation of your upgraded mobile banking account within next 24 hours.
                                              For any feedback or enquiries, please call our Contact Center on 07004446147 or send an email to support@trustbancgroup.com.</p> 
                                              <p>Thank you for choosing TrustBanc J6 MfB</p>
                                              ";
                        _genServ.SendMail(sendMailObject);
                        /*
                        sendMailObject.Email = "opeyemi.adubiaro@trustbancgroup.com"; //for mgt
                        sendMailObject.Html = $@"This customer Mr/Mrs {firstname.ToUpper()} has just uploaded his/her credentials this day at {new DateTime()}.Kindly check and upgrade the account on her behalf";
                        _genServ.SendMail(sendMailObject);
                        */
                        sendMailObject.Email = _settings.CustomerServiceEmail; //for mgt
                        sendMailObject.Html = $@"This customer Mr/Mrs {firstname.ToUpper()} {usr.LastName.ToUpper()} with Account {customerDocuments.AccountNumberTobeUpgraded} has just uploaded his/her credentials this day at {new DateTime()}.Kindly check and upgrade the account on his/her behalf";
                        _genServ.SendMail(sendMailObject);
                    });
                    thread.Start();
                    return new GenericResponse() { Response = EnumResponse.Successful, Message = "All files uploaded successfully",Success=true };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> AddSignature(string clientKey, CustomerDocuments customerDocuments, IFormFile signature)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(customerDocuments.Username, customerDocuments.Session, customerDocuments.ChannelId, con);
                    if (!validateSession)
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidSession, Message = "Invalid Session" };
                    }
                    var myuserid = (await con.QueryAsync<string>("select userid from user_credentials where credentialtype=2 and status=1 and userid=(select id from users where Username=@Username)", new { username = customerDocuments.Username })).FirstOrDefault();
                    if (string.IsNullOrEmpty(myuserid))
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidUsernameOrPassword, Message = "Invalid Customer" };
                    }
                    CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con,int.Parse(myuserid));
                    string path = null;
                    if (_settings.FileUploadPath=="wwwroot")
                    {
                        path = "./" + _settings.FileUploadPath + "/";
                    }
                    else
                    {
                        path = _settings.FileUploadPath + "\\";
                    }
                    //string path = "./" + _settings.FileUploadPath + "/";
                    _logger.LogInformation("path " + path + " and _settings.FileUploadPath " + _settings.FileUploadPath, null);
                    Console.WriteLine("signature name " + Path.GetFileName(path));
                    _logger.LogInformation("signature name " + Path.GetFileName(path), null);
                    if (!signature.FileName.EndsWith(".jpeg") && !signature.FileName.EndsWith(".png") && !signature.FileName.EndsWith(".jpg"))
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidFileformat, Message = "All file must be in either .jpeg,png,jpg" };
                    }
                    int signatureresult = await processFileUpload("signature", myuserid, customerDocuments, con, signature, path + signature.FileName);
                    if (signatureresult==-1) {
                        return new GenericResponse() { Success = false, Response = EnumResponse.AllApprovedAll, Message = "already approved all" };
                    }
                    await _fileService.SaveFileAsync(signature, path); //save to external folder
                                                                       // send email .
                    var usr = await _genServ.GetUserbyUsername(customerDocuments.Username, con);
                    Thread thread = new Thread(async () =>
                    {
                        var firstname = (await con.QueryAsync<string>("select firstname from users where Username=@Username)", new { username = customerDocuments.Username })).FirstOrDefault();
                        SendMailObject sendMailObject = new SendMailObject();
                        sendMailObject.Subject = "Notification of Kyc Account Upgrade In Process";
                        sendMailObject.Email = customerDataNotFromBvn?.Email;
                        sendMailObject.Html = $@"Dear {firstname.ToUpper()}
                                              <p>Thank you for providing the necessary details and signature documents for upgrading your mobile banking account. Your request has been received and is currently being processed by our customer support team. </p>
                                              <p>Should we require any additional information or documentation, we will reach out to you promptly. Otherwise, you can expect to receive confirmation of your upgraded mobile banking account within next 24 hours.
                                              For any feedback or enquiries, please call our Contact Center on 07004446147 or send an email to support@trustbancgroup.com.</p> 
                                              <p>Thank you for choosing TrustBanc J6 MfB</p>
                                              ";
                        _genServ.SendMail(sendMailObject);
                        /*
                        sendMailObject.Email = "opeyemi.adubiaro@trustbancgroup.com"; //for mgt
                        sendMailObject.Html = $@"This customer Mr/Mrs {firstname.ToUpper()} has just uploaded his/her credentials this day at {new DateTime()}.Kindly check and upgrade the account on her behalf";
                        _genServ.SendMail(sendMailObject);
                        */
                        sendMailObject.Email = _settings.CustomerServiceEmail; //for mgt
                        sendMailObject.Html = $@"This customer Mr/Mrs {firstname.ToUpper()} {usr.LastName.ToUpper()} with Account {customerDocuments.AccountNumberTobeUpgraded} has just uploaded his/her credentials this day at {new DateTime()}.Kindly check and upgrade the account on his/her behalf";
                        _genServ.SendMail(sendMailObject);
                    });
                    thread.Start();
                    return new GenericResponse() { Response = EnumResponse.Successful, Message = "All files uploaded successfully" };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> KycPassportAcceptance(string clientKey, KycPassport kycStatus)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    if ((!kycStatus.PassportStatus.Equals("reject", StringComparison.CurrentCultureIgnoreCase)) && !kycStatus.PassportStatus.Equals("accept", StringComparison.CurrentCultureIgnoreCase))
                    {
                        return new GenericResponse() { Response = EnumResponse.RejectionReason, Success = true };
                    }
                    var usr = await _genServ.GetUserbyUsername(kycStatus.Username, con);
                    CustomerDataNotFromBvn customerDataNotFromBvn = (await con.QueryAsync<CustomerDataNotFromBvn>("select * from customerdatanotfrombvn where userid=@userid", new { userid = usr.Id })).FirstOrDefault();
                    if (kycStatus.PassportStatus.Equals("accept", StringComparison.CurrentCultureIgnoreCase))
                    {
                        // do update 
                        var checkedUserid = (await con.QueryAsync<string>(
                            "SELECT userid FROM kycdocumentstatus WHERE userid = @userid and typeofdocument='passport'",
                             new { userid = usr.Id})).FirstOrDefault();
                        if (string.IsNullOrEmpty(checkedUserid))
                        {
                            await con.ExecuteAsync(
                                "INSERT INTO kycdocumentstatus(userid,typeofdocument,actionid,passportstatus,purpose) VALUES(@userid,'passport',@actionid,@passportstatus,@purpose)",
                                new { userid = usr.Id,actionid=kycStatus.actionId, passportstatus=kycStatus.PassportStatus,purpose=kycStatus.RejectionReason});
                        }else
                          await con.ExecuteAsync("update kycdocumentstatus set passportapprovalstatus=false, passportstatus=@status,purpose=@purpose where userid=@usrId and typeofdocument='passport'", new { status = kycStatus.PassportStatus, purpose=kycStatus.RejectionReason, usrId = usr.Id});
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(kycStatus.RejectionReason)) {
                            return new GenericResponse() { Response = EnumResponse.RejectionReason, Success = true };
                        }
                        await con.ExecuteAsync("update kycdocumentstatus set passportapprovalstatus=false, passportstatus=@status,purpose=@purpose where userid=@usrId and typeofdocument='passport'", new {purpose=kycStatus.RejectionReason, status = kycStatus.PassportStatus, usrId = usr.Id });
                        Thread thread = new Thread(async () =>
                        {
                            //  var firstname = (await con.QueryAsync<string>("select firstname from users where Username=@Username)", new { Username = kycStatus.Username })).FirstOrDefault();
                            var firstname = (await con.QueryAsync<string>(
                                    "SELECT firstname FROM users WHERE Username = @Username",
                                    new { Username = kycStatus.Username }
                                )).FirstOrDefault();
                        });
                        thread.Start();
                    }
                    return new GenericResponse() { Response = EnumResponse.Successful, Success = true };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> KycUtlityBillAcceptance(string clientKey, KycUtlityBill kycStatus)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    
                    if ((!kycStatus.UtlityBillStatus.Equals("reject", StringComparison.CurrentCultureIgnoreCase)) && !kycStatus.UtlityBillStatus.Equals("accept", StringComparison.CurrentCultureIgnoreCase)) {
                        return new GenericResponse() { Response = EnumResponse.RejectionReason, Success = true };
                    }
                    
                    var usr = await _genServ.GetUserbyUsername(kycStatus.Username, con);
                    CustomerDataNotFromBvn customerDataNotFromBvn = (await con.QueryAsync<CustomerDataNotFromBvn>("select * from customerdatanotfrombvn where userid=@userid", new { userid = usr.Id })).FirstOrDefault();
                    if (kycStatus.UtlityBillStatus.Equals("accept", StringComparison.CurrentCultureIgnoreCase))
                    {
                        // do update
                        var checkedUserid = (await con.QueryAsync<string>(
                            "SELECT userid FROM kycdocumentstatus WHERE userid = @userid and typeofdocument='utilitybill'",
                             new { userid = usr.Id})).FirstOrDefault();
                        if (string.IsNullOrEmpty(checkedUserid))
                        {
                            await con.ExecuteAsync(
                                "INSERT INTO kycdocumentstatus(userid,typeofdocument,utlitybillstatus,actionid,purpose) VALUES(@userid,'utilitybill',@utlitybillstatus,@actionid,@purpose)",
                                new { userid = usr.Id, utlitybillstatus = kycStatus.UtlityBillStatus, actionid=kycStatus.actionId,purpose=kycStatus.RejectionReason});
                        }
                        else
                          await con.ExecuteAsync("update kycdocumentstatus set utilityapprovalstatus=false, purpose=@purpose,utlitybillstatus=@status where userid=@usrId and typeofdocument='utilitybill'", new { purpose=kycStatus.RejectionReason, status = kycStatus.UtlityBillStatus, usrId = usr.Id});
                    }
                    else {
                        if (string.IsNullOrEmpty(kycStatus.RejectionReason))
                        {
                            return new GenericResponse() { Response = EnumResponse.RejectionReason, Success = true };
                        }
                        await con.ExecuteAsync("update kycdocumentstatus set utilityapprovalstatus=false, purpose=@purpose,utlitybillstatus=@status where userid=@usrId and typeofdocument='utilitybill'", new { purpose = kycStatus.RejectionReason, status = kycStatus.UtlityBillStatus, usrId = usr.Id});
                        Thread thread = new Thread(async () =>
                        {
                            //var firstname = (await con.QueryAsync<string>("select firstname from users where Username=@Username)", new { Username = kycStatus.Username })).FirstOrDefault();
                            var firstname = (await con.QueryAsync<string>(
                                    "SELECT firstname FROM users WHERE Username = @Username",
                                    new { Username = kycStatus.Username }
                                )).FirstOrDefault();                         
                             });
                        thread.Start();
                        return new GenericResponse() { Response = EnumResponse.Successful, Success = true };
                    }
                    return new GenericResponse() { Response = EnumResponse.Successful, Success = true };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> KycSignatureAcceptance(string clientKey, KycSignature kycStatus)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    if ((!kycStatus.SignatureStatus.Equals("reject", StringComparison.CurrentCultureIgnoreCase)) && !kycStatus.SignatureStatus.Equals("accept", StringComparison.CurrentCultureIgnoreCase)) {
                        return new GenericResponse() { Response = EnumResponse.RejectionReason, Success = true };
                    }
                    var usr = await _genServ.GetUserbyUsername(kycStatus.Username, con);
                    CustomerDataNotFromBvn customerDataNotFromBvn = (await con.QueryAsync<CustomerDataNotFromBvn>("select * from customerdatanotfrombvn where userid=@userid", new { userid = usr.Id })).FirstOrDefault();
                    if (kycStatus.SignatureStatus.Equals("accept", StringComparison.CurrentCultureIgnoreCase))
                    {
                        // do update
                        var checkedUserid = (await con.QueryAsync<string>(
                            "SELECT userid FROM kycdocumentstatus WHERE userid = @userid and typeofdocument='signature'",
                             new { userid = usr.Id})).FirstOrDefault();
                        if (string.IsNullOrEmpty(checkedUserid))
                        {
                            await con.ExecuteAsync(
                                "INSERT INTO kycdocumentstatus(userid,signaturestatus,typeofdocument,purpose,actionid) VALUES(@userid,@signaturestatus,'signature',@purpose,@actionid)",
                                new { userid = usr.Id,signaturestatus=kycStatus.SignatureStatus,purpose=kycStatus.RejectionReason,actionid=kycStatus.actionid});
                        }else
                          await con.ExecuteAsync("update kycdocumentstatus set signatureapprovalstatus=false,signaturestatus=@status,purpose=@purpose where userid=@usrId and typeofdocument='signature'", new {purpose=kycStatus.RejectionReason,status = kycStatus.SignatureStatus, usrId = usr.Id });
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(kycStatus.RejectionReason))
                        {
                            return new GenericResponse() { Response = EnumResponse.RejectionReason, Success = true };
                        }
                        //await con.ExecuteAsync("update kycdocumentstatus set utlitybillstatus=@status where userid=@usrId", new { status = kycStatus.SignatureStatus, usrId = usr.Id });
                        await con.ExecuteAsync("update kycdocumentstatus set signatureapprovalstatus=false, signaturestatus=@status,purpose=@purpose where userid=@usrId and typeofdocument='signature'", new { purpose=kycStatus.RejectionReason, status = kycStatus.SignatureStatus, usrId = usr.Id});
                        Thread thread = new Thread(async () =>
                        {
                            //var firstname = (await con.QueryAsync<string>("select firstname from users where Username=@Username)", new { Username = kycStatus.Username })).FirstOrDefault();
                            var firstname = (await con.QueryAsync<string>(
                                        "SELECT firstname FROM users WHERE Username = @Username",
                                        new { Username = kycStatus.Username }
                                    )).FirstOrDefault();
                        });
                        thread.Start();
                        return new GenericResponse() { Response = EnumResponse.Successful, Success = true };
                    }
                    return new GenericResponse() { Response = EnumResponse.Successful, Success = true };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> KycIdCardAcceptance(string clientKey, KycidCard kycStatus)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    if ((!kycStatus.idCardStatus.Equals("reject", StringComparison.CurrentCultureIgnoreCase)) && !kycStatus.idCardStatus.Equals("accept", StringComparison.CurrentCultureIgnoreCase)) {
                        return new GenericResponse() { Response = EnumResponse.RejectionReason, Success = true };
                    }
                    var usr = await _genServ.GetUserbyUsername(kycStatus.Username, con);
                    CustomerDataNotFromBvn customerDataNotFromBvn = (await con.QueryAsync<CustomerDataNotFromBvn>("select * from customerdatanotfrombvn where userid=@userid", new { userid = usr.Id })).FirstOrDefault();
                    if (kycStatus.idCardStatus.Equals("accept", StringComparison.CurrentCultureIgnoreCase))
                    {
                        // do update
                        var checkedUserid = (await con.QueryAsync<string>(
                            "SELECT userid FROM kycdocumentstatus WHERE userid = @userid and typeofdocument='idcard'",
                             new { userid = usr.Id})).FirstOrDefault();
                        if (string.IsNullOrEmpty(checkedUserid))
                        {
                            await con.ExecuteAsync(
                                "INSERT INTO kycdocumentstatus(userid,typeofdocument,actionid,idcardstatus,purpose) VALUES(@userid,'idcard',@actionid,@idcardstatus,@purpose)",
                                new { userid = usr.Id, actionid= kycStatus.actionId, idcardstatus=kycStatus.idCardStatus, purpose=kycStatus.RejectionReason });
                        }else
                          await con.ExecuteAsync("update kycdocumentstatus set idcardapprovalstatus=false,purpose=@purpose,idcardstatus=@status where userid=@usrId and typeofdocument='idcard'", new { purpose=kycStatus.RejectionReason, status = kycStatus.idCardStatus, usrId = usr.Id });
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(kycStatus.RejectionReason))
                        {
                            return new GenericResponse() { Response = EnumResponse.RejectionReason, Success = true };
                        }
                        await con.ExecuteAsync("update kycdocumentstatus set idcardapprovalstatus=false,purpose=@purpose,idcardstatus=@status where userid=@usrId and typeofdocument='idcard'", new { purpose = kycStatus.RejectionReason, status = kycStatus.idCardStatus, usrId = usr.Id });
                        Thread thread = new Thread(async () =>
                        {
                            //var firstname = (await con.QueryAsync<string>("select firstname from users where Username=@Username)", new { Username = kycStatus.Username })).FirstOrDefault();
                            var firstname = (await con.QueryAsync<string>(
                                    "SELECT firstname FROM users WHERE Username = @Username",
                                    new { Username = kycStatus.Username }
                                )).FirstOrDefault();

                        });
                        thread.Start();
                        return new GenericResponse() { Response = EnumResponse.Successful, Success = true };
                    }
                    return new GenericResponse() { Response = EnumResponse.Successful, Success = true };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> KycStatus(string username)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var usr = await _genServ.GetUserbyUsername(username, con);
                    string utilityquery = "select utlitybillstatus as utilitystatus,utilityapprovalstatus from kycdocumentstatus where typeofdocument='utilitybill' and userid=@userid";
                    string pasportquery = "select passportstatus,passportapprovalstatus from kycdocumentstatus where typeofdocument='passport' and userid=@userid";
                    string idcardquery = "select idcardstatus,idcardapprovalstatus from kycdocumentstatus where typeofdocument='idcard' and userid=@userid";
                    string signaturequery = "select signaturestatus,signatureapprovalstatus from kycdocumentstatus where typeofdocument='signature' and userid=@userid";
                    IdCardKycStatus idCardKycStatus = (await con.QueryAsync<IdCardKycStatus>(idcardquery, new { userid = usr.Id })).FirstOrDefault();
                    PassportKycStatus passportKycStatus = (await con.QueryAsync<PassportKycStatus>(pasportquery, new { userid = usr.Id })).FirstOrDefault();
                    SignatureKycStatus signatureKycStatus = (await con.QueryAsync<SignatureKycStatus>(signaturequery, new { userid = usr.Id })).FirstOrDefault();
                    UtiityKycStatus utiityKycStatus = (await con.QueryAsync<UtiityKycStatus>(utilityquery, new { userid = usr.Id })).FirstOrDefault();
                    return new PrimeAdminResponse() { Response = EnumResponse.Successful, Success = true, Data = new { idcard=idCardKycStatus, passport=passportKycStatus, signature=signatureKycStatus, utilitybill=utiityKycStatus } };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse2> GetCustomerAccountLimit(string clientKey, string username, string Session, int ChannelId)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(username, Session, ChannelId, con);
                    if (!validateSession)
                    {
                        return new GenericResponse2() { Response = EnumResponse.InvalidSession, Message = "Invalid Session" };
                    }
                    var usr = await _genServ.GetUserbyUsername(username, con);
                    var customerAccountLimit = (await con.QueryAsync<CustomerAccountLimit>("select * from customerindemnity where userid=@userid", new { userid = usr.Id })).FirstOrDefault();
                    if (customerAccountLimit != null && customerAccountLimit.indemnityapproval) {
                        return new GenericResponse2() { Success = true, data = customerAccountLimit, Response = EnumResponse.Successful };
                    }
                    var customerAccountTransactionLimit = (await con.QueryAsync<CustomerAccountTransactionLimit>("select distinct * from customertransactionlimit where userid=@userid", new { userid = usr.Id })).FirstOrDefault();
                    if (customerAccountTransactionLimit != null)
                    {
                        return new GenericResponse2() { Success = true, data = customerAccountTransactionLimit, Response = EnumResponse.Successful };
                    }
                    BalanceEnquiryResponse balanceEnquiryResponse = await _genServ.GetAccountbyCustomerId(usr.CustomerId);
                    string kycTier = balanceEnquiryResponse.balances.ElementAtOrDefault(0).kycLevel;
                    if (kycTier.Equals(_tier1AccountLimitInfo.kycLevel, StringComparison.CurrentCultureIgnoreCase)) {
                        return new GenericResponse2() { Success = true, data = _tier1AccountLimitInfo, Response = EnumResponse.Successful };
                    } else if (kycTier.Equals(_tier2AccountLimitInfo.kycLevel, StringComparison.CurrentCultureIgnoreCase)) {
                        return new GenericResponse2() { Success = true, data = _tier2AccountLimitInfo, Response = EnumResponse.Successful };
                    } else if (kycTier.Equals(_tier3AccountLimitInfo.kycLevel, StringComparison.CurrentCultureIgnoreCase)) {
                        return new GenericResponse2() { Success = true, data = _tier3AccountLimitInfo, Response = EnumResponse.Successful };
                    }
                    return new GenericResponse2() { Success = true, data = null, Response = EnumResponse.Successful };
                }
            } catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
        }


        public async Task<GenericResponse2> BackOfficeIndemnityFormUploadForCustomer(string clientKey, BackofficeIndemnityForm customerDocuments, IFormFile indemnityform)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var myuserid = (await con.QueryAsync<string>("select userid from user_credentials where credentialtype=2 and status=1 and userid=(select id from users where Username=@Username)", new { username = customerDocuments.Username })).FirstOrDefault();
                    if (string.IsNullOrEmpty(myuserid))
                    {
                        return new GenericResponse2() { Response = EnumResponse.InvalidUsernameOrPassword, Message = "Invalid Customer Id" };
                    }
                    TransactionCappedLimit transactionCappedLimit = (await con.QueryAsync<TransactionCappedLimit>("select * from transactioncappedlimit where ApprovalStatus=true")).FirstOrDefault();
                    if (transactionCappedLimit==null)
                    {
                        return new GenericResponse2() { Response = EnumResponse.NotApprovetYet };
                    }
                    if (decimal.Parse(customerDocuments.Singlewithdrawaltransactionlimit)>transactionCappedLimit.SingleTransactionLimit)
                    {
                        return new GenericResponse2()
                        {
                            Success=false,Response=EnumResponse.SinglecummulativeTierLimitExceeded
                        };
                    }
                    if (decimal.Parse(customerDocuments.Dailywithdrawaltransactionlimit) > transactionCappedLimit.DailyCummulativeLimit)
                    {
                        return new GenericResponse2()
                        {
                            Success = false,
                            Response = EnumResponse.CappedcummulativeTierLimitExceeded
                        };
                    }
                    // check if the customer already has customer indemnity
                    if (customerDocuments.IndemnityType.Equals(_indemnityType.AccountIndemnityperAccount, StringComparison.CurrentCultureIgnoreCase))
                    {
                        var customerIndemnity = (await con.QueryAsync<IndemnityForm>("select * from customerindemnity where userid=@userid and IndemnityType=@IndemnityType", new { userid = myuserid, IndemnityType = _indemnityType.CustomerIndemnityAcrossAccount })).FirstOrDefault();
                        if (customerIndemnity != null)
                        {
                            // check accountindemnity 
                            var accountIndemnity = (await con.QueryAsync<IndemnityForm>("select * from customerindemnity where userid=@userid and AccountNumber=@AccountNumber", new { userid = myuserid, AccountNumber = customerDocuments.AccountNumber })).FirstOrDefault();
                            if (accountIndemnity != null)
                            {
                                if (accountIndemnity.Dailywithdrawaltransactionlimit > customerIndemnity.Dailywithdrawaltransactionlimit)
                                {
                                    return new GenericResponse2() { Response = EnumResponse.CustomerIndemnityOverRuleAccountIndemnity, Message = "customer overrule account indemnity" };
                                }
                                if (accountIndemnity.Singlewithdrawaltransactionlimit > customerIndemnity.Singlewithdrawaltransactionlimit)
                                {
                                    return new GenericResponse2() { Response = EnumResponse.SingleCustomerIndemnityOverRuleAccountIndemnity, Message = "customer overrule account indemnity" };
                                }

                            }
                        }
                    }
                    string path = _settings.IndemnityformPath;
                    _logger.LogInformation("path " + path + " and _settings.FileUploadPath " + _settings.FileUploadPath, null);
                    _logger.LogInformation("indemnityform name " + Path.GetFileName(path), null);
                    if (!indemnityform.FileName.EndsWith(".jpg") && !indemnityform.FileName.EndsWith(".jpeg") && !indemnityform.FileName.EndsWith(".png") && !indemnityform.FileName.EndsWith(".pdf"))
                    {
                        return new GenericResponse2() { Response = EnumResponse.InvalidFileformat, Message = "All file must be in either jpg,.jpeg,png,pdf" };
                    }
                    if (customerDocuments.IndemnityType.Equals(_indemnityType.AccountIndemnityperAccount, StringComparison.CurrentCultureIgnoreCase))
                    {
                        myuserid = (await con.QueryAsync<string>("select userid from customerindemnity where userid=@userid and AccountNumber=@AccountNumber and IndemnityType=@IndemnityType", new { userid = myuserid, AccountNumber = customerDocuments.AccountNumber, IndemnityType = _indemnityType.AccountIndemnityperAccount })).FirstOrDefault();
                    }
                    else if (customerDocuments.IndemnityType.Equals(_indemnityType.CustomerIndemnityAcrossAccount, StringComparison.CurrentCultureIgnoreCase))
                    {
                        myuserid = (await con.QueryAsync<string>("select userid from customerindemnity where userid=@userid and IndemnityType=@IndemnityType", new { userid = myuserid, IndemnityType = _indemnityType.CustomerIndemnityAcrossAccount })).FirstOrDefault();
                    }
                    var usr = (await _genServ.GetUserbyUsername(customerDocuments.Username, con));
                    BalanceEnquiryResponse balanceEnquiryResponse = await _genServ.GetAccountbyCustomerId(usr.CustomerId);
                   // string kyclevel = balanceEnquiryResponse.balances.ElementAtOrDefault(0).kycLevel;
                    string kyclevel;
                    if (customerDocuments.IndemnityType.Equals(_indemnityType.AccountIndemnityperAccount, StringComparison.CurrentCultureIgnoreCase))
                    {
                        var matchingAccount = balanceEnquiryResponse.balances
                            .Where(e => e.accountNumber.Equals(customerDocuments.AccountNumber))
                            .ToList();
                        _logger.LogInformation("in if " + JsonConvert.SerializeObject(matchingAccount));
                        if (matchingAccount != null && matchingAccount.Any())
                        {
                            kyclevel = matchingAccount[0].kycLevel;
                        }
                        else
                        {
                            kyclevel = null; // No matching account found.
                        }
                    }
                    else
                    {
                        _logger.LogInformation("else " + balanceEnquiryResponse.balances);
                        if (balanceEnquiryResponse.balances != null && balanceEnquiryResponse.balances.Any())
                        {
                            kyclevel = balanceEnquiryResponse.balances.ElementAtOrDefault(0).kycLevel; // Check and retrieve the first element.
                        }
                        else
                        {
                            kyclevel = null; // No balances available.
                        }
                    }
                    if (kyclevel == null)
                    {
                        return new GenericResponse2() { Response = EnumResponse.InvalidAccount };
                    }
                    if (!kyclevel.Equals(_tier3AccountLimitInfo.kycLevel, StringComparison.CurrentCultureIgnoreCase))
                    {
                        return new GenericResponse2() { Response = EnumResponse.AccountLessThanTier3 };
                    }
                    CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)usr.Id);
                    _logger.LogInformation("path " + Path.Combine(path, indemnityform.FileName));
                    customerDocuments.indemnityformpath = Path.Combine(path, indemnityform.FileName);
                    if (string.IsNullOrEmpty(myuserid) && !string.IsNullOrEmpty(customerDocuments.AccountNumber) && customerDocuments.IndemnityType.Equals(_indemnityType.AccountIndemnityperAccount, StringComparison.CurrentCultureIgnoreCase)) // if it is new
                    {
                        await con.ExecuteAsync($@"insert into
                                             customerindemnity(userid,createdAt,accounttier,indemnityformpath,requestpurpose,
                                             Singledeposittransactionlimit,
                                             Dailydeposittransactionlimit,
                                             Singlewithdrawaltransactionlimit,
                                             Dailywithdrawaltransactionlimit,AccountNumber)
                                             values(@userid,@createdAt,@accounttier,@indemnityformpath,
                                              @requestpurpose
                                             ,@Singledeposittransactionlimit
                                             ,@Dailydeposittransactionlimit,
                                              @Singlewithdrawaltransactionlimit,
                                              @Dailywithdrawaltransactionlimit,@AccountNumber)",
                                                                    new
                                                                    {
                                                                        userid = usr.Id,
                                                                        createdAt = DateTime.Now,
                                                                        accounttier = customerDocuments.accounttier,
                                                                        indemnityformpath = Path.Combine(path, indemnityform.FileName),
                                                                        requestpurpose = customerDocuments.requestpurpose,
                                                                        Singledeposittransactionlimit = customerDocuments.Singledeposittransactionlimit,
                                                                        Dailydeposittransactionlimit = customerDocuments.Dailydeposittransactionlimit,
                                                                        Singlewithdrawaltransactionlimit = decimal.Parse(customerDocuments.Singlewithdrawaltransactionlimit),
                                                                        Dailywithdrawaltransactionlimit = decimal.Parse(customerDocuments.Dailywithdrawaltransactionlimit),
                                                                        AccountNumber = customerDocuments.AccountNumber
                                                                    });
                        await _fileService.SaveFileAsync(indemnityform, path); // save to external folder
                    }
                    else if (!string.IsNullOrEmpty(myuserid) && !string.IsNullOrEmpty(customerDocuments.AccountNumber) && customerDocuments.IndemnityType.Equals(_indemnityType.AccountIndemnityperAccount, StringComparison.CurrentCultureIgnoreCase))
                    {
                        //update the indemnity for the particular account
                        var customerIndemnity = (await con.QueryAsync<IndemnityForm>("select * from customerindemnity where userid=@userid and AccountNumber=@AccountNumber", new { userid = myuserid, AccountNumber = customerDocuments.AccountNumber })).FirstOrDefault();
                        // remove formal indemnityform path from system path
                        if (System.IO.File.Exists(customerIndemnity.indemnityformpath))
                        {
                            System.IO.File.Delete(customerIndemnity.indemnityformpath);
                        }
                        await con.ExecuteAsync($@" 
                                             update customerindemnity set createdAt=@createdAt,accounttier=@accounttier,indemnityformpath=@indemnityformpath,requestpurpose=@requestpurpose,
                                             Singledeposittransactionlimit=@Singledeposittransactionlimit,
                                             Dailydeposittransactionlimit=@Dailydeposittransactionlimit,
                                             Singlewithdrawaltransactionlimit=@Singlewithdrawaltransactionlimit,
                                             Dailywithdrawaltransactionlimit=@Dailywithdrawaltransactionlimit,
                                             indemnitystatus='Awaiting review',
                                             indemnityapproval=false,
                                             initiated=true
                                             where userid=@userid and AccountNumber=@AccountNumber and IndemnityType='accountindemnity'",
                                                                         new
                                                                         {
                                                                             createdAt = DateTime.Now,
                                                                             accounttier = customerDocuments.accounttier,
                                                                             indemnityformpath = Path.Combine(path, indemnityform.FileName),
                                                                             requestpurpose = customerDocuments.requestpurpose,
                                                                             Singledeposittransactionlimit = customerDocuments.Singledeposittransactionlimit,
                                                                             Dailydeposittransactionlimit = customerDocuments.Dailydeposittransactionlimit,
                                                                             Singlewithdrawaltransactionlimit = decimal.Parse(customerDocuments.Singlewithdrawaltransactionlimit),
                                                                             Dailywithdrawaltransactionlimit = decimal.Parse(customerDocuments.Dailywithdrawaltransactionlimit),
                                                                             userid = usr.Id,
                                                                             AccountNumber = customerDocuments.AccountNumber
                                                                         });
                        await _fileService.SaveFileAsync(indemnityform, path);
                    }
                    else if (string.IsNullOrEmpty(myuserid) && string.IsNullOrEmpty(customerDocuments.AccountNumber) && customerDocuments.IndemnityType.Equals(_indemnityType.CustomerIndemnityAcrossAccount, StringComparison.CurrentCultureIgnoreCase))
                    {
                        // insert afresh
                        await con.ExecuteAsync($@"insert into
                                             customerindemnity(userid,createdAt,accounttier,indemnityformpath,requestpurpose,
                                             Singledeposittransactionlimit,
                                             Dailydeposittransactionlimit,
                                             Singlewithdrawaltransactionlimit,
                                             Dailywithdrawaltransactionlimit,IndemnityType)
                                             values(@userid,@createdAt,@accounttier,@indemnityformpath,
                                              @requestpurpose
                                             ,@Singledeposittransactionlimit
                                             ,@Dailydeposittransactionlimit,
                                              @Singlewithdrawaltransactionlimit,
                                              @Dailywithdrawaltransactionlimit,@IndemnityType)",
                                                                      new
                                                                      {
                                                                          userid = usr.Id,
                                                                          createdAt = DateTime.Now,
                                                                          accounttier = customerDocuments.accounttier,
                                                                          indemnityformpath = Path.Combine(path, indemnityform.FileName),
                                                                          requestpurpose = customerDocuments.requestpurpose,
                                                                          Singledeposittransactionlimit = customerDocuments.Singledeposittransactionlimit,
                                                                          Dailydeposittransactionlimit = customerDocuments.Dailydeposittransactionlimit,
                                                                          Singlewithdrawaltransactionlimit =decimal.Parse(customerDocuments.Singlewithdrawaltransactionlimit),
                                                                          Dailywithdrawaltransactionlimit = decimal.Parse(customerDocuments.Dailywithdrawaltransactionlimit),
                                                                          IndemnityType = _indemnityType.CustomerIndemnityAcrossAccount
                                                                      });
                        await _fileService.SaveFileAsync(indemnityform, path); // save to external folder
                    }
                    else if (!string.IsNullOrEmpty(myuserid) && string.IsNullOrEmpty(customerDocuments.AccountNumber) && customerDocuments.IndemnityType.Equals(_indemnityType.CustomerIndemnityAcrossAccount, StringComparison.CurrentCultureIgnoreCase))
                    {
                        // update afresh
                        //update the indemnity for the particular account
                        var customerIndemnity = (await con.QueryAsync<IndemnityForm>("select * from customerindemnity where userid=@userid and IndemnityType=@IndemnityType", new { userid = myuserid, IndemnityType = _indemnityType.CustomerIndemnityAcrossAccount })).FirstOrDefault();
                        // remove formal indemnityform path from system path
                        if (System.IO.File.Exists(customerIndemnity.indemnityformpath))
                        {
                            System.IO.File.Delete(customerIndemnity.indemnityformpath);
                        }
                        await con.ExecuteAsync($@" 
                                             update customerindemnity set createdAt=@createdAt,accounttier=@accounttier,indemnityformpath=@indemnityformpath,requestpurpose=@requestpurpose,
                                             Singledeposittransactionlimit=@Singledeposittransactionlimit,
                                             Dailydeposittransactionlimit=@Dailydeposittransactionlimit,
                                             Singlewithdrawaltransactionlimit=@Singlewithdrawaltransactionlimit,
                                             Dailywithdrawaltransactionlimit=@Dailywithdrawaltransactionlimit,
                                             indemnitystatus='Awaiting review',
                                             indemnityapproval=false,
                                             initiated=true
                                             where userid=@userid and IndemnityType=@IndemnityType",
                                                                         new
                                                                         {
                                                                             createdAt = DateTime.Now,
                                                                             accounttier = customerDocuments.accounttier,
                                                                             indemnityformpath = Path.Combine(path, indemnityform.FileName),
                                                                             requestpurpose = customerDocuments.requestpurpose,
                                                                             Singledeposittransactionlimit = customerDocuments.Singledeposittransactionlimit,
                                                                             Dailydeposittransactionlimit = customerDocuments.Dailydeposittransactionlimit,
                                                                             Singlewithdrawaltransactionlimit = decimal.Parse(customerDocuments.Singlewithdrawaltransactionlimit),
                                                                             Dailywithdrawaltransactionlimit = decimal.Parse(customerDocuments.Dailywithdrawaltransactionlimit),
                                                                             userid = usr.Id,
                                                                             IndemnityType = _indemnityType.CustomerIndemnityAcrossAccount
                                                                         });
                        await _fileService.SaveFileAsync(indemnityform, path);
                    }
                    else
                    {
                        return new GenericResponse2() { Response = EnumResponse.WrongAction };
                    }
                    // send email .
                    Thread thread = new Thread(async () =>
                    {
                        // var firstname = (await con.QueryAsync<string>("select firstname from users where username=@Username)", new { Username = customerDocuments.Username })).FirstOrDefault();
                        var firstname = (await con.QueryAsync<string>(
                                    "SELECT firstname FROM users WHERE username = @Username",
                                    new { Username = customerDocuments.Username })
                                ).FirstOrDefault();
                        SendMailObject sendMailObject = new SendMailObject();
                        sendMailObject.Subject = "Customer Indemnity";
                        sendMailObject.Email = customerDataNotFromBvn?.Email;
                        sendMailObject.Html = $@"<p>Dear {firstname.ToUpper()} {usr.LastName}</p>
                                              <p>Your indemnity request has been received and is currently being processed by our customer support team.</p>
                                              <p>Should we require any additional information or documentation, we will reach out to you promptly.Otherwise, you can expect to receive confirmation of your upgraded mobile banking account within next 24 hours.</p>
                                              <p>For any feedback or enquiries, please call our Contact Center on 07004446147 or send an email to support@trustbancgroup.com.</p> 
                                              <p>Thank you for choosing TrustBanc J6 MfB</p>
                                              ";
                        _genServ.SendMail(sendMailObject);
                        sendMailObject.Email = _settings.CustomerServiceEmail; //for mgt
                        sendMailObject.Html = $@"
                                               <p>Dear Customer Service</p>
                                              <p>Indemnity form has been uploaded this day at {new DateTime()} for the customer Mr/Mrs {firstname.ToUpper()} {usr.LastName.ToUpper()}.</p><p>Please proceed to process it.</p>";
                        _genServ.SendMail(sendMailObject);
                    });
                    thread.Start();
                    return new GenericResponse2() { Success = true, Response = EnumResponse.Successful, Message = "All files uploaded successfully" };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
        }


        public async Task<GenericResponse2> UploadIndemnityForm(string clientKey, IndemnityForm customerDocuments, IFormFile indemnityform)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(customerDocuments.Username, customerDocuments.Session, customerDocuments.ChannelId, con);
                    if (!validateSession)
                    {
                        return new GenericResponse2() { Response = EnumResponse.InvalidSession, Message = "Invalid Session" };
                    }
                    var myuserid = (await con.QueryAsync<string>("select userid from user_credentials where credentialtype=2 and status=1 and userid=(select id from users where Username=@Username)", new { username = customerDocuments.Username })).FirstOrDefault();
                    if (string.IsNullOrEmpty(myuserid))
                    {
                        return new GenericResponse2() { Response = EnumResponse.InvalidUsernameOrPassword, Message = "Invalid Customer Id" };
                    }
                    string path = _settings.IndemnityformPath;
                    _logger.LogInformation("path " + path + " and _settings.FileUploadPath " + _settings.FileUploadPath, null);
                    _logger.LogInformation("indemnityform name " + Path.GetFileName(path), null);
                    if (!indemnityform.FileName.EndsWith(".jpeg") && !indemnityform.FileName.EndsWith(".png") && !indemnityform.FileName.EndsWith(".pdf"))
                    {
                        return new GenericResponse2() { Response = EnumResponse.InvalidFileformat, Message = "All file must be in either .jpeg,png,pdf" };
                    }
                    myuserid = (await con.QueryAsync<string>("select userid from customerindemnity where userid=@userid and AccountNumber=@AccountNumber", new { userid = myuserid, AccountNumber=customerDocuments.AccountNumber})).FirstOrDefault();

                    //if (!string.IsNullOrEmpty(myuserid))
                    //{
                      //  await con.ExecuteAsync("delete * from customerindemnity where userid=@userid", new { userid = myuserid });
                    //}
                    var usr = (await _genServ.GetUserbyUsername(customerDocuments.Username, con));
                    BalanceEnquiryResponse balanceEnquiryResponse = await _genServ.GetAccountbyCustomerId(usr.CustomerId);
                    string kyclevel = balanceEnquiryResponse.balances.ElementAtOrDefault(0).kycLevel;
                    if (!kyclevel.Equals(_tier3AccountLimitInfo.kycLevel,StringComparison.CurrentCultureIgnoreCase))
                    {
                        return new GenericResponse2() { Response = EnumResponse.AccountLessThanTier3 };
                    }
                    CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)usr.Id);
                    string otp = await _redisStorageService.GetCustomerAsync($"otp_{customerDataNotFromBvn.PhoneNumber}");
                    OtpTransLimit otpTransLimit = JsonConvert.DeserializeObject<OtpTransLimit>(otp);
                    if(customerDocuments.Otp.Equals(otpTransLimit.otp))
                    {
                        return new GenericResponse2() { Response = EnumResponse.InvalidOtp };
                    }
                    DateTime parseddateTime = DateTime.Parse(otpTransLimit.DateTimeString);
                    _logger.LogInformation("dateTimeString " + otpTransLimit.DateTimeString);
                    DateTime dateTime = DateTime.Now;
                    // Calculate the difference
                    TimeSpan difference = dateTime - parseddateTime;
                    if (Math.Abs(difference.TotalMinutes) >= 3)
                    {
                        return new GenericResponse2() { Response = EnumResponse.OtpTimeOut, Success = false };
                    }
                    /*
                    if (!customerDocuments.Otp.Equals(otp, StringComparison.CurrentCulture))
                    {
                        return new GenericResponse2() { Response = EnumResponse.InvalidOtp };
                    }
                    */
                    _logger.LogInformation("path " + Path.Combine(path, indemnityform.FileName));
                    if (string.IsNullOrEmpty(myuserid) && !string.IsNullOrEmpty(customerDocuments.AccountNumber) && customerDocuments.IndemnityType.Equals(_indemnityType.AccountIndemnityperAccount,StringComparison.CurrentCultureIgnoreCase)) // if it is new
                    {
                        await con.ExecuteAsync($@"insert into
                                             customerindemnity(userid,createdAt,accounttier,indemnityformpath,requestpurpose,
                                             Singledeposittransactionlimit,
                                             Dailydeposittransactionlimit,
                                             Singlewithdrawaltransactionlimit,
                                             Dailywithdrawaltransactionlimit,AccountNumber)
                                             values(@userid,@createdAt,@accounttier,@indemnityformpath,
                                              @requestpurpose
                                             ,@Singledeposittransactionlimit
                                             ,@Dailydeposittransactionlimit,
                                              @Singlewithdrawaltransactionlimit,
                                              @Dailywithdrawaltransactionlimit,@AccountNumber)",
                                                                    new
                                                                    {
                                                                        userid = usr.Id,
                                                                        createdAt = DateTime.Now,
                                                                        accounttier = customerDocuments.accounttier,
                                                                        indemnityformpath = Path.Combine(path, indemnityform.FileName),
                                                                        requestpurpose = customerDocuments.requestpurpose,
                                                                        Singledeposittransactionlimit = customerDocuments.Singledeposittransactionlimit,
                                                                        Dailydeposittransactionlimit = customerDocuments.Dailydeposittransactionlimit,
                                                                        Singlewithdrawaltransactionlimit = customerDocuments.Singlewithdrawaltransactionlimit,
                                                                        Dailywithdrawaltransactionlimit = customerDocuments.Dailywithdrawaltransactionlimit,
                                                                        AccountNumber=customerDocuments.AccountNumber
                                                                    });
                        await _fileService.SaveFileAsync(indemnityform, path); // save to external folder
                    }
                    else if(!string.IsNullOrEmpty(myuserid) && !string.IsNullOrEmpty(customerDocuments.AccountNumber) && customerDocuments.IndemnityType.Equals(_indemnityType.AccountIndemnityperAccount, StringComparison.CurrentCultureIgnoreCase))
                    {
                        //update the indemnity for the particular account
                      var customerIndemnity = (await con.QueryAsync<IndemnityForm>("select * from customerindemnity where userid=@userid and AccountNumber=@AccountNumber", new { userid = myuserid, AccountNumber = customerDocuments.AccountNumber })).FirstOrDefault();
                        // remove formal indemnityform path from system path
                        if (System.IO.File.Exists(customerIndemnity.indemnityformpath))
                        {
                            System.IO.File.Delete(customerIndemnity.indemnityformpath);
                        }           
                      await con.ExecuteAsync($@" 
                                             update customerindemnity set createdAt=@createdAt,accounttier=@accounttier,indemnityformpath=@indemnityformpath,requestpurpose=@requestpurpose,
                                             Singledeposittransactionlimit=@Singledeposittransactionlimit,
                                             Dailydeposittransactionlimit=@Dailydeposittransactionlimit,
                                             Singlewithdrawaltransactionlimit=@Singlewithdrawaltransactionlimit,
                                             Dailywithdrawaltransactionlimit=@Dailywithdrawaltransactionlimit where userid=@userid and AccountNumber=@AccountNumber",
                                                                       new
                                                                       {
                                                                           createdAt = DateTime.Now,
                                                                           accounttier = customerDocuments.accounttier,
                                                                           indemnityformpath = Path.Combine(path, indemnityform.FileName),
                                                                           requestpurpose = customerDocuments.requestpurpose,
                                                                           Singledeposittransactionlimit = customerDocuments.Singledeposittransactionlimit,
                                                                           Dailydeposittransactionlimit = customerDocuments.Dailydeposittransactionlimit,
                                                                           Singlewithdrawaltransactionlimit = customerDocuments.Singlewithdrawaltransactionlimit,
                                                                           Dailywithdrawaltransactionlimit = customerDocuments.Dailywithdrawaltransactionlimit,
                                                                           userid=usr.Id,
                                                                           AccountNumber = customerDocuments.AccountNumber
                                                                       });
                        await _fileService.SaveFileAsync(indemnityform,path);
                    }else if(string.IsNullOrEmpty(myuserid) && string.IsNullOrEmpty(customerDocuments.AccountNumber) && customerDocuments.IndemnityType.Equals(_indemnityType.CustomerIndemnityAcrossAccount, StringComparison.CurrentCultureIgnoreCase))
                    {
                        // insert afresh
                        await con.ExecuteAsync($@"insert into
                                             customerindemnity(userid,createdAt,accounttier,indemnityformpath,requestpurpose,
                                             Singledeposittransactionlimit,
                                             Dailydeposittransactionlimit,
                                             Singlewithdrawaltransactionlimit,
                                             Dailywithdrawaltransactionlimit,IndemnityType)
                                             values(@userid,@createdAt,@accounttier,@indemnityformpath,
                                              @requestpurpose
                                             ,@Singledeposittransactionlimit
                                             ,@Dailydeposittransactionlimit,
                                              @Singlewithdrawaltransactionlimit,
                                              @Dailywithdrawaltransactionlimit,@IndemnityType)",
                                                                      new
                                                                      {
                                                                          userid = usr.Id,
                                                                          createdAt = DateTime.Now,
                                                                          accounttier = customerDocuments.accounttier,
                                                                          indemnityformpath = Path.Combine(path, indemnityform.FileName),
                                                                          requestpurpose = customerDocuments.requestpurpose,
                                                                          Singledeposittransactionlimit = customerDocuments.Singledeposittransactionlimit,
                                                                          Dailydeposittransactionlimit = customerDocuments.Dailydeposittransactionlimit,
                                                                          Singlewithdrawaltransactionlimit = customerDocuments.Singlewithdrawaltransactionlimit,
                                                                          Dailywithdrawaltransactionlimit = customerDocuments.Dailywithdrawaltransactionlimit,
                                                                          IndemnityType=_indemnityType.CustomerIndemnityAcrossAccount
                                                                      });
                        await _fileService.SaveFileAsync(indemnityform, path); // save to external folder
                    }
                    else if (!string.IsNullOrEmpty(myuserid) && string.IsNullOrEmpty(customerDocuments.AccountNumber) && customerDocuments.IndemnityType.Equals(_indemnityType.CustomerIndemnityAcrossAccount, StringComparison.CurrentCultureIgnoreCase))
                    {
                        // update afresh
                        //update the indemnity for the particular account
                        var customerIndemnity = (await con.QueryAsync<IndemnityForm>("select * from customerindemnity where userid=@userid and AccountNumber=@AccountNumber", new { userid = myuserid, AccountNumber = customerDocuments.AccountNumber })).FirstOrDefault();
                        // remove formal indemnityform path from system path
                        if (System.IO.File.Exists(customerIndemnity.indemnityformpath))
                        {
                            System.IO.File.Delete(customerIndemnity.indemnityformpath);
                        }
                        await con.ExecuteAsync($@" 
                                             update customerindemnity set createdAt=@createdAt,accounttier=@accounttier,indemnityformpath=@indemnityformpath,requestpurpose=@requestpurpose,
                                             Singledeposittransactionlimit=@Singledeposittransactionlimit,
                                             Dailydeposittransactionlimit=@Dailydeposittransactionlimit,
                                             Singlewithdrawaltransactionlimit=@Singlewithdrawaltransactionlimit,
                                             Dailywithdrawaltransactionlimit=@Dailywithdrawaltransactionlimit where userid=@userid and IndemnityType=@IndemnityType",
                                                                         new
                                                                         {
                                                                             createdAt = DateTime.Now,
                                                                             accounttier = customerDocuments.accounttier,
                                                                             indemnityformpath = Path.Combine(path, indemnityform.FileName),
                                                                             requestpurpose = customerDocuments.requestpurpose,
                                                                             Singledeposittransactionlimit = customerDocuments.Singledeposittransactionlimit,
                                                                             Dailydeposittransactionlimit = customerDocuments.Dailydeposittransactionlimit,
                                                                             Singlewithdrawaltransactionlimit = customerDocuments.Singlewithdrawaltransactionlimit,
                                                                             Dailywithdrawaltransactionlimit = customerDocuments.Dailywithdrawaltransactionlimit,
                                                                             userid = usr.Id,
                                                                             IndemnityType = _indemnityType.CustomerIndemnityAcrossAccount
                                                                         });
                        await _fileService.SaveFileAsync(indemnityform, path);
                    }
                    else
                    {
                        return new GenericResponse2() {Response = EnumResponse.WrongAction};
                    }
                    // send email .
                    Thread thread = new Thread(async () =>
                    {
                        // var firstname = (await con.QueryAsync<string>("select firstname from users where username=@Username)", new { Username = customerDocuments.Username })).FirstOrDefault();
                        var firstname = (await con.QueryAsync<string>(
                                    "SELECT firstname FROM users WHERE username = @Username",
                                    new { Username = customerDocuments.Username })
                                ).FirstOrDefault();
                        SendMailObject sendMailObject = new SendMailObject();
                        sendMailObject.Subject = "Customer Indemnity";
                        sendMailObject.Email = customerDataNotFromBvn?.Email;
                        sendMailObject.Html = $@"Dear {firstname.ToUpper()}
                                              <p>Thank you for providing the necessary details and documents for customer indemnity form. Your request has been received and is currently being processed by our customer support team. </p>
                                              <p>Should we require any additional information or documentation, we will reach out to you promptly. Otherwise, you can expect to receive confirmation of your upgraded mobile banking account within next 24 hours.
                                              For any feedback or enquiries, please call our Contact Center on 07004446147 or send an email to support@trustbancgroup.com.</p> 
                                              <p>Thank you for choosing TrustBanc J6 MfB</p>
                                              ";
                        _genServ.SendMail(sendMailObject);
                        sendMailObject.Email = _settings.CustomerServiceEmail; //for mgt
                        sendMailObject.Html = $@"This customer Mr/Mrs {firstname.ToUpper()} {usr.LastName.ToUpper()} has just uploaded his/her Indemnity form this day at {new DateTime()}.Kindly check and process on his/her behalf";
                        _genServ.SendMail(sendMailObject);
                    });
                    thread.Start();
                //send mail to customerservice and operation for indemnity form attachement of customer
                    Task.Run(async () => {
                        CustomerDetails customerDetails = new CustomerDetails();
                        customerDetails.FirstName = usr.Firstname;
                        customerDetails.LastName = usr.LastName;
                        customerDetails.PhoneNumber = usr.PhoneNumber;
                        customerDetails.Email = usr.Email;
                        customerDetails.CustomerServiceEmail = _settings.CustomerServiceEmail;
                        customerDetails.OperationEmail = _settings.OperationEmail;
                        string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.AccountUrl + "api/Account/SendIndemnityWithAttachmentToCustomerService", customerDetails, true);
                        //_logger.LogInformation("response " + response);
                        GenericResponse2 mailnotification = JsonConvert.DeserializeObject<GenericResponse2>(response);
                        _logger.LogInformation("mailnotification " + JsonConvert.SerializeObject(mailnotification));
                    });
                    return new GenericResponse2() { Success = true, Response = EnumResponse.Successful, Message = "All files uploaded successfully" };
                }
            } catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse2> CustomerTransactionLimit(string clientKey, CustomerAccountTransactionLimit customerAccountLimit)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    /*
                    var validateSession = await _genServ.ValidateSession(customerAccountLimit.username,customerAccountLimit.Session,customerAccountLimit.ChannelId, con);
                    if (!validateSession)
                    {
                        return new GenericResponse2() { Response = EnumResponse.InvalidSession, Message = "Invalid Session" };
                    }
                    */
                    var usr = await _genServ.GetUserbyUsername(customerAccountLimit.username,con);
                    if (usr==null)
                    {
                        return new GenericResponse2() { Response = EnumResponse.UsernameNotFound };
                    }
                    //validate otp
                    CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)usr.Id);
                    string otp = await _redisStorageService.GetCustomerAsync($"otp_{customerDataNotFromBvn.PhoneNumber}");
                    // customerAccountLimit.Otp = _genServ.RemoveSpecialCharacters(customerAccountLimit.Otp);
                    Console.WriteLine("otp "+otp);
                    OtpTransLimit otpTransLimit = JsonConvert.DeserializeObject<OtpTransLimit>(otp);
                    //compare otp 
                    if (!otpTransLimit.otp.Equals(customerAccountLimit.Otp))
                    {
                        return new GenericResponse2() { Response = EnumResponse.InvalidOtp, Success = false };
                    }
                    //string dateTimeString = otp.Split('-')[1] ; // Your date string
                    DateTime parseddateTime = DateTime.Parse(otpTransLimit.DateTimeString);
                    Console.WriteLine(parseddateTime); // Outputs: 10/30/2024 12:15:00 PM
                    _logger.LogInformation("dateTimeString " + otpTransLimit.DateTimeString);
                    DateTime dateTime = DateTime.Now;
                    // Calculate the difference
                    TimeSpan difference = dateTime - parseddateTime;
                    // Check if the difference is not greater than 3 minutes
                    if (Math.Abs(difference.TotalMinutes) >= 3)
                    {
                        return new GenericResponse2() { Response = EnumResponse.OtpTimeOut, Success = false };
                    }
                    BalanceEnquiryResponse balanceEnquiryResponse = await _genServ.GetAccountbyCustomerId(usr.CustomerId);
                    string kyclevel = balanceEnquiryResponse.balances.ElementAtOrDefault(0).kycLevel;
                    if (kyclevel.Equals("001",StringComparison.CurrentCultureIgnoreCase))
                    {
                        if (customerAccountLimit.Dailywithdrawaltransactionlimit > decimal.Parse(_customerChannelLimit.DailyTransactionWithdrawalLimit))
                        {
                            return new GenericResponse2() { Success = false, Response = EnumResponse.TransactionLimitOutOfRange };
                        }
                        else if (customerAccountLimit.Singlewithdrawaltransactionlimit > decimal.Parse(_customerChannelLimit.SingleTransactionWithdrawalLimit))
                        {
                            return new GenericResponse2() { Success = false, Response = EnumResponse.TransactionLimitOutOfRange };
                        }
                    }
                    else if (kyclevel.Equals("002", StringComparison.CurrentCultureIgnoreCase))
                    {
                        if (customerAccountLimit.Dailywithdrawaltransactionlimit > decimal.Parse(_customerChannelLimit.DailyTransactionWithdrawalLimit))
                        {
                            return new GenericResponse2() { Success = false, Response = EnumResponse.TransactionLimitOutOfRange };
                        }
                        else if (customerAccountLimit.Singlewithdrawaltransactionlimit > decimal.Parse(_customerChannelLimit.SingleTransactionWithdrawalLimit))
                        {
                            return new GenericResponse2() { Success = false, Response = EnumResponse.TransactionLimitOutOfRange };
                        }
                    }
                    else if (kyclevel.Equals("003", StringComparison.CurrentCultureIgnoreCase))
                    {
                        if (customerAccountLimit.Dailywithdrawaltransactionlimit > decimal.Parse(_customerChannelLimit.DailyTransactionWithdrawalLimit))
                        {
                            return new GenericResponse2() { Success = false, Response = EnumResponse.TransactionLimitOutOfRange };
                        }
                        else if (customerAccountLimit.Singlewithdrawaltransactionlimit > decimal.Parse(_customerChannelLimit.SingleTransactionWithdrawalLimit))
                        {
                            return new GenericResponse2() { Success = false, Response = EnumResponse.TransactionLimitOutOfRange };
                        }
                    }
                    var CustomerIdInCheckLimit = (await con.QueryAsync<CustomerAccountTransactionLimit>("select * from customertransactionlimit where userid=@userid and AccountNumber=@AccountNumber and LimitType=@LimitType", new {userid=usr.Id,AccountNumber=customerAccountLimit.AccountNumber, LimitType=_accountLimitType.AccountLimitPerAccount})).FirstOrDefault();
                    var CustomerLimitacrossAccount = (await con.QueryAsync<CustomerAccountTransactionLimit>("select * from customertransactionlimit where userid=@userid and LimitType=@LimitType", new { userid = usr.Id,LimitType = _accountLimitType.CustomerLimitAcrossAccount })).FirstOrDefault();
                    if (CustomerIdInCheckLimit==null&&customerAccountLimit.LimitType.Equals(_accountLimitType.AccountLimitPerAccount))
                    {
                        //then insert
                        await con.ExecuteAsync($@"insert into customertransactionlimit(accounttier,Singledeposittransactionlimit,Dailydeposittransactionlimit,Singlewithdrawaltransactionlimit,Dailywithdrawaltransactionlimit,userid,createdAt,AccountNumber) values(@accounttier,@Singledeposittransactionlimit,@Dailydeposittransactionlimit,@Singlewithdrawaltransactionlimit,@Dailywithdrawaltransactionlimit,@userid,@createdAt,@AccountNumber)",
                             new {
                                 accounttier=kyclevel,
                                 Singledeposittransactionlimit=customerAccountLimit.Singledeposittransactionlimit,
                                 Dailydeposittransactionlimit=customerAccountLimit.Dailydeposittransactionlimit,
                                 Singlewithdrawaltransactionlimit=customerAccountLimit.Singlewithdrawaltransactionlimit,
                                 Dailywithdrawaltransactionlimit=customerAccountLimit.Dailywithdrawaltransactionlimit,
                                 userid=usr.Id,
                                 createdAt=DateTime.Now,
                                 AccountNumber=customerAccountLimit.AccountNumber
                             });
                    }else if (CustomerLimitacrossAccount==null&&customerAccountLimit.LimitType.Equals(_accountLimitType.CustomerLimitAcrossAccount))
                      {
                        //then insert
                        await con.ExecuteAsync($@"insert into customertransactionlimit(accounttier,Singledeposittransactionlimit,Dailydeposittransactionlimit,Singlewithdrawaltransactionlimit,Dailywithdrawaltransactionlimit,userid,createdAt,LimitType) values(@accounttier,@Singledeposittransactionlimit,@Dailydeposittransactionlimit,@Singlewithdrawaltransactionlimit,@Dailywithdrawaltransactionlimit,@userid,@createdAt,@LimitType)",
                             new
                             {
                                 accounttier = kyclevel,
                                 Singledeposittransactionlimit = customerAccountLimit.Singledeposittransactionlimit,
                                 Dailydeposittransactionlimit = customerAccountLimit.Dailydeposittransactionlimit,
                                 Singlewithdrawaltransactionlimit = customerAccountLimit.Singlewithdrawaltransactionlimit,
                                 Dailywithdrawaltransactionlimit = customerAccountLimit.Dailywithdrawaltransactionlimit,
                                 userid = usr.Id,
                                 createdAt = DateTime.Now,
                                 LimitType= _accountLimitType.CustomerLimitAcrossAccount                              
                             });
                    }
                    /*
                    else
                    {
                        return new GenericResponse2() { Response = EnumResponse.WrongInput };
                    }
                    */
                    if(CustomerIdInCheckLimit!=null)
                     {
                        // then update
                        await con.ExecuteAsync($@"update customertransactionlimit set accounttier=@accounttier,Singledeposittransactionlimit=@Singledeposittransactionlimit,Dailydeposittransactionlimit=@Dailydeposittransactionlimit,Singlewithdrawaltransactionlimit=@Singlewithdrawaltransactionlimit,Dailywithdrawaltransactionlimit=@Dailywithdrawaltransactionlimit,createdAt=@createdAt,AccountNumber=@AccountNumber where userid=@userid",new
                        {
                            accounttier=kyclevel,
                            Singledeposittransactionlimit=customerAccountLimit.Singledeposittransactionlimit,
                            Dailydeposittransactionlimit=customerAccountLimit.Dailydeposittransactionlimit,
                            Singlewithdrawaltransactionlimit=customerAccountLimit.Singlewithdrawaltransactionlimit,
                            Dailywithdrawaltransactionlimit=customerAccountLimit.Dailywithdrawaltransactionlimit,
                            createdAt=DateTime.Now,
                            AccountNumber=customerAccountLimit.AccountNumber,
                            userid=usr.Id
                        });
                    }else if (CustomerLimitacrossAccount != null)
                    {
                        // then update
                        await con.ExecuteAsync($@"update customertransactionlimit set accounttier=@accounttier,Singledeposittransactionlimit=@Singledeposittransactionlimit,Dailydeposittransactionlimit=@Dailydeposittransactionlimit,Singlewithdrawaltransactionlimit=@Singlewithdrawaltransactionlimit,Dailywithdrawaltransactionlimit=@Dailywithdrawaltransactionlimit,createdAt=@createdAt where userid=@userid and LimitType=@LimitType", new
                        {
                            accounttier = kyclevel,
                            Singledeposittransactionlimit = customerAccountLimit.Singledeposittransactionlimit,
                            Dailydeposittransactionlimit = customerAccountLimit.Dailydeposittransactionlimit,
                            Singlewithdrawaltransactionlimit = customerAccountLimit.Singlewithdrawaltransactionlimit,
                            Dailywithdrawaltransactionlimit = customerAccountLimit.Dailywithdrawaltransactionlimit,
                            createdAt = DateTime.Now,
                            userid = usr.Id,
                            LimitType=_accountLimitType.CustomerLimitAcrossAccount
                        });
                    }
                    new Thread(() =>
                    {
                        SendMailObject sendMailObject = new SendMailObject();
                        sendMailObject.Subject = "Account Limit";
                        sendMailObject.Email = customerDataNotFromBvn?.Email;
                        sendMailObject.Html = $@"Dear {usr.Firstname.ToUpper()},<br/> Your have account limit has been set successfully.<br/>Thank you for banking with us.";
                        _genServ.SendMail(sendMailObject);
                    }).Start();
                    return new GenericResponse2() { Success = true, Response = EnumResponse.Successful};
                 }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
            }
        public async Task<GenericResponse> InitiateTask(int customerid, string action, string StaffNameAndRole)
        {
            try
            {

                using IDbConnection con = _context.CreateConnection();
                var actions = (await con.QueryAsync<string>("select actioname from checkeraction")).ToList();
                _logger.LogInformation("action " + action + " actions " + string.Join(",", actions));
               // Console.WriteLine("action " + action + " actions " + string.Join(",", actions));
                if (!actions.Any(a => a.Equals(action, StringComparison.OrdinalIgnoreCase)))
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.WrongAction };
                }
                var InitiatedActionId = (await con.QueryAsync<int>("select id from checkeraction where actioname=@action", new { action = action })).FirstOrDefault();
                if (InitiatedActionId == 0)
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.WrongAction };
                }
                Console.WriteLine("StaffNameAndRole " + StaffNameAndRole);
                var name = StaffNameAndRole.Split('_')[0];
                var staffid2 = (await con.QueryAsync<int>("select id from staff where email=@email", new { email = name + "@trustbancgroup.com" })).FirstOrDefault();
                Console.WriteLine("staffid2 " + staffid2);
                await con.ExecuteAsync("delete from staffaction where staffidtoaction=@userid and approvalstatus=false and action=@action",new { userid= customerid,action= InitiatedActionId });
                //staffidtoaction is the customerid in this case.
                await con.ExecuteAsync($@"insert into staffaction(action,initiationstaff,choosenstaff,staffidtoaction,createdAt)
                                          values(@action,@initiationstaff,@choosenstaff,@staffidtoaction,now())", new { action = InitiatedActionId, initiationstaff = staffid2, choosenstaff = "", staffidtoaction = customerid });
                return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> InitiateKYcTask(int customerid, string action, string StaffNameAndRole,string typeofdocument)
        {
            try
            {

                using IDbConnection con = _context.CreateConnection();
                var actions = (await con.QueryAsync<string>("select actioname from checkeraction")).ToList();
                _logger.LogInformation("action " + action + " actions " + string.Join(",", actions));
                // Console.WriteLine("action " + action + " actions " + string.Join(",", actions));
                if (!actions.Any(a => a.Equals(action, StringComparison.OrdinalIgnoreCase)))
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.WrongAction };
                }
                var InitiatedActionId = (await con.QueryAsync<int>("select id from checkeraction where actioname=@action", new { action = action })).FirstOrDefault();
                if (InitiatedActionId == 0)
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.WrongAction };
                }
                Console.WriteLine("StaffNameAndRole " + StaffNameAndRole);
                var name = StaffNameAndRole.Split('_')[0];
                var staffid2 = (await con.QueryAsync<int>("select id from staff where email=@email", new { email = name + "@trustbancgroup.com" })).FirstOrDefault();
                Console.WriteLine("staffid2 " + staffid2);
                await con.ExecuteAsync("delete from kycstaffaction where staffidtoaction=@userid and approvalstatus=false and typeofdocument=@typeofdocument and action=@action", new { userid = customerid, typeofdocument = typeofdocument, action=InitiatedActionId });
                //staffidtoaction is the customerid in this case.
                await con.ExecuteAsync($@"insert into kycstaffaction(action,initiationstaff,choosenstaff,staffidtoaction,createdAt,typeofdocument)
                                          values(@action,@initiationstaff,@choosenstaff,@staffidtoaction,now(),@typeofdocument)", new { action = InitiatedActionId, initiationstaff = staffid2, choosenstaff = "", staffidtoaction = customerid, typeofdocument= typeofdocument });
                return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> KycAcceptance(Kyc kycStatus, string type,string StaffNameAndRole)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var usr = await _genServ.GetUserbyUsername(kycStatus.Username,con);
                    var status = kycStatus.status=="accept"?"acceptkyc":"rejectkyc";
                    var checkedUserid = (await con.QueryAsync<string>(
                                           "SELECT username FROM customerkycstatus WHERE username = @username",
                                            new { username = kycStatus.Username })).FirstOrDefault();
                    var InitiatedActionId = (await con.QueryAsync<int>("select id from checkeraction where actioname=@action", new { action = status })).FirstOrDefault();
                    if (InitiatedActionId == 0)
                    {
                        return new GenericResponse() { Success = false, Response = EnumResponse.WrongAction };
                    }
                    if (string.IsNullOrEmpty(checkedUserid))
                    {
                        await con.ExecuteAsync(
                            "INSERT INTO customerkycstatus(username) VALUES(@username)",
                            new { username = kycStatus.Username});
                    }
                    if (type == "utilitybill")
                    {
                        KycUtlityBill kycUtlityBill = new KycUtlityBill();
                        kycUtlityBill.type = type;
                        kycUtlityBill.RejectionReason = kycStatus.RejectionReason;
                        kycUtlityBill.Username = kycStatus.Username;
                        kycUtlityBill.UtlityBillStatus = kycStatus.status;
                        kycUtlityBill.actionId = InitiatedActionId;
                        var resp = await KycUtlityBillAcceptance("", kycUtlityBill);
                        await con.ExecuteAsync("update document_type set submittedrequest=1 where Document=@Document and userid=@id", new { Document = type, id = usr.Id });
                        //  await con.ExecuteAsync("update customerkycstatus set kycstatus=@status where username=@username", new { status = true, username = kycStatus.Username }); //this will updated under approval
                        Console.WriteLine("KycUtlityBillAcceptance ...." + resp);
                        _genServ.LogRequestResponse("KycUtlityBillAcceptance ...", null, JsonConvert.SerializeObject(resp));
                        return await InitiateKYcTask((int)usr.Id,status,StaffNameAndRole,kycUtlityBill.type);
                        //return resp;
                    }
                    else if (type == "idcard")
                    {
                        KycidCard kycidCard = new KycidCard();
                        kycidCard.type = type;
                        kycidCard.RejectionReason = kycStatus.RejectionReason;
                        kycidCard.Username = kycStatus.Username;
                        kycidCard.actionId= InitiatedActionId;
                        kycidCard.idCardStatus = kycStatus.status;
                        var resp = await KycIdCardAcceptance("", kycidCard);
                        await con.ExecuteAsync("update document_type set submittedrequest=1 where Document=@Document and userid=@id", new { Document = type, id = usr.Id });
                        // await con.ExecuteAsync("update customerkycstatus set kycstatus=@status where username=@username", new { status = true, username = kycStatus.Username });
                        Console.WriteLine("KycIdCardAcceptance ...." + resp);
                        _genServ.LogRequestResponse("KycIdCardAcceptance ...", null, JsonConvert.SerializeObject(resp));
                        //return resp;
                        return await InitiateKYcTask((int)usr.Id,status, StaffNameAndRole,kycidCard.type);
                    }
                    else if (type == "passport")
                    {
                        KycPassport kycPassport = new KycPassport();
                        kycPassport.type = type;
                        kycPassport.RejectionReason = kycStatus.RejectionReason;
                        kycPassport.Username = kycStatus.Username;
                        kycPassport.actionId = InitiatedActionId;
                        kycPassport.PassportStatus = kycStatus.status;
                        var resp = await KycPassportAcceptance("", kycPassport);
                        await con.ExecuteAsync("update document_type set submittedrequest=1 where Document=@Document and userid=@id", new { Document = type, id = usr.Id });
                        //  await con.ExecuteAsync("update customerkycstatus set kycstatus=@status where username=@username", new { status = true, username = kycStatus.Username });
                        Console.WriteLine("KycPassportAcceptance ...." + resp);
                        _genServ.LogRequestResponse("KycPassportAcceptance ...", null, JsonConvert.SerializeObject(resp));
                        //return resp;
                        return await InitiateKYcTask((int)usr.Id, status, StaffNameAndRole,kycPassport.type);
                    }
                    else if (type == "signature")
                    {
                        KycSignature kycSignature = new KycSignature();
                        kycSignature.type = type;
                        kycSignature.RejectionReason = kycStatus.RejectionReason;
                        kycSignature.Username = kycStatus.Username;
                        kycSignature.actionid= InitiatedActionId;
                        kycSignature.SignatureStatus = kycStatus.status;
                        var resp = await KycSignatureAcceptance("", kycSignature);
                        await con.ExecuteAsync("update document_type set submittedrequest=1 where Document=@Document and userid=@id", new { Document = type, id = usr.Id });
                        //  await con.ExecuteAsync("update customerkycstatus set kycstatus=@status where username=@username", new { status = true, username = kycStatus.Username });
                        Console.WriteLine("KycSignatureAcceptance ...." + resp);
                        _genServ.LogRequestResponse("KycSignatureAcceptance ...", null, JsonConvert.SerializeObject(resp));
                        // return resp;
                        return await InitiateKYcTask((int)usr.Id, status, StaffNameAndRole,kycSignature.type);
                    }
                    else
                    {
                        return new GenericResponse() { Success = false, Response = EnumResponse.WrongInput };
                    }
                }
            }catch (Exception ex)
             {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
             }
        }

        public async Task<ValidateOtpResponse> SendConfirmationOtp(OtpType registration,string PhoneNumber,string Username, string type)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var usr = await _genServ.GetUserbyUsername(Username, con);
                    if (usr == null)
                    {
                     new ValidateOtpResponse() { Success = false, Response = EnumResponse.UsernameNotFound };
                    }
                   string otp =  _genServ.GenerateOtp();
                   CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)usr.Id);
                    OtpTransLimit otpTransLimit = new OtpTransLimit();
                    await _genServ.SendOtp3(OtpType.Confirmation,otp,customerDataNotFromBvn.PhoneNumber,_smsBLService,"confirmation",customerDataNotFromBvn.Email);
                    DateTime dateTime = DateTime.Now;
                    string dateTimeString = dateTime.ToString("yyyy-MM-dd HH:mm:ss"); // Customize the format as needed
                    //Console.WriteLine(dateTimeString); // Outputs: 2024-10-30 12:15:00
                    otpTransLimit.otp = otp;
                    otpTransLimit.PhoneNumber = customerDataNotFromBvn.PhoneNumber;
                    otpTransLimit.DateTimeString= dateTimeString;
                    if (_redisStorageService.GetCustomerAsync($"otp_{otpTransLimit.PhoneNumber}")!=null)
                    {
                       await _redisStorageService.RemoveCustomerAsync($"otp_{otpTransLimit.PhoneNumber}");
                    }
                    await _redisStorageService.SetCacheDataAsync($"otp_{otpTransLimit.PhoneNumber}",otpTransLimit);
                   return new ValidateOtpResponse() { Success = true, Response = EnumResponse.Successful };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new ValidateOtpResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<ValidateOtpResponse> SendTypeOtp(OtpType registration, string username, string typeOtp)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var usr = await _genServ.GetUserbyUsername(username, con);
                    if (usr == null)
                    {
                        new ValidateOtpResponse() { Success = false, Response = EnumResponse.UsernameNotFound };
                    }
                    _logger.LogInformation("typeotp "+typeOtp);
                    string otp = _genServ.GenerateOtp();
                    //_logger.LogInformation();
                    CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)usr.Id);
                    OtpTransLimit otpTransLimit = new OtpTransLimit();
                    _logger.LogInformation("customerDataNotFromBvn " + JsonConvert.SerializeObject(customerDataNotFromBvn));
                    await _genServ.SendOtp3(OtpType.PinResetOrChange, otp, customerDataNotFromBvn.PhoneNumber, _smsBLService,"Confirmation", customerDataNotFromBvn?.Email);
                    DateTime dateTime = DateTime.Now;
                    string dateTimeString = dateTime.ToString("yyyy-MM-dd HH:mm:ss"); // Customize the format as needed
                    _logger.LogInformation("dateTimeString " + dateTimeString);// Outputs: 2024-10-30 12:15:00
                    otpTransLimit.otp = otp;
                    otpTransLimit.PhoneNumber = customerDataNotFromBvn?.PhoneNumber;
                    otpTransLimit.DateTimeString = dateTimeString;
                    if (_redisStorageService.GetCustomerAsync($"{typeOtp}_{otpTransLimit.PhoneNumber}") != null)
                    {
                        _logger.LogInformation("removed key for pin");
                        await _redisStorageService.RemoveCustomerAsync($"{typeOtp}_{otpTransLimit.PhoneNumber}");
                    }
                    string key = $"{typeOtp}{otpTransLimit.PhoneNumber}";
                    _logger.LogInformation("key "+key+ " otpTransLimit "+JsonConvert.SerializeObject(otpTransLimit));
                    await _redisStorageService.SetCacheDataAsync(key, otpTransLimit);
                    _logger.LogInformation("sent and saved successfully "+JsonConvert.SerializeObject(otpTransLimit));
                    return new ValidateOtpResponse() { Success = true, Response = EnumResponse.Successful };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new ValidateOtpResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> ValidateOtp2(string clientKey, ValidateOtpRetrival Request)
        {
            try
            {
                if (string.IsNullOrEmpty(Request.AccountNumber))
                    return new GenericResponse() { Response = EnumResponse.InvalidDetails };

                //Check CBA using Account No
                var getCust = await _genServ.GetCustomerbyAccountNo(Request.AccountNumber);
                if (!getCust.success)
                    return new GenericResponse() { Response = EnumResponse.UserNotFound };

                using (IDbConnection con = _context.CreateConnection())
                {
                    var getuser = await _genServ.GetUserbyCustomerId(getCust.result.customerID, con);
                    if (getuser == null)
                        return new GenericResponse() { Response = EnumResponse.UserNotFound };

                    var validateSession = await _genServ.ValidateSession(getuser.Username, Request.Session, Request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                    var resp = await _genServ.ValidateSessionOtp2((OtpType)Request.RetrivalType, Request.Session, con);
                   // if (resp == null || resp.OTP != Request.Otp)- you can check later
                     //   return new ValidateOtpResponse() { Response = EnumResponse.InvalidOtp };
                    CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)getuser.Id);
                    if ((OtpType)Request.RetrivalType == OtpType.RetrieveUsername)
                    {
                        string msg = _settings.RetriveUsernameText;
                        msg = msg.Replace("{Username}", getuser.Username);
                        var request = new SendSmsRequest()
                        {
                            ClientKey = "",
                            Message = msg,
                            PhoneNumber = getuser.PhoneNumber,
                            SmsReference = DateTime.Now.ToString("ddMMyyyyHHmmss") + "_" + new Random().Next(11111, 99999)
                        };
                        // await _genServ.SendSMS(request);
                        Task.Run(async () =>
                        {
                            customerDataNotFromBvn.PhoneNumber = !string.IsNullOrEmpty(customerDataNotFromBvn.PhoneNumber) ? customerDataNotFromBvn.PhoneNumber : getuser.PhoneNumber;
                            var msg = $@"Dear {getuser.Firstname},your username is {getuser.Username}.Thank you for banking with us.";
                            customerDataNotFromBvn.PhoneNumber = CustomerServiceNotFromBvnService.ReplaceFirstDigit(customerDataNotFromBvn.PhoneNumber, "234");
                            GenericResponse response = await _smsBLService.SendSmsNotificationToCustomer("Otp", customerDataNotFromBvn.PhoneNumber, $@"{msg}", "AccountNumber Creation", _settings.SmsUrl);
                            _logger.LogInformation("response " + response.ResponseMessage + " message " + response.Message);
                        });
                        SendMailObject sendMailObject = new SendMailObject();
                        //sendMailObject.BvnEmail = getuser.BvnEmail;
                        sendMailObject.Email = customerDataNotFromBvn?.Email;
                        sendMailObject.Subject = "TrustBanc Mobile App Username Retrieval";
                        sendMailObject.Html = $@"
                            <p>Dear {getuser.Firstname.ToUpper()} {getuser.LastName.ToUpper()},
                             </p>
                             <p>Your Username is {getuser.Username}.</p>
                            <p>Thank your for Banking with us.</p>
                                            ";
                        Thread thread = new Thread(() =>
                        {
                            _logger.LogInformation("mail sending");
                            _genServ.SendMail(sendMailObject);
                            _logger.LogInformation("mail sent");
                        });
                        thread.Start();
                    }

                    if ((OtpType)Request.RetrivalType == OtpType.UnlockDevice)
                        await con.ExecuteAsync($"update mobiledevice set status = 1 where userid = {getuser.Id} and status = 3");

                    if ((OtpType)Request.RetrivalType == OtpType.UnlockProfile)
                        await con.ExecuteAsync($"update users set status = 1 where id = {getuser.Id}");
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

        public async Task<GenericResponse2> UploadIndemnityFormWithoutForm(IndemnityForm customerDocuments)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    _logger.LogInformation("customer documents "+JsonConvert.SerializeObject(customerDocuments));
                    
                   var validateSession = await _genServ.ValidateSession(customerDocuments.Username,
                        customerDocuments.Session, customerDocuments.ChannelId, con);
                    if (!validateSession)
                    {
                        return new GenericResponse2() { Response = EnumResponse.InvalidSession,
                            Message = "Invalid Session" };
                    }

                    var myuserid = (await con.QueryAsync<string>("select userid from user_credentials where credentialtype=2 and status=1 and userid=(select id from users where Username=@Username)", new { username = customerDocuments.Username })).FirstOrDefault();
                    if (string.IsNullOrEmpty(myuserid))
                    {
                        return new GenericResponse2() { Response = EnumResponse.InvalidUsernameOrPassword, Message = "Invalid Customer Id" };
                    }
                    //check for the capped  limit here 
                    //TransactionCappedLimit transactionCappedLimit=(await con.QueryAsync<TransactionCappedLimit>("")).FirstOrDefault();
                    TransactionCappedLimit transactionCappedLimit = (await con.QueryAsync<TransactionCappedLimit>("select * from transactioncappedlimit where ApprovalStatus=true")).FirstOrDefault();
                    if (transactionCappedLimit == null)
                    {
                        return new GenericResponse2() { Response = EnumResponse.NotApprovetYet };
                    }
                    if(customerDocuments.Singlewithdrawaltransactionlimit>transactionCappedLimit.SingleTransactionLimit)
                    {
                        return new GenericResponse2() { Response = EnumResponse.SinglecummulativeTierLimitExceeded };
                    }
                    if (customerDocuments.Dailywithdrawaltransactionlimit > transactionCappedLimit.DailyCummulativeLimit)
                    {
                        return new GenericResponse2(){ Response = EnumResponse.CappedcummulativeTierLimitExceeded };
                    }
                    // check if the customer already has customer indemnity
                    if (customerDocuments.IndemnityType.Equals(_indemnityType.AccountIndemnityperAccount, StringComparison.CurrentCultureIgnoreCase))
                    {
                        var customerIndemnity = (await con.QueryAsync<IndemnityForm>("select * from customerindemnity where userid=@userid and IndemnityType=@IndemnityType", new { userid = myuserid, IndemnityType = _indemnityType.CustomerIndemnityAcrossAccount })).FirstOrDefault();
                        if (customerIndemnity!=null)
                        {
                            // check accountindemnity 
                            var accountIndemnity = (await con.QueryAsync<IndemnityForm>("select * from customerindemnity where userid=@userid and AccountNumber=@AccountNumber", new { userid = myuserid, AccountNumber = customerDocuments.AccountNumber })).FirstOrDefault();
                            if (accountIndemnity!=null)
                            {
                                if (accountIndemnity.Dailywithdrawaltransactionlimit>customerIndemnity.Dailywithdrawaltransactionlimit)
                                {
                                    return new GenericResponse2(){Response=EnumResponse.CustomerIndemnityOverRuleAccountIndemnity,Message="customer overrule account indemnity"};
                                }
                                if (accountIndemnity.Singlewithdrawaltransactionlimit > customerIndemnity.Singlewithdrawaltransactionlimit)
                                {
                                    return new GenericResponse2() { Response = EnumResponse.SingleCustomerIndemnityOverRuleAccountIndemnity, Message = "customer overrule account indemnity" };
                                }
                                
                            }
                        }
                    }
                    string path = _settings.IndemnityformPath;
                    if (customerDocuments.IndemnityType.Equals(_indemnityType.AccountIndemnityperAccount, StringComparison.CurrentCultureIgnoreCase)) {
                        myuserid = (await con.QueryAsync<string>("select userid from customerindemnity where userid=@userid and AccountNumber=@AccountNumber and IndemnityType=@IndemnityType", new { userid = myuserid, AccountNumber = customerDocuments.AccountNumber, IndemnityType = _indemnityType.AccountIndemnityperAccount })).FirstOrDefault();
                    } else if (customerDocuments.IndemnityType.Equals(_indemnityType.CustomerIndemnityAcrossAccount,StringComparison.CurrentCultureIgnoreCase)) {
                        myuserid = (await con.QueryAsync<string>("select userid from customerindemnity where userid=@userid and IndemnityType=@IndemnityType", new { userid = myuserid, IndemnityType = _indemnityType.CustomerIndemnityAcrossAccount })).FirstOrDefault();
                    }
                    var usr = (await _genServ.GetUserbyUsername(customerDocuments.Username, con));
                    //var AccountDetails = await _genServ.GetCustomerbyAccountNo(AccountNumber);
                    BalanceEnquiryResponse balanceEnquiryResponse = await _genServ.GetAccountbyCustomerId(usr.CustomerId);
                    // string kyclevel = balanceEnquiryResponse.balances.ElementAtOrDefault(0).kycLevel;
                    // string kyclevel = (customerDocuments.IndemnityType.Equals(_indemnityType.AccountIndemnityperAccount)) ? balanceEnquiryResponse.balances.Where(e=>e.accountNumber.Equals(customerDocuments.AccountNumber)).ToList().ElementAt(0).kycLevel : balanceEnquiryResponse.balances.ElementAtOrDefault(0).kycLevel;
                    string kyclevel;
                    if (customerDocuments.IndemnityType.Equals(_indemnityType.AccountIndemnityperAccount,StringComparison.CurrentCultureIgnoreCase))
                    {
                        var matchingAccount = balanceEnquiryResponse.balances
                            .Where(e => e.accountNumber.Equals(customerDocuments.AccountNumber))
                            .ToList();
                        _logger.LogInformation("in if "+ JsonConvert.SerializeObject(matchingAccount));
                        if (matchingAccount != null && matchingAccount.Any())
                        {
                            kyclevel = matchingAccount[0].kycLevel;
                        }
                        else
                        {
                            kyclevel = null; // No matching account found.
                        }
                    }
                    else
                    {
                        _logger.LogInformation("else "+ balanceEnquiryResponse.balances);
                        if (balanceEnquiryResponse.balances != null && balanceEnquiryResponse.balances.Any())
                        {
                            kyclevel = balanceEnquiryResponse.balances.ElementAtOrDefault(0).kycLevel; // Check and retrieve the first element.
                        }
                        else
                        {
                            kyclevel = null; // No balances available.
                        }
                    }
                    if (kyclevel==null) {
                        return new GenericResponse2() { Response = EnumResponse.InvalidAccount };
                     }
                    _logger.LogInformation("kyclevel " + kyclevel);
                    if (!kyclevel.Equals(_tier3AccountLimitInfo.kycLevel, StringComparison.CurrentCultureIgnoreCase))
                    {
                        return new GenericResponse2() { Response = EnumResponse.AccountLessThanTier3 };
                    }                   
                    CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)usr.Id);
                    //customerDocuments.indemnityformpath = Path.Combine(path, indemnityform.FileName);
                    string otp = await _redisStorageService.GetCustomerAsync($"otp_{customerDataNotFromBvn.PhoneNumber}");
                    OtpTransLimit otpTransLimit = JsonConvert.DeserializeObject<OtpTransLimit>(otp);
                    _logger.LogInformation(" otpTransLimit " + JsonConvert.SerializeObject(otpTransLimit));
                    _logger.LogInformation("otp in limit "+otpTransLimit.otp);
                    _logger.LogInformation("customerDocuments otp " + customerDocuments.Otp);
                    _logger.LogInformation("customerdocuments "+ !customerDocuments.Otp.Equals(otpTransLimit.otp, StringComparison.CurrentCultureIgnoreCase));
                    if (!customerDocuments.Otp.Equals(otpTransLimit.otp,StringComparison.CurrentCultureIgnoreCase))
                    {
                        return new GenericResponse2() { Response = EnumResponse.InvalidOtp };
                    }
                    DateTime parseddateTime = DateTime.Parse(otpTransLimit.DateTimeString);
                    _logger.LogInformation("dateTimeString " + otpTransLimit.DateTimeString);
                    DateTime dateTime = DateTime.Now;
                    TimeSpan difference = dateTime - parseddateTime;
                    if (Math.Abs(difference.TotalMinutes) >= 3)
                    {
                        return new GenericResponse2() { Response = EnumResponse.OtpTimeOut, Success = false };
                    }             
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
                    };

                    //var model = System.Text.Json.JsonSerializer.Deserialize<IndemnityForm>(customerDocuments,options);

                    if (string.IsNullOrEmpty(myuserid) && !string.IsNullOrEmpty(customerDocuments.AccountNumber) && customerDocuments.IndemnityType.Equals(_indemnityType.AccountIndemnityperAccount, StringComparison.CurrentCultureIgnoreCase)) // if it is new
                    {
                        await con.ExecuteAsync($@"insert into
                                             customerindemnity(userid,createdAt,accounttier,indemnityformpath,requestpurpose,
                                             Singledeposittransactionlimit,
                                             Dailydeposittransactionlimit,
                                             Singlewithdrawaltransactionlimit,
                                             Dailywithdrawaltransactionlimit,AccountNumber,Consent,)
                                             values(@userid,@createdAt,@accounttier,@indemnityformpath,
                                              @requestpurpose
                                             ,@Singledeposittransactionlimit
                                             ,@Dailydeposittransactionlimit,
                                              @Singlewithdrawaltransactionlimit,
                                              @Dailywithdrawaltransactionlimit,@AccountNumber,@Consent)",
                                                                    new
                                                                    {
                                                                        userid = usr.Id,
                                                                        createdAt = DateTime.Now,
                                                                        accounttier = customerDocuments.accounttier,
                                                                        indemnityformpath = "",
                                                                        requestpurpose = customerDocuments.requestpurpose,
                                                                        Singledeposittransactionlimit = customerDocuments.Singledeposittransactionlimit,
                                                                        Dailydeposittransactionlimit = customerDocuments.Dailydeposittransactionlimit,
                                                                        Singlewithdrawaltransactionlimit = customerDocuments.Singlewithdrawaltransactionlimit,
                                                                        Dailywithdrawaltransactionlimit = customerDocuments.Dailywithdrawaltransactionlimit,
                                                                        AccountNumber = customerDocuments.AccountNumber,
                                                                        Consent=customerDocuments.Consent
                                                                    });
                       // await _fileService.SaveFileAsync(indemnityform, path); // save to external folder
                    }
                    else if (!string.IsNullOrEmpty(myuserid) && 
                        !string.IsNullOrEmpty(customerDocuments.AccountNumber)
                        &&
                        customerDocuments.IndemnityType.Equals(_indemnityType.AccountIndemnityperAccount, StringComparison.CurrentCultureIgnoreCase))
                    {
                        //update the indemnity for the particular account
                        var customerIndemnity = (await con.QueryAsync<IndemnityForm>("select * from customerindemnity where userid=@userid and AccountNumber=@AccountNumber", new { userid = myuserid, AccountNumber = customerDocuments.AccountNumber })).FirstOrDefault();
                        await con.ExecuteAsync($@" 
                                             update customerindemnity set indemnitystatus='Awaiting review',indemnityapproval=false,initiated=false,createdAt=@createdAt,accounttier=@accounttier,indemnityformpath=@indemnityformpath,requestpurpose=@requestpurpose,
                                             Singledeposittransactionlimit=@Singledeposittransactionlimit,
                                             Dailydeposittransactionlimit=@Dailydeposittransactionlimit,
                                             Singlewithdrawaltransactionlimit=@Singlewithdrawaltransactionlimit,
                                             Dailywithdrawaltransactionlimit=@Dailywithdrawaltransactionlimit,Consent=@Consent where userid=@userid and AccountNumber=@AccountNumber",
                                                                         new
                                                                         {
                                                                             createdAt = DateTime.Now,
                                                                             accounttier = customerDocuments.accounttier,
                                                                             indemnityformpath = "",
                                                                             requestpurpose = customerDocuments.requestpurpose,
                                                                             Singledeposittransactionlimit = customerDocuments.Singledeposittransactionlimit,
                                                                             Dailydeposittransactionlimit = customerDocuments.Dailydeposittransactionlimit,
                                                                             Singlewithdrawaltransactionlimit = customerDocuments.Singlewithdrawaltransactionlimit,
                                                                             Dailywithdrawaltransactionlimit = customerDocuments.Dailywithdrawaltransactionlimit,
                                                                             userid = usr.Id,
                                                                             AccountNumber = customerDocuments.AccountNumber,
                                                                             Consent=customerDocuments.Consent
                                                                         });
                       // await _fileService.SaveFileAsync(indemnityform, path);
                    }
                    else if (string.IsNullOrEmpty(myuserid) && string.IsNullOrEmpty(customerDocuments.AccountNumber) && customerDocuments.IndemnityType.Equals(_indemnityType.CustomerIndemnityAcrossAccount, StringComparison.CurrentCultureIgnoreCase))
                    {
                        // insert afresh
                        await con.ExecuteAsync($@"insert into
                                             customerindemnity(userid,createdAt,accounttier,indemnityformpath,requestpurpose,
                                             Singledeposittransactionlimit,
                                             Dailydeposittransactionlimit,
                                             Singlewithdrawaltransactionlimit,
                                             Dailywithdrawaltransactionlimit,IndemnityType,Consent)
                                             values(@userid,@createdAt,@accounttier,@indemnityformpath,
                                              @requestpurpose
                                             ,@Singledeposittransactionlimit
                                             ,@Dailydeposittransactionlimit,
                                              @Singlewithdrawaltransactionlimit,
                                              @Dailywithdrawaltransactionlimit,@IndemnityType,@Consent)",
                                                                      new
                                                                      {
                                                                          userid = usr.Id,
                                                                          createdAt = DateTime.Now,
                                                                          accounttier = customerDocuments.accounttier,
                                                                          indemnityformpath = "",
                                                                          requestpurpose = customerDocuments.requestpurpose,
                                                                          Singledeposittransactionlimit = customerDocuments.Singledeposittransactionlimit,
                                                                          Dailydeposittransactionlimit = customerDocuments.Dailydeposittransactionlimit,
                                                                          Singlewithdrawaltransactionlimit = customerDocuments.Singlewithdrawaltransactionlimit,
                                                                          Dailywithdrawaltransactionlimit = customerDocuments.Dailywithdrawaltransactionlimit,
                                                                          IndemnityType = _indemnityType.CustomerIndemnityAcrossAccount,
                                                                          Consent=customerDocuments.Consent
                                                                      });
                      //  await _fileService.SaveFileAsync(indemnityform, path); // save to external folder
                    }
                    else if (!string.IsNullOrEmpty(myuserid) && 
                        string.IsNullOrEmpty(customerDocuments.AccountNumber) && 
                        customerDocuments.IndemnityType.Equals(_indemnityType.CustomerIndemnityAcrossAccount, StringComparison.CurrentCultureIgnoreCase))
                    {
                        // update afresh
                        //update the indemnity for the particular account
                       // var customerIndemnity = (await con.QueryAsync<IndemnityForm>("select * from customerindemnity where userid=@userid and AccountNumber=@AccountNumber", new { userid = myuserid, AccountNumber = customerDocuments.AccountNumber })).FirstOrDefault();
                        var customerIndemnity = (await con.QueryAsync<IndemnityForm>("select * from customerindemnity where userid=@userid and IndemnityType=@IndemnityType", new { userid = myuserid, IndemnityType = _indemnityType.CustomerIndemnityAcrossAccount })).FirstOrDefault();
                        await con.ExecuteAsync($@" 
                                             update customerindemnity set indemnityapproval=false,indemnitystatus='Awaiting review',initiated=false,createdAt=@createdAt,accounttier=@accounttier,indemnityformpath=@indemnityformpath,requestpurpose=@requestpurpose,
                                             Singledeposittransactionlimit=@Singledeposittransactionlimit,
                                             Dailydeposittransactionlimit=@Dailydeposittransactionlimit,
                                             Singlewithdrawaltransactionlimit=@Singlewithdrawaltransactionlimit,
                                             Dailywithdrawaltransactionlimit=@Dailywithdrawaltransactionlimit,Consent=@Consent
                                             where userid=@userid and IndemnityType=@IndemnityType",
                                                                         new
                                                                         {
                                                                             createdAt = DateTime.Now,
                                                                             accounttier = customerDocuments.accounttier,
                                                                             indemnityformpath = "",
                                                                             requestpurpose = customerDocuments.requestpurpose,
                                                                             Singledeposittransactionlimit = customerDocuments.Singledeposittransactionlimit,
                                                                             Dailydeposittransactionlimit = customerDocuments.Dailydeposittransactionlimit,
                                                                             Singlewithdrawaltransactionlimit = customerDocuments.Singlewithdrawaltransactionlimit,
                                                                             Dailywithdrawaltransactionlimit = customerDocuments.Dailywithdrawaltransactionlimit,
                                                                             userid = usr.Id,
                                                                             IndemnityType = _indemnityType.CustomerIndemnityAcrossAccount,
                                                                             Consent=customerDocuments.Consent
                                                                         });
                      //  await _fileService.SaveFileAsync(indemnityform, path);
                    }
                    else
                    {
                        return new GenericResponse2() { Response = EnumResponse.WrongAction };
                    }
                    // send email .
                    Thread thread = new Thread(async () =>
                    {
                        // var firstname = (await con.QueryAsync<string>("select firstname from users where username=@Username)", new { Username = customerDocuments.Username })).FirstOrDefault();
                        var firstname = (await con.QueryAsync<string>(
                                    "SELECT firstname FROM users WHERE username = @Username",
                                    new { Username = customerDocuments.Username })
                                ).FirstOrDefault();
                        SendMailObject sendMailObject = new SendMailObject();
                        sendMailObject.Subject = "Customer Indemnity";
                        sendMailObject.Email = customerDataNotFromBvn?.Email;
                        sendMailObject.Html = $@"Dear {firstname.ToUpper()}
                                              <p>Thank you for providing the necessary details and documents for customer indemnity form. Your request has been received and is currently being processed by our customer support team. </p>
                                              <p>Should we require any additional information or documentation, we will reach out to you promptly. Otherwise, you can expect to receive confirmation of your upgraded mobile banking account within next 24 hours.
                                              For any feedback or enquiries, please call our Contact Center on 07004446147 or send an email to support@trustbancgroup.com.</p> 
                                              <p>Thank you for choosing TrustBanc J6 MfB</p>
                                              ";
                        _genServ.SendMail(sendMailObject);
                        sendMailObject.Email = _settings.CustomerServiceEmail; //for mgt
                        //sendMailObject.Html = $@"This customer Mr/Mrs {firstname.ToUpper()} {usr.LastName.ToUpper()} with phonenumber {customerDataNotFromBvn?.PhoneNumber} has just uploaded his/her Indemnity for this day at {DateTime.Now}.Kindly call, find out and process this request on his/her behalf";
                        sendMailObject.Html = $@"
                                                    <p>Subject: Indemnity Request Due to [Issue/Incident] – {customerDocuments.IndemnityType}</p>
                                                    <p>
                                                    Incident Details:
                                                    •	Customer(s) Affected: {usr.Firstname} {usr.LastName} with phonenumber {customerDataNotFromBvn?.PhoneNumber}
                                                    •	Incident Date/Time: {DateTime.Now}
                                                    •	Nature of the Indmnity: {customerDocuments.IndemnityType},Singlewithdrawaltransactionlimit-{customerDocuments.Singlewithdrawaltransactionlimit} and Dailywithdrawaltransactionlimit-{customerDocuments.Dailywithdrawaltransactionlimit}                                                   
                                                    </p>
                                                    <p>
                                                    Request:
                                                    We request that the affected customer(s) be provided with indemnity for any potential losses, errors, or unauthorized transactions that may have occurred due to the technical issue. The IT department has taken steps to address the root cause of the issue, but we believe indemnity is required to protect the customers involved.
                                                    </p>
                                                    <p>
                                                    Action Needed:
                                                    •	Please initiate the indemnity process for the affected customer(s) as per TrustBanc’s policies.
                                                    •	Kindly notify us of any required documentation or further details needed from the IT team to complete this request.
                                                    </p>
                                                    <p>
                                                    We would appreciate your prompt assistance in processing this indemnity request. If you need any additional technical details or clarification, please feel free to reach out to us.
                                                    Thank you for your support and cooperation.
                                                    </p>
                                                    <p>
                                                    Best regards,
                                                    TrustBanc IT Department
                                                    {_settings.SupportPhoneNumber}
                                                    </p>
                                                    ";
                        _genServ.SendMail(sendMailObject);
                    });
                    thread.Start();
                    //send mail to customerservice and operation for indemnity form attachement of customer
                    Task.Run(async () => {
                        CustomerDetails customerDetails = new CustomerDetails();
                        customerDetails.FirstName = usr.Firstname;
                        customerDetails.LastName = usr.LastName;
                        customerDetails.PhoneNumber = usr.PhoneNumber;
                        customerDetails.Email = usr.Email;
                        customerDetails.CustomerServiceEmail = _settings.CustomerServiceEmail;
                        customerDetails.OperationEmail = _settings.OperationEmail;
                        string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.AccountUrl + "api/Account/SendIndemnityWithAttachmentToCustomerService", customerDetails, true);
                        //_logger.LogInformation("response " + response);
                        GenericResponse2 mailnotification = JsonConvert.DeserializeObject<GenericResponse2>(response);
                        _logger.LogInformation("mailnotification " + JsonConvert.SerializeObject(mailnotification));
                    });
                    return new GenericResponse2() { Success = true, Response = EnumResponse.Successful, Message = "Indemnity successful" };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse2> MailAfterLoginAndSuccessfulAccountFetch(string username, string Session, int ChannelId,string DeviceName)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    // _logger.LogInformation("customer documents " + JsonConvert.SerializeObject(customerDocuments));
                    var validateSession = await _genServ.ValidateSession(username,
                        Session, ChannelId, con);
                    if (!validateSession)
                    {
                        return new GenericResponse2()
                        {
                            Response = EnumResponse.InvalidSession,
                            Message = "Invalid Session"
                        };
                    }
                    var usr =await _genServ.GetUserbyUsername(username,con);
                    CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)usr.Id);
                    SendMailObject sendMailObject = new SendMailObject();
                    sendMailObject.Email = customerDataNotFromBvn?.Email;
                    sendMailObject.Firstname = usr.Firstname + " " + usr.LastName;
                    sendMailObject.Html = $@"<p>Hello Dear {username}</p>,
                                      <p>You logged into your TrustBanc Mobile Platform from a device {DeviceName} at {DateTime.Now}.</p>
                                     <p>If this login did not originate from you, please let us know by sending an email to support@trustbancgroup.com</p>
                                     <p>Alternatively, you can call 07004446147 immediately.Thanks.</p>
                                     <p>Thank you for choosing TrustBanc J6 MfB.</p>";
                    sendMailObject.Subject = "TrustBanc Bank Mobile App Login Notification";
                    Thread thread = new Thread(() =>
                    {
                        //Console.WriteLine("sending mail in thread");
                        _genServ.LogRequestResponse("enter in thread to send email ", $" ", "");
                        _genServ.SendMail(sendMailObject);
                        Console.WriteLine("mail sent ....");
                    });
                    thread.Start();
                    return new GenericResponse2() { Response=EnumResponse.Successful,Success=true};
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
           }
        }
}








