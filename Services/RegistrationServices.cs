using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlX.XDevAPI;
using Newtonsoft.Json;
using Org.BouncyCastle.Asn1.Ocsp;
using RestSharp;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using Retailbanking.Common.DbObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static System.Collections.Specialized.BitVector32;

namespace Retailbanking.BL.Services
{
    public class RegistrationServices : IRegistration
    {
        private readonly ILogger<RegistrationServices> _logger;
        private readonly AppSettings _settings;
        private readonly IGeneric _genServ;
        private readonly IMemoryCache _cache;
        private readonly DapperContext _context;
        private readonly ISmsBLService _smsBLService;
        private readonly Tier3AccountLimitInfo _tier3AccountLimitInfo;
        private readonly Tier2AccountLimitInfo _tier2AccountLimitInfo;
        private readonly Tier1AccountLimitInfo _tier1AccountLimitInfo;
        private readonly IUserCacheService _userCacheService;
        private readonly IRedisStorageService _redisStorageService;
        private readonly IFileService _fileService;
        public RegistrationServices(IFileService fileService, IRedisStorageService redisStorageService,IOptions<Tier3AccountLimitInfo> tier3AccountLimitInfo, IOptions<Tier2AccountLimitInfo> tier2AccountLimitInfo, IOptions<Tier1AccountLimitInfo> tier1AccountLimitInfo, IUserCacheService userCacheService, ILogger<RegistrationServices> logger, IOptions<AppSettings> options, IGeneric genServ, IMemoryCache memoryCache, DapperContext context, ISmsBLService smsBLService)
        {
            _redisStorageService = redisStorageService;
            _fileService = fileService;
            _tier1AccountLimitInfo = tier1AccountLimitInfo.Value;
            _tier2AccountLimitInfo = tier2AccountLimitInfo.Value;
            _tier3AccountLimitInfo = tier3AccountLimitInfo.Value;
            _logger = logger;
            _settings = options.Value;
            _genServ = genServ;
            _cache = memoryCache;
            _context = context;
            _smsBLService = smsBLService;
           _userCacheService = userCacheService;
        }

        public async Task<string> MigratedExistinguser(string sess, string otp, int ChannelId, MigratedCustomer migratedCustomer, IDbConnection con,string uniRef2)
        {
            // string sess = _genServ.GetSession();
            // string otp = _genServ.GenerateOtp();
            var uniRef = $"Reg{new Random().Next(11111, 99999)}{DateTime.Now.ToString("ddMMyyyyHHmm")}";
            string sql = $@"insert into registration (channelid, bvn, requestreference, createdon,validbvn,nin,migrateduser,CustomerId)
                              values ({ChannelId},@bvs,'{uniRef}',sysdate(),1,'{migratedCustomer.NiN}',true,@CustomerId)";
            await con.ExecuteAsync(sql, new { bvs = migratedCustomer.Bvn, CustomerId=migratedCustomer.CustID });
            var checkreq = await con.QueryAsync<Registration>($"SELECT * FROM registration where bvn = @bnvs", new { bnvs = migratedCustomer.Bvn });
            Console.WriteLine($"checkreq ${checkreq}");
            BvnResponse bvnResponse = new BvnResponse();
            bvnResponse.bvn = migratedCustomer.Bvn;
            bvnResponse.bankBranch = "";
            bvnResponse.bankCode = "";
            bvnResponse.email = migratedCustomer.Email;
            bvnResponse.phoneNumber = migratedCustomer.PhoneNumber;
            bvnResponse.secondaryPhoneNumber = migratedCustomer.PhoneNumber;
            bvnResponse.nationalIdentityNumber = migratedCustomer.NiN;
            bvnResponse.base64Image = "";
            bvnResponse.firstName = migratedCustomer.FirstName;
            bvnResponse.lastName = migratedCustomer.SurName;
            bvnResponse.dateOfBirth = migratedCustomer.DOB;
            bvnResponse.levelOfAccount = "";
            bvnResponse.lgaOfOrigin = "";
            bvnResponse.lgaOfResidence = "";
            bvnResponse.maritalStatus = "";
            bvnResponse.middleName = migratedCustomer.othername;
            bvnResponse.nameOnCard = "";
            bvnResponse.gender = "";
            bvnResponse.nationality = migratedCustomer.Nationality;
            bvnResponse.residentialAddress = migratedCustomer.residentialaddress;
            await InsertBvn(bvnResponse, con);
            await _genServ.InsertOtp(OtpType.Registration, checkreq.FirstOrDefault().ID, sess, otp, con);
            //await _genServ.SendOtp(OtpType.Registration, otp, checkreq.FirstOrDefault().PhoneNumber, checkreq.FirstOrDefault().Email);
            await InsertRegistrationSession(checkreq.FirstOrDefault().ID, sess, ChannelId, con);
            SetRegristationCredential setRegristationCredential = new SetRegristationCredential();
            setRegristationCredential.ChannelId = ChannelId;
            setRegristationCredential.SecretValue = migratedCustomer.username;
            setRegristationCredential.Session = sess;
            setRegristationCredential.RequestReference = uniRef;
            await con.ExecuteAsync($"update registration set lastname= @lastname, phonenumber = @ph, email = @em,firstname = @fn,ValidBvn = 1 where id= {checkreq.FirstOrDefault().ID}", new
            {
                lastname = migratedCustomer.SurName,
                ph = migratedCustomer.PhoneNumber,
                em = migratedCustomer.Email,
                fn = migratedCustomer.FirstName
            });
            string queryCheck = "select email from customerdatanotfrombvn where email = @Email or phonenumber=@PhoneNumber";
            var customerdatanotfrombvnCheck = (await con.QueryAsync<string>(queryCheck, new { Email = migratedCustomer.Email, PhoneNumber = migratedCustomer.PhoneNumber })).FirstOrDefault();
            _logger.LogInformation("checking mail ..." + customerdatanotfrombvnCheck);
            if (string.IsNullOrEmpty(customerdatanotfrombvnCheck))
            {
                _logger.LogInformation("user id from reg ");
                string custsql = "insert into customerdatanotfrombvn (PhoneNumber, Email, Address, regid, phonenumberfrombvn) values (@PhoneNumber, @Email, @Address, @regid,@phonenumberfrombvn)";
                await con.ExecuteAsync(custsql, new { PhoneNumber = migratedCustomer.PhoneNumber, Email = string.IsNullOrEmpty(migratedCustomer.Email) ? "" : migratedCustomer.Email, Address = "", regid = checkreq.FirstOrDefault().ID, phonenumberfrombvn = migratedCustomer.PhoneNumber });
            }
            await CreateUsername("", setRegristationCredential);
            _logger.LogInformation("username created successfully ....");
            return uniRef;
            //update the otp.
           //await _genServ.InsertOtp(OtpType.Registration,checkreq.FirstOrDefault().ID,sess,otp,con);
        }

        
        public async Task<RegistrationResponse> StartRegistration(string ClientKey, RegistrationRequest Request)
        {
            try
            {
                _logger.LogInformation("Reg Request: {Request}", JsonConvert.SerializeObject(Request));

                string sess = _genServ.GetSession();
                string otp = _genServ.GenerateOtp();

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
                    var existingRegistration = (await con.QueryAsync<Registration>("SELECT * FROM registration WHERE bvn = @Bvn", new { Bvn = Request.Bvn })).FirstOrDefault();

                    if (existingRegistration != null)
                    {
                        if (!existingRegistration.ValidBvn)
                        {
                            return new RegistrationResponse { Response = EnumResponse.InvalidBvn };
                        }
                        var user = await _genServ.GetUserbyUsername(existingRegistration.Username, con);
                     //  Task.Run(async () =>
                       //   {
                               await _genServ.InsertOtp(OtpType.Registration, existingRegistration.ID, sess, otp, con);                           
                                await InsertRegistrationSession(existingRegistration.ID, sess, Request.ChannelId, con);
                         // });
                        Console.WriteLine("user "+JsonConvert.SerializeObject(user));
                        if (user == null)
                        {
                            await _genServ.SendOtp3(OtpType.Registration, otp, existingRegistration?.PhoneNumber, _smsBLService, "Registration", existingRegistration?.Email);
                        }
                        var accountCheck = await CheckCbaByBvn(Request.Bvn);
                        var passwordCheck = user != null
                            ? await con.QueryFirstOrDefaultAsync<string>(
                                "SELECT Credential FROM user_credentials WHERE UserId = @UserId AND CredentialType = 1",
                                new { UserId = user.Id })
                            : null;

                        return new RegistrationResponse
                        {
                            Email = _genServ.MaskEmail(existingRegistration.Email),
                            Success = true,
                            PhoneNumber = existingRegistration.PhoneNumber,
                            Response = EnumResponse.Successful,
                            SessionID = sess,
                            RequestReference = existingRegistration.RequestReference,
                            IsUsernameExist = user != null,
                            IsAccountNumberExist = accountCheck?.success ?? false,
                            IsPasswordExist = passwordCheck != null
                        };
                    }
                    // Insert new registration entry
                    await con.ExecuteAsync(
                        @"INSERT INTO registration (channelid, bvn, requestreference, createdon, validbvn, nin)
                  VALUES (@ChannelId, @Bvn, @UniRef, sysdate(), 1, @Nin)",
                        new { Request.ChannelId, Request.Bvn, UniRef = uniRef, Request.Nin });

                    var registrationId = await con.QuerySingleAsync<long>(
                        "SELECT id FROM registration WHERE requestreference = @UniRef", new { UniRef = uniRef });

                    var username = await con.QueryFirstOrDefaultAsync<string>(
                        "SELECT Username FROM registration WHERE requestreference = @UniRef", new { UniRef = uniRef });

                    // Check for existing CBA data
                    var cbaResponse = await CheckCbaByBvn(Request.Bvn);

                    if (cbaResponse?.success == true)
                    {
                        await con.ExecuteAsync(
                            @"UPDATE registration SET lastname = @Lastname, phonenumber = @PhoneNumber,
                      CustomerId = @CustomerId, FirstName = @FirstName, AccountOpened = 1, email = @Email, ValidBvn = 1
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
                        var usr = username != null? await _genServ.GetUserbyUsername(username, con) : null;
                        var IsPwdExists = usr != null ? (await con.QueryAsync<string>($"select Credential from user_credentials where UserId = @usrid and CredentialType=1", new { usrid = usr.Id })).FirstOrDefault() : null;
                        Task.Run(async () => { 
                           await _genServ.InsertOtp(OtpType.Registration, registrationId, sess, otp, con);
                           await InsertRegistrationSession(registrationId, sess, Request.ChannelId, con);
                           string queryCheck = "select email from customerdatanotfrombvn where email = @Email or phonenumber=@PhoneNumber";
                            var customerdatanotfrombvnCheck = (await con.QueryAsync<string>(queryCheck, new { Email = cbaResponse.result?.email, PhoneNumber = cbaResponse.result?.mobile })).FirstOrDefault();
                            _logger.LogInformation("checking mail ..." + customerdatanotfrombvnCheck);
                            if (string.IsNullOrEmpty(customerdatanotfrombvnCheck))
                            {
                                _logger.LogInformation("user id from reg ");
                                string custsql = "insert into customerdatanotfrombvn (PhoneNumber, Email, Address, regid, phonenumberfrombvn) values (@PhoneNumber, @Email, @Address, @regid,@phonenumberfrombvn)";
                                await con.ExecuteAsync(custsql, new { PhoneNumber = cbaResponse.result?.mobile, Email = string.IsNullOrEmpty(Request.Email) ? cbaResponse.result?.email : Request.Email, Address = "", regid = registrationId, phonenumberfrombvn = cbaResponse.result?.mobile });
                            }
                        });
                        return new RegistrationResponse
                        {
                            Email = _genServ.MaskEmail(cbaResponse.result.email),
                            Success = true,
                            PhoneNumber = cbaResponse.result.mobile,
                            Response = EnumResponse.Successful,
                            SessionID = sess,
                            RequestReference = uniRef,
                            IsUsernameExist = username != null,
                            IsAccountNumberExist = true,
                            IsPasswordExist = IsPwdExists!=null
                        };
                    }

                    // Validate BVN if no CBA data is found
                    var bvnDetail = await ValidateBvn(Request.Bvn, con);
                    if (!bvnDetail.Success)
                    {
                        return new RegistrationResponse { Response = EnumResponse.InvalidBvn };
                    }
                  
                    var thread = new Thread(async () =>
                    {
                        await con.ExecuteAsync($"update registration set lastname= @lastname, phonenumber = @ph, email = @em,firstname = @fn,ValidBvn = 1 where id= {registrationId}", new
                        {
                            lastname = bvnDetail.BvnDetails.Lastname,
                            ph = bvnDetail.BvnDetails.PhoneNumber,
                            em = bvnDetail.BvnDetails.Email,
                            fn = bvnDetail.BvnDetails.Firstname
                        });
                        await _genServ.InsertOtp(OtpType.Registration, registrationId, sess, otp, con);
                        await InsertRegistrationSession(registrationId, sess, Request.ChannelId, con);
                        await _genServ.SendOtp3(OtpType.Registration, otp, bvnDetail.BvnDetails?.PhoneNumber, _smsBLService, "Registration", bvnDetail.BvnDetails.Email);
                        string query = "select email from customerdatanotfrombvn where email = @Email or phonenumber=@PhoneNumber";
                        var check = (await con.QueryAsync<string>(query, new { Email = bvnDetail.BvnDetails?.Email, PhoneNumber = bvnDetail.BvnDetails?.PhoneNumber })).FirstOrDefault();
                        _logger.LogInformation("checking mail ..." + check);
                        if (string.IsNullOrEmpty(check))
                        {
                            string custsql = "insert into customerdatanotfrombvn(PhoneNumber, Email, Address, regid, phonenumberfrombvn) values (@PhoneNumber, @Email, @Address, @regid,@phonenumberfrombvn)";
                            await con.ExecuteAsync(custsql, new { PhoneNumber = bvnDetail.BvnDetails?.PhoneNumber, Email = string.IsNullOrEmpty(Request.Email) ? bvnDetail.BvnDetails?.Email : Request.Email, Address = "", regid = registrationId, phonenumberfrombvn = bvnDetail.BvnDetails?.PhoneNumber });
                        }
                    });
                    // await _genServ.InsertOtp(OtpType.Registration, registrationId, sess, otp, con);
                    //await InsertRegistrationSession(registrationId, sess, Request.ChannelId, con);
                    // Thread.Sleep(5);
                    thread.Start();
                    //Console.WriteLine("returning ......");
                    return new RegistrationResponse
                    {
                        Email = _genServ.MaskEmail(bvnDetail.BvnDetails?.Email),
                        Success = true,
                        PhoneNumber = bvnDetail.BvnDetails?.PhoneNumber,
                        Response = EnumResponse.Successful,
                        SessionID = sess,
                        RequestReference = uniRef,
                        IsUsernameExist = false,
                        IsAccountNumberExist = false,
                        IsPasswordExist = false
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in StartRegistration");
                return new RegistrationResponse { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }
       
        
        /*
        public async Task<RegistrationResponse> StartRegistration(string ClientKey, RegistrationRequest Request)
        {
            try
            {
                _logger.LogInformation("Reg Request " + JsonConvert.SerializeObject(Request));
                string sess = _genServ.GetSession();
                string otp = _genServ.GenerateOtp();
                string pattern = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";
                //  bool allDigits = Request.PhoneNumber.All(char.IsDigit);
                 Console.WriteLine(Regex.IsMatch(Request.Email, pattern));
                if (!string.IsNullOrEmpty(Request.Email))
                {
                    if (!Regex.IsMatch(Request.Email, pattern))
                    {
                        return new RegistrationResponse() { Response = EnumResponse.InvalidEmail };
                    }
                }
                var uniRef = $"Reg{new Random().Next(11111, 99999)}{DateTime.Now.ToString("ddMMyyyyHHmm")}";
                if (Request.ChannelId == 0)
                    Request.ChannelId = 1;
                Console.WriteLine($"session {sess},otp {otp} uniref {uniRef}");
                using (IDbConnection con = _context.CreateConnection())
                {
                    if (!string.IsNullOrEmpty(Request.Nin))
                    { // cos nin is optional
                        GenericResponse res = await _genServ.ValidateNin(Request.Nin, con, Request.Bvn);
                        _logger.LogInformation("res " + JsonConvert.SerializeObject(res));
                        if (!res.Success)
                        {
                            return new RegistrationResponse() { Response = res.Response, Success = res.Success };
                        }
                    }
                    var checkreq = await con.QueryAsync<Registration>($"SELECT * FROM registration where bvn = @bnvs", new { bnvs = Request.Bvn });
                    Console.WriteLine($"checkreq ${checkreq}");
                    if (checkreq.Any())
                    {
                        //var checkusername = (await con.QueryAsync<string>($"select username from registration where requestreference = @reqRef", new { reqRef = uniRef })).FirstOrDefault();
                        if (!checkreq.FirstOrDefault().ValidBvn)
                        {
                            Console.WriteLine("returning from here ");
                            return new RegistrationResponse() { Response = EnumResponse.InvalidBvn };
                        }
                        await _genServ.InsertOtp(OtpType.Registration, checkreq.FirstOrDefault().ID, sess, otp, con);
                        //await _genServ.SendOtp(OtpType.Registration, otp, checkreq.FirstOrDefault().PhoneNumber, checkreq.FirstOrDefault().Email);
                        await _genServ.SendOtp3(OtpType.Registration, otp, checkreq.FirstOrDefault().PhoneNumber,_smsBLService,"Registration",checkreq.FirstOrDefault().Email);
                        _logger.LogInformation("got here after sending otp ...." + checkreq.FirstOrDefault().PhoneNumber);
                        await InsertRegistrationSession(checkreq.FirstOrDefault().ID, sess, Request.ChannelId, con);
                        var checkUsr = await _genServ.GetUserbyUsername(checkreq.FirstOrDefault().Username, con);
                        var checkforAccountNUmber = await CheckCbaByBvn(Request.Bvn);
                        var checkPassword = checkUsr != null ? (await con.QueryAsync<string>($"select Credential from user_credentials where UserId = @usrid and CredentialType=1", new { usrid = checkUsr.Id })).FirstOrDefault() : null;
                        return new RegistrationResponse() { Email = _genServ.MaskEmail(checkreq.FirstOrDefault().Email), Success = true, PhoneNumber = checkreq.FirstOrDefault().PhoneNumber, Response = EnumResponse.Successful, SessionID = sess, RequestReference = checkreq.FirstOrDefault().RequestReference, IsUsernameExist = checkUsr != null ? true : false, IsAccountNumberExist = checkforAccountNUmber != null ? checkforAccountNUmber.success : checkforAccountNUmber.success, IsPasswordExist = checkPassword != null ? true : false };
                    }
                    // Console.WriteLine($"requestreference {uniRef}");
                    _logger.LogWarning($"requestreference {uniRef}");
                    string sql = $@"insert into registration (channelid, bvn, requestreference, createdon,validbvn,nin)
                              values ({Request.ChannelId},@bvs,'{uniRef}',sysdate(),1,'{Request.Nin}')";
                    await con.ExecuteAsync(sql, new { bvs = Request.Bvn });
                    //Console.WriteLine("query successsful");
                    var regId = await con.QueryAsync<long>($"select id from registration where requestreference = @reqRef", new { reqRef = uniRef });
                    long regIds = regId.FirstOrDefault();
                    var username = (await con.QueryAsync<string>($"select Username from registration where requestreference = @reqRef", new { reqRef = uniRef })).FirstOrDefault();
                    // long regIds = regId.FirstOrDefault();
                    Console.WriteLine("regIds ", regIds);
                    _logger.LogWarning($"regIds {regIds}");
                    _logger.LogWarning($"username {username}");
                    var resp = await CheckCbaByBvn(Request.Bvn);
                    if (resp != null)
                    {
                        if (resp.success && resp.result != null)
                        {
                            await con.ExecuteAsync($"update registration set lastname= @lastname, phonenumber = @phn, CustomerId=@cust,FirstName=@fnm,AccountOpened = 1,email=@emi,ValidBvn = 1 where id = {regIds}", new
                            {
                                lastname = resp.result.lastname != null ? resp.result.lastname : "",
                                phn = resp.result.mobile,
                                cust = resp.result.customerID,
                                fnm = resp.result.firstname,
                                emi = resp.result.email
                            });
                            var checkUsr2 = await _genServ.GetUserbyUsername(username, con);
                            // var checkforAccountNUmber = await CheckCbaByBvn(Request.Bvn);
                            var checkPassword2 = checkUsr2 != null ? (await con.QueryAsync<string>($"select Credential from user_credentials where UserId = @usrid and CredentialType=1", new { usrid = checkUsr2.Id })).FirstOrDefault() : null;
                            await _genServ.InsertOtp(OtpType.Registration, regIds, sess, otp, con);
                            // await _genServ.SendOtp(OtpType.Registration, otp, resp.result.mobile, resp.result.email);
                            Task.Run(async () => {
                                if (checkUsr2==null) { 
                                  await _genServ.SendOtp3(OtpType.Registration, otp, checkreq.FirstOrDefault().PhoneNumber, _smsBLService, "Registration", checkreq.FirstOrDefault().Email);
                                  }
                                await InsertRegistrationSession(regIds, sess, Request.ChannelId, con);
                                string queryCheck = "select email from customerdatanotfrombvn where email = @Email or phonenumber=@PhoneNumber";
                                var customerdatanotfrombvnCheck = (await con.QueryAsync<string>(queryCheck, new { Email = resp.result?.email, PhoneNumber = resp.result?.mobile })).FirstOrDefault();
                                _logger.LogInformation("checking mail ..." + customerdatanotfrombvnCheck);
                                if (string.IsNullOrEmpty(customerdatanotfrombvnCheck))
                                {
                                    _logger.LogInformation("user id from reg ");
                                    string custsql = "insert into customerdatanotfrombvn (PhoneNumber, Email, Address, regid, phonenumberfrombvn) values (@PhoneNumber, @Email, @Address, @regid,@phonenumberfrombvn)";
                                    await con.ExecuteAsync(custsql, new { PhoneNumber = resp.result?.mobile, Email = string.IsNullOrEmpty(Request.Email) ? resp.result?.email : Request.Email, Address = "", regid = regIds, phonenumberfrombvn = resp.result?.mobile });
                                }
                            });                           
                            return new RegistrationResponse() { Email = _genServ.MaskEmail(resp.result.email), Success = true, PhoneNumber = resp.result.mobile, Response = EnumResponse.Successful, SessionID = sess, RequestReference = uniRef, IsUsernameExist = checkUsr2 != null ? true : false, IsAccountNumberExist = resp != null ? resp.success : resp.success, IsPasswordExist = checkPassword2 != null ? true : false };
                        }
                    }
                    _logger.LogInformation("consider using the next validatebvn endpoint ......");
                    //for new or fresh customer I suppose when reading this code cos I wasnt the writer.
                    var bvn_detail = await ValidateBvn(Request.Bvn, con);
                    // Console.WriteLine($"bvn_detail.Success {bvn_detail} and ");
                    if (!bvn_detail.Success)
                        return new RegistrationResponse() { Response = EnumResponse.InvalidBvn };
                    var usr = await _genServ.GetUserbyUsername(username, con);
                    Task.Run(async () =>
                    {
                        if (usr==null)
                        {
                            Console.WriteLine("sending otp async");
                            _logger.LogInformation("sending otp async");
                            await _genServ.SendOtp3(OtpType.Registration, otp,bvn_detail.BvnDetails.PhoneNumber, _smsBLService, "Registration", bvn_detail.BvnDetails.Email);
                            _logger.LogInformation("otp sent otp async");
                        }
                    });
                    new Thread(async () => {
                        await _genServ.InsertOtp(OtpType.Registration, regIds, sess, otp, con);
                        // await _genServ.SendOtp(OtpType.Registration, otp, bvn_detail.BvnDetails.PhoneNumber, bvn_detail.BvnDetails.Email);
                        _logger.LogInformation(" BvnDetails " + JsonConvert.SerializeObject(bvn_detail.BvnDetails));
                        await InsertRegistrationSession(regIds, sess, Request.ChannelId, con);
                        await con.ExecuteAsync($"update registration set lastname= @lastname, phonenumber = @ph, email = @em,firstname = @fn,ValidBvn = 1 where id= {regIds}", new
                        {
                            lastname = bvn_detail.BvnDetails.Lastname,
                            ph = bvn_detail.BvnDetails.PhoneNumber,
                            em = bvn_detail.BvnDetails.Email,
                            fn = bvn_detail.BvnDetails.Firstname
                        });
                        Console.WriteLine("new user registered ....");
                        _logger.LogInformation("new user registered ....");
                        //profiling the data in customerdatanotbvn 
                        string Address = _genServ.RemoveSpecialCharacters(Request.Address);
                        string query = "select email from customerdatanotfrombvn where email = @Email or phonenumber=@PhoneNumber";
                        var check = (await con.QueryAsync<string>(query, new { Email = bvn_detail.BvnDetails?.Email, PhoneNumber = bvn_detail.BvnDetails?.PhoneNumber })).FirstOrDefault();
                        _logger.LogInformation("checking mail ..." + check);
                        _logger.LogInformation("checking usr " + usr);
                        if (string.IsNullOrEmpty(check))
                        {
                            _logger.LogInformation("user id from reg ");
                            string custsql = "insert into customerdatanotfrombvn (PhoneNumber, Email, Address, regid, phonenumberfrombvn) values (@PhoneNumber, @Email, @Address, @regid,@phonenumberfrombvn)";
                            await con.ExecuteAsync(custsql, new { PhoneNumber = bvn_detail.BvnDetails.PhoneNumber, Email = string.IsNullOrEmpty(Request.Email) ? bvn_detail.BvnDetails?.Email : Request.Email, Address = "", regid = regIds, phonenumberfrombvn = bvn_detail.BvnDetails.PhoneNumber });
                        }
                    }).Start();                
                    // BvnPhoneNumber = _genServ.MaskPhone(bvn_detail.BvnDetails.PhoneNumber),
                    var checkPassword3 = usr != null ? (await con.QueryAsync<string>($"select Credential from user_credentials where UserId = @usrid and CredentialType=1", new { usrid = usr.Id })).FirstOrDefault() : null;
                    return new RegistrationResponse()
                    {
                        Email = _genServ.MaskEmail(bvn_detail.BvnDetails?.Email),
                        Success = true,
                        PhoneNumber = bvn_detail.BvnDetails.PhoneNumber,
                        Response = EnumResponse.Successful,
                        SessionID = sess,
                        RequestReference = uniRef,
                        IsUsernameExist = usr != null ? true : false,
                        IsAccountNumberExist = resp != null ? resp.success : false,
                        IsPasswordExist = checkPassword3 != null ? true : false
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new RegistrationResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }
       */ 
        private async Task<FinedgeSearchBvn> CheckCbaByBvn(string Bvn)
        {
            try
            {
                var header = new Dictionary<string, string>
                    {
                        { "ClientKey", _settings.FinedgeKey }
                    };
                var resp = await _genServ.CallServiceAsync<FinedgeSearchBvn>(Method.GET, $"{_settings.FinedgeUrl}api/enquiry/SearchCustomerbyBvn/{Bvn}", null, true, header);
                Console.WriteLine($" resp.success {resp}");
                return resp;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return null;
            }
        }

        private async Task<NinPreamblyResponse> CheckCbaByNin(string Nin, string token)
        {
            try
            {
                var header = new Dictionary<string, string>
                    {
                        { "Authorization",$"Bearer {token}"},
                    };
                // call the nin endpoint
                var resp = await _genServ.CallServiceAsync<NinPreamblyResponse>(Method.GET, $"{_settings.NinEndPoint}api/v1/verification/nin/{Nin}", null, true, header);
                Console.WriteLine($" resp.success {resp.message}");
                return resp;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return null;
            }
        }


        public async Task<ValidateBvn> CheckAssetCapitalInsuranceBvn(string Bvn, IDbConnection con)
        {
            try
            {
                //var bvn = await GetBvn(Bvn, con);
                var bvn = await GetAssetCapitalInsuranceBvn(Bvn, con);
                if (bvn != null)
                {
                    var bvndetails = new BvnResp()
                    {
                        Firstname = bvn.FIRSTNAME,
                        Middlename = bvn.MIDDLENAME,
                        Lastname = bvn.LASTNAME,
                        Email = bvn.EMAIL,
                        PhoneNumber = bvn.PHONENUMBER
                    };

                    return new ValidateBvn()
                    {
                        BvnDetails = bvndetails,
                        Response = EnumResponse.Successful,
                        Success = true
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new ValidateBvn() { Response = EnumResponse.SystemError };
            }
            return new ValidateBvn()
            {
                BvnDetails = null,
                Response = EnumResponse.NotSuccessful,
                Success = false
            };
        }

        public async Task<ValidateBvn> ValidateAssetCapitalInsuranceBvn(string Bvn, IDbConnection con)
        {
            try
            {
                //var bvn = await GetBvn(Bvn, con);
                var bvn =  await GetAssetCapitalInsuranceBvn(Bvn, con);
                if (bvn != null)
                {
                    var bvndetails = new BvnResp()
                    {
                        Firstname = bvn.FIRSTNAME,
                        Middlename = bvn.MIDDLENAME,
                        Lastname = bvn.LASTNAME,
                        Email = bvn.EMAIL,
                        PhoneNumber = bvn.PHONENUMBER
                    };

                    return new ValidateBvn()
                    {
                        BvnDetails = bvndetails,
                        Response = EnumResponse.Successful,
                        Success = true
                    };
                }

                var header = new Dictionary<string, string>
                {
                    { "ClientKey", _settings.BvnKey }
                };
                _logger.LogInformation("going to call raw bvn endpoint ..");
                var resp = await _genServ.CallServiceAsync<BvnResponse>(Method.GET, $"{_settings.BvnUrl}api/bvn/ValidateBvnFull/{Bvn}", null, true, header);

                if (resp.ResponseCode != "00")
                    return new ValidateBvn() { Response = EnumResponse.NotSuccessful };
                _logger.LogInformation("bvn inserted ..");
                await InsertAssetCapitalInsuranceBvn(resp, con);
                Console.WriteLine("bvn response success ..");
                return new ValidateBvn()
                {
                    Response = EnumResponse.Successful,
                    Success = true,
                    BvnDetails = new BvnResp()
                    {
                        Firstname = resp.firstName,
                        Middlename = resp.middleName,
                        Lastname = resp.lastName,
                        Email = resp.email,
                        PhoneNumber = resp.phoneNumber
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new ValidateBvn() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<ValidateBvn> ValidateBvn(string Bvn, IDbConnection con)
        {
            try
            {
                var bvn = await GetBvn(Bvn, con);
                if (bvn != null)
                {
                    var bvndetails = new BvnResp()
                    {
                        Firstname = bvn.FIRSTNAME,
                        Middlename = bvn.MIDDLENAME,
                        Lastname = bvn.LASTNAME,
                        Email = bvn.EMAIL,
                        PhoneNumber = bvn.PHONENUMBER
                    };

                    return new ValidateBvn()
                    {
                        BvnDetails = bvndetails,
                        Response = EnumResponse.Successful,
                        Success = true
                    };
                }

                var header = new Dictionary<string, string>
                {
                    { "ClientKey", _settings.BvnKey }
                };
                _logger.LogInformation("going to call raw bvn endpoint ..");
                var resp = await _genServ.CallServiceAsync<BvnResponse>(Method.GET, $"{_settings.BvnUrl}api/bvn/ValidateBvnFull/{Bvn}", null, true, header);

                if (resp.ResponseCode != "00")
                    return new ValidateBvn() { Response = EnumResponse.NotSuccessful };
                _logger.LogInformation("bvn inserted ..");
                await InsertBvn(resp, con);
                Console.WriteLine("response success ..");
                return new ValidateBvn()
                {
                    Response = EnumResponse.Successful,
                    Success = true,
                    BvnDetails = new BvnResp()
                    {
                        Firstname = resp.firstName,
                        Middlename = resp.middleName,
                        Lastname = resp.lastName,
                        Email = resp.email,
                        PhoneNumber = resp.phoneNumber
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new ValidateBvn() { Response = EnumResponse.SystemError };
            }
        }

        private async Task InsertAssetCapitalInsuranceBvn(BvnResponse BvnDetails, IDbConnection con)
        {
            try
            {
                string sql = $@"INSERT INTO asset_capital_insurance_bvn_validation (BVN,PhoneNumber,PhoneNumber2,Email,Gender,LgaResidence,
                LgaOrigin,MaritalStatus,Nationality,ResidentialAddress,StateOrigin,StateResidence,
                DOB,DateCreated,FirstName,MiddleName,LastName) VALUES (@bvn,@phn,@phn2,@emi,@gen,@lga,@lgOrig,@marit,@nati,@res,@stat,@staReg,@dob,sysdate(),@fname,@mname,@lname)";
                await con.ExecuteAsync(sql, new
                {
                    bvn = BvnDetails.bvn,
                    phn = BvnDetails.phoneNumber,
                    phn2 = BvnDetails.secondaryPhoneNumber,
                    emi = BvnDetails.email,
                    gen = BvnDetails.gender,
                    lga = BvnDetails.lgaOfResidence,
                    lgOrig = BvnDetails.lgaOfOrigin,
                    marit = BvnDetails.maritalStatus,
                    nati = BvnDetails.nationality,
                    res = BvnDetails.residentialAddress,
                    stat = BvnDetails.stateOfOrigin,
                    staReg = BvnDetails.stateOfResidence,
                    dob = _genServ.ConvertDatetime(BvnDetails.dateOfBirth),
                    fname = BvnDetails.firstName,
                    mname = BvnDetails.middleName,
                    lname = BvnDetails.lastName
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
            }
        }

        private async Task InsertBvn(BvnResponse BvnDetails, IDbConnection con)
        {
            try
            {
                
                string sql = $@"INSERT INTO bvn_validation (BVN,PhoneNumber,PhoneNumber2,Email,Gender,LgaResidence,
                LgaOrigin,MaritalStatus,Nationality,ResidentialAddress,StateOrigin,StateResidence,
                DOB,DateCreated,FirstName,MiddleName,LastName) VALUES (@bvn,@phn,@phn2,@emi,@gen,@lga,@lgOrig,@marit,@nati,@res,@stat,@staReg,@dob,sysdate(),@fname,@mname,@lname)";
                await con.ExecuteAsync(sql, new
                {
                    bvn = BvnDetails.bvn,
                    phn = BvnDetails.phoneNumber,
                    phn2 = BvnDetails.secondaryPhoneNumber,
                    emi = BvnDetails.email,
                    gen = BvnDetails.gender,
                    lga = BvnDetails.lgaOfResidence,
                    lgOrig = BvnDetails.lgaOfOrigin,
                    marit = BvnDetails.maritalStatus,
                    nati = BvnDetails.nationality,
                    res = BvnDetails.residentialAddress,
                    stat = BvnDetails.stateOfOrigin,
                    staReg = BvnDetails.stateOfResidence,
                    dob = _genServ.ConvertDatetime(BvnDetails.dateOfBirth),
                    fname = BvnDetails.firstName,
                    mname = BvnDetails.middleName,
                    lname = BvnDetails.lastName
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
            }
        }

        private async Task<BvnValidation> GetBvn(string Bvn, IDbConnection con)
        {
            try
            {
                var resp = await con.QueryAsync<BvnValidation>($"SELECT * FROM bvn_validation where bvn = @bvns", new { bvns = Bvn });
                return resp.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return null;
            }
        }

        private async Task<BvnValidation> GetAssetCapitalInsuranceBvn(string Bvn, IDbConnection con)
        {
            try
            {
                var resp = await con.QueryAsync<BvnValidation>($"SELECT * FROM asset_capital_insurance_bvn_validation where bvn = @bvns", new { bvns = Bvn });
                return resp.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return null;
            }
        }

        public async Task<BvnSubDetails> ValidateDob(string ClientKey, SetRegristationCredential Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var getReg = await ValidateRegSession(Request.ChannelId, Request.Session, con);
                    Console.WriteLine(" getReg " + getReg);
                    if (getReg == 0)
                        return new BvnSubDetails() { Response = EnumResponse.InvalidRegSession };
                   // var bvndetails = await con.QueryAsync<BvnValidation>($"select * from bvn_validation where bvn_validation.bvn = (select bvn from registration where registration.id=(select id from registration where registration.requestReference='{Request.RequestReference}'))");
                   // _logger.LogInformation("Request.SecretValue " + Request.SecretValue);
                    var query = @"
                                SELECT *
                                FROM bvn_validation
                                WHERE bvn = (
                                    SELECT bvn
                                    FROM registration
                                    WHERE id = (
                                        SELECT id
                                        FROM registration
                                        WHERE requestReference = @RequestReference
                                    )
                                )";
                    var bvndetails = await con.QueryAsync<BvnValidation>(query, new { RequestReference = Request.RequestReference });
                    if(!bvndetails.Any())
                    {
                        return new BvnSubDetails() { Response = EnumResponse.DobNotFound};
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

        public async Task<RegistrationResponse> ResendOtp(string ClientKey, GenericRegRequest Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var getReg = await ValidateRegSession(Request.ChannelId, Request.Session, con);
                    if (getReg == 0)
                        return new RegistrationResponse() { Response = EnumResponse.InvalidRegSession };
                    string otp = _genServ.GenerateOtp();
                    // await con.ExecuteAsync($"update otp_session set status = 0 where otp_type = {(int)OtpType.Registration} and objId = {getReg}");
                    await _genServ.InsertOtp(OtpType.Registration, getReg, Request.Session, otp, con);
                    //var userId = (await con.QueryAsync<int>("select id from users where username=(select username from registration where RequestReference=@RequestReference)", new {RequestReference=Request.RequestReference})).FirstOrDefault();
                    var userId = (await con.QueryAsync<int?>(
                                    "SELECT id FROM users WHERE username = (SELECT username FROM registration WHERE RequestReference = @RequestReference)",
                                    new { RequestReference = Request.RequestReference }
                                )).FirstOrDefault();
                    CustomerDataNotFromBvn customerDataNotFromBvn = null;
                    if (userId.HasValue)
                    {
                        customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, userId.Value);
                    }
                    // Retrieve PhoneNumber with FirstOrDefault to handle cases where the query might return no result.
                    var phoneNumber = (await con.QueryAsync<string>(
                        "SELECT PhoneNumber FROM registration WHERE RequestReference = @RequestReference",
                        new { RequestReference = Request.RequestReference }
                    )).FirstOrDefault();
                    // Determine the correct phone number to use
                    var targetPhoneNumber = customerDataNotFromBvn?.PhoneNumber ?? phoneNumber;
                    // Send OTP if targetPhoneNumber is valid
                    if (!string.IsNullOrEmpty(targetPhoneNumber))
                    {
                        await _genServ.SendOtp3(OtpType.Registration, otp, targetPhoneNumber, _smsBLService, "Registration",customerDataNotFromBvn.Email);
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

        public async Task<RegistrationResponse> ResendOtpToPhoneNumber(string ClientKey, GenericRegRequest2 Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var getReg = await ValidateRegSession(Request.ChannelId, Request.Session, con);
                    if (getReg == 0)
                        return new RegistrationResponse() { Response = EnumResponse.InvalidRegSession };
                    string otp = _genServ.GenerateOtp2();
                    await con.ExecuteAsync("update otp_session set OTP=@otp where Session=@sess", new { otp=otp,sess=Request.Session});
                    _logger.LogInformation("otp updated ..");
                    var userId = (await con.QueryAsync<int?>(
                                 "SELECT id FROM users WHERE username = (SELECT username FROM registration WHERE RequestReference = @RequestReference)",
                                 new { RequestReference = Request.RequestReference }
                             )).FirstOrDefault();
                    CustomerDataNotFromBvn customerDataNotFromBvn = null;
                    if (userId.HasValue)
                    {
                        customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, userId.Value);
                    }
                    // await con.ExecuteAsync($"update otp_session set status = 0 where otp_type = {(int)OtpType.Registration} and objId = {getReg}");
                    var PhoneNumberCheker = (await con.QueryAsync<string>("select PhoneNumber from customerdatanotfrombvn where PhoneNumber=@PhoneNumber", new { PhoneNumber = Request.PhoneNumber })).FirstOrDefault();
                    if (!string.IsNullOrEmpty(PhoneNumberCheker))
                    {
                        _logger.LogInformation("PhoneNumberCheker "+ PhoneNumberCheker);
                        Task.Run(async () =>
                        {
                            await _genServ.SendOtp3(OtpType.Registration, otp, PhoneNumberCheker, _smsBLService, "Registration", customerDataNotFromBvn?.Email);
                        });
                        return new RegistrationResponse() { Success = true, Response = EnumResponse.Successful };
                    }
                    await con.ExecuteAsync($"update customerdatanotfrombvn set PhoneNumber = @PhoneNumber where regid = {getReg}", new { PhoneNumber = Request.PhoneNumber }); //to update incase they provide alternative PhoneNumber
                    await _genServ.InsertOtp(OtpType.Registration, getReg, Request.Session, otp, con);
                    var phoneNumber = (await con.QueryAsync<string>(
                        "SELECT PhoneNumber FROM registration WHERE RequestReference = @RequestReference",
                        new { RequestReference = Request.RequestReference }
                    )).FirstOrDefault();

                    // Determine the correct phone number to use
                    var targetPhoneNumber = customerDataNotFromBvn?.PhoneNumber ?? phoneNumber;
                    // Send OTP if targetPhoneNumber is valid
                    if (!string.IsNullOrEmpty(targetPhoneNumber))
                    {
                        await _genServ.SendOtp3(OtpType.Registration, otp, targetPhoneNumber, _smsBLService, "Registration",customerDataNotFromBvn.Email);
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

        public async Task<AccountOpeningResponse> OpenAccount(string ClientKey, GenericRegRequest Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var getReg = await ValidateRegSession(Request.ChannelId, Request.Session, con);
                    if (getReg == 0)
                        return new AccountOpeningResponse() { Response = EnumResponse.InvalidRegSession };

                    var chkOtp = await _genServ.ValidateSessionOtp(OtpType.Registration, Request.Session, con);
                    // Console.WriteLine("chkOtp " + chkOtp?.Session);
                    _logger.LogInformation("Session " + chkOtp?.Session);
                    if (chkOtp == null)
                        return new AccountOpeningResponse() { Response = EnumResponse.OtpNotValidated };
                    // if (chkOtp != null)
                    //    return new AccountOpeningResponse() { Response = EnumResponse.OtpNotValidated };
                    // var bvndetails = await con.QueryAsync<BvnValidation>($"select * from bvn_validation where bvn = (select bvn from registration where id = {getReg})");
                    Console.WriteLine("issuing query .....");
                    var bvndetails = await con.QueryAsync<BvnValidation>($"select * from bvn_validation where bvn_validation.bvn = (select bvn from registration where registration.id=(select id from registration where registration.requestReference='{Request.RequestReference}'))");
                    Console.WriteLine("query successful .....");
                    if (bvndetails == null)
                        return new AccountOpeningResponse() { Response = EnumResponse.InvalidBvn };

                    var request = new FinedgeAccountOpeningRequest()
                    {
                        FirstName = bvndetails.FirstOrDefault().FIRSTNAME,
                        OtherName = bvndetails.FirstOrDefault().MIDDLENAME,
                        Surname = bvndetails.FirstOrDefault().LASTNAME,
                        DOB = bvndetails.FirstOrDefault().DOB.GetValueOrDefault().ToString("dd/MMM/yyyy"),
                        Gender = bvndetails.FirstOrDefault().GENDER.StartsWith('M') ? 1 : 2,
                        PhoneNumber = !string.IsNullOrEmpty(bvndetails.FirstOrDefault().PHONENUMBER)? bvndetails.FirstOrDefault().PHONENUMBER:"",
                        Address = bvndetails.FirstOrDefault().RESIDENTIALADDRESS + " " + bvndetails.FirstOrDefault().LgaResidence + " " + bvndetails.FirstOrDefault().StateResidence,
                        BVN = bvndetails.FirstOrDefault().BVN,
                        Email = !string.IsNullOrEmpty(bvndetails.FirstOrDefault().EMAIL)? bvndetails.FirstOrDefault().EMAIL:"",
                        ProductCode = _settings.ProductCode,
                        BranchCode=_settings.BranchCode,
                        Title = bvndetails.FirstOrDefault().GENDER.StartsWith('M') ? 1 : 2,
                        IDType = 5,
                        IDCardNo = bvndetails.FirstOrDefault().BVN.ToString()
                    };
                    _logger.LogInformation("FinedgeAccountOpeningRequest ....." + JsonConvert.SerializeObject(request).ToString());
                    var header = new Dictionary<string, string>
                    {
                        { "ClientKey", _settings.FinedgeKey }
                    };
                    _logger.LogInformation(" header....." + header.ToString() + " url " + _settings.FinedgeUrl + "api/customer/OpenAccount");
                    var opnAcct = await _genServ.CallServiceAsync<FinedgeAccountOpeningMessage>(Method.POST, $"{_settings.FinedgeUrl}api/customer/OpenAccount", request, true, header);
                    _logger.LogInformation($"openAcct "+ JsonConvert.SerializeObject(opnAcct));
                    if (opnAcct.Success)
                    {
                        var id = await con.QueryAsync<long>($"select id from registration where registration.requestReference = '{Request.RequestReference}'");
                        // await con.ExecuteAsync($"update registration set customerid = '{opnAcct.CustomerId}', accountopened = 1 where id = {getReg}");
                        var CustomerId = opnAcct.CustomerId != null ? opnAcct.CustomerId : "";// but this shd not returned null
                        Console.WriteLine($"customerid {CustomerId}");
                        var id2 = id.FirstOrDefault();
                        Console.WriteLine($"id {id2}");
                        Task.Run(async () =>
                        {
                            var phoneNumber = (await con.QueryAsync<string>($"select PhoneNumber from customerdatanotfrombvn where regid=(select id from registration where registration.requestReference = '{Request.RequestReference}')")).FirstOrDefault();
                            _logger.LogInformation("phoneNumber " + phoneNumber);
                            phoneNumber = !string.IsNullOrEmpty(phoneNumber) ? phoneNumber : bvndetails.FirstOrDefault().PHONENUMBER;
                            var msg = $@"Dear {bvndetails.FirstOrDefault().FIRSTNAME},your generated AccountNumber is {opnAcct.AccountNumber}.Thank you for banking with us.Kindly proceed to upgrade for seamless transaction.";
                            GenericResponse response = await _smsBLService.SendSmsNotificationToCustomer("Otp","234"+phoneNumber.Substring(1), $@"{msg}","AccountNumber Creation", _settings.SmsUrl);
                            _logger.LogInformation("response " + response.ResponseMessage+ " message "+response.Message);
                        });
                        await con.ExecuteAsync($"update registration set Customerid = '{CustomerId}', accountopened = 1 where requestReference = '{Request.RequestReference}'");
                        return new AccountOpeningResponse() { AccountNumber = opnAcct.AccountNumber, Response = EnumResponse.Successful, Success = true };
                    }
                    return new AccountOpeningResponse() { Response = EnumResponse.NotSuccessful };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new AccountOpeningResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<ValidateOtpResponse> ValidateOtp(string ClientKey, ValidateOtp Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var getReg = await ValidateRegSession(Request.ChannelId, Request.Session, con);
                    Console.WriteLine($"getReg {getReg}");
                    //if (getReg == 0)
                    //  return new ValidateOtpResponse() { Response = EnumResponse.InvalidRegSession };
                    if (getReg == 0)
                        return new ValidateOtpResponse() { Response = EnumResponse.RestartRegistration };

                    var resp = await _genServ.ValidateSessionOtp(OtpType.Registration, Request.Session, con);
                    Console.WriteLine($"resp {resp}");
                    if (resp == null || resp.OTP != Request.Otp)
                        return new ValidateOtpResponse() { Response = EnumResponse.InvalidOtp };
                    //  await con.ExecuteAsync($"update otp_session set status = 0 where id = {resp.ID}");
                    Console.WriteLine($"id of user {resp.ObjId}");
                    var reg = await con.QueryAsync<Registration>($"select * from registration where id = {resp.ObjId}");
                    // Console.WriteLine($" reg.FirstOrDefault().ID {reg.FirstOrDefault().BVN} {reg.FirstOrDefault().ID} {reg.FirstOrDefault().FirstName} {reg.FirstOrDefault().PhoneNumber}");                   
                    var checkcbaforbvn = await CheckCbaByBvn(reg.FirstOrDefault().BVN);
                    Console.WriteLine($"checkcbaforbvn {checkcbaforbvn}");
                    if (checkcbaforbvn == null || !checkcbaforbvn.success)
                    {
                        Console.WriteLine("check for bvn ....");
                        return new ValidateOtpResponse() { Success = true, Response = EnumResponse.Successful };
                    }
                    var checkprofile = await con.QueryAsync<Users>($"select * from users where bvn = @bv", new { bv = reg.FirstOrDefault().BVN });
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



        public async Task<GenericResponse> CreateUsername(string ClientKey, SetRegristationCredential Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var getReg = await ValidateRegSession(Request.ChannelId, Request.Session, con);
                    if (getReg == 0)
                        return new GenericResponse() { Response = EnumResponse.InvalidRegSession };

                    var chkOtp = await _genServ.ValidateSessionOtp(OtpType.Registration, Request.Session, con);
                    // if (chkOtp != null)
                    //    return new GenericResponse() { Response = EnumResponse.OtpNotValidated };
                    if (chkOtp == null)
                        return new GenericResponse() { Response = EnumResponse.OtpNotValidated };
                    var chkuser = await ValidateUsername(Request.SecretValue, con);
                    if (!chkuser.Success)
                        return chkuser;
                    //await con.ExecuteAsync($"update registration set username=@usr where id = {getReg}", new { usr = Request.SecretValue.Trim().ToLower() });
                    await con.ExecuteAsync($"update registration set username=@usr where requestReference=@requestReference", new { requestReference = Request.RequestReference, usr = Request.SecretValue.Trim().ToLower() });
                    await con.ExecuteAsync("update customerdatanotfrombvn set username=@username where regid=(select id from registration where requestReference=@Requestref)", new { Requestref = Request.RequestReference, username = Request.SecretValue.Trim().ToLower() });
                    return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> ValidateUsername(string ClientKey, string Username)
        {
            try
            {
                if (string.IsNullOrEmpty(Username))
                    return new GenericResponse() { Response = EnumResponse.InvalidDetails };

                foreach (char c in Username)
                    if (!Char.IsLetterOrDigit(c))
                        return new GenericResponse() { Response = EnumResponse.UsernameStringDigitOnly };

                using (IDbConnection con = _context.CreateConnection())
                    return await ValidateUsername(Username, con);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        private async Task<GenericResponse> ValidateUsername(string Username, IDbConnection con)
        {
            try
            {
                var usr = await con.QueryAsync<long>($"SELECT id FROM users where lower(username) = @usrs", new { usrs = Username.ToLower().Trim() });
                if (usr.Any())
                    return new GenericResponse() { Response = EnumResponse.UsernameAlreadyExist };
                var usr2 = await con.QueryAsync<long>($"select id from registration where lower(username)=@uss", new { uss = Username.ToLower().Trim() });
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

        public async Task<GenericResponse> CreatePassword(string ClientKey, SavePasswordRequest Request)
        {
            try
            {
                if (string.IsNullOrEmpty(Request.SecretValue))
                    return new GenericResponse() { Response = EnumResponse.InvalidDetails };

                if (!_genServ.CheckPasswordCondition(Request.SecretValue))
                    return new GenericResponse() { Response = EnumResponse.PasswordConditionNotMet };

                using (IDbConnection con = _context.CreateConnection())
                {
                    var getReg = await ValidateRegSession(Request.ChannelId, Request.Session, con);
                    if (getReg == 0)
                        return new GenericResponse() { Response = EnumResponse.InvalidRegSession };

                    var chkOtp = await _genServ.ValidateSessionOtp(OtpType.Registration, Request.Session, con);
                    //if (chkOtp != null)
                    //  return new GenericResponse() { Response = EnumResponse.OtpNotValidated };
                    if (chkOtp == null)
                        return new GenericResponse() { Response = EnumResponse.OtpNotValidated };
                    if (string.IsNullOrEmpty(Request.DeviceId))
                        return new GenericResponse() { Response = EnumResponse.DeviceIdRequired };
                    //  await con.ExecuteAsync($"update registration set password=@psd,deviceId=@dv,devicename=@dvn where id ={getReg}", new { psd = _genServ.EncryptString(Request.SecretValue), dv = Request.DeviceId, dvn = Request.DeviceName });
                    _logger.LogInformation("create password " + _genServ.EncryptString(Request.SecretValue)+" ref "+Request.RequestReference);
                    await con.ExecuteAsync($"update registration set password=@psd,deviceId=@dv,devicename=@dvn where requestReference ='{Request.RequestReference}'", new { psd = _genServ.EncryptString(Request.SecretValue), dv = Request.DeviceId, dvn = Request.DeviceName });
                    return new GenericResponse() { Response = EnumResponse.Successful, Success = true };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> CreateTransPin(string ClientKey, SetRegristationCredential Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var getReg = await ValidateRegSession(Request.ChannelId, Request.Session, con);
                    //if (getReg == 0)
                    //  return new GenericResponse() { Response = EnumResponse.InvalidRegSession};
                    if (getReg == 0)
                        return new GenericResponse() { Response = EnumResponse.RestartRegistration };

                    var chkOtp = await _genServ.ValidateSessionOtp(OtpType.Registration, Request.Session, con);
                    // if (chkOtp != null)
                    //   return new GenericResponse() { Response = EnumResponse.OtpNotValidated };
                    if (chkOtp == null)
                        return new GenericResponse() { Response = EnumResponse.OtpNotValidated };
                    _logger.LogInformation($"{chkOtp} and {getReg}"+" pin "+ _genServ.EncryptString(Request.SecretValue));
                    await con.ExecuteAsync($"update registration set transpin = @tpin where id={getReg}", new { tpin = _genServ.EncryptString(Request.SecretValue) });
                    //var createprofile = await CreateProfile(getReg, Request.ChannelId, con);
                    var createprofile = await CreateProfile(getReg, Request.RequestReference, Request.ChannelId, con, Request.SecretValue);
                    string customerid = (await con.QueryAsync<string>($"select CustomerId from registration where RequestReference='{Request.RequestReference}'")).FirstOrDefault();
                    _logger.LogInformation("username " + customerid);
                    Thread thread = new Thread(async () =>
                    {
                        _logger.LogInformation("sending email for registration ......");
                        var BalanceEnq = await _genServ.GetAccountbyCustomerId(customerid);
                        _logger.LogInformation("account balance enquiry got ......" + BalanceEnq.balances.ElementAtOrDefault(0).bvn);
                        var BalanceEnqiry = BalanceEnq.balances.ElementAtOrDefault(0);
                        var Users = await _genServ.GetUserbyCustomerId(customerid, con);
                        CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)Users.Id);
                        SendMailObject sendMailObject = new SendMailObject();
                        // sendMailObject.Email = Users.Email;
                        sendMailObject.Email = customerDataNotFromBvn!=null?customerDataNotFromBvn.Email:Users.Email;
                        sendMailObject.Subject = "TrustBanc Mobile Registration";
                        CultureInfo nigerianCulture = new CultureInfo("en-NG");
                        //  < ol > Available Balance { (BalanceEnqiry.availableBalance.ToString("F2", nigerianCulture))}</ ol >
                        sendMailObject.Html = $@"
                            <p>Dear {Users.Firstname.ToUpper()} {Users.LastName.ToUpper()},
                                </p>
                            <p>This is to inform you that your Registration is Successful for the Mobile Banking Platform</p>
                            <p>Below are your Bank Details :</p>
                            <li>
                              <ol>Account Number {BalanceEnqiry.accountNumber}</ol>
                            </li>
                             <p>For any feedback or enquiries, please call our Contact Center on 07004446147 or send an email to support@trustbancgroup.com</p>
                              <p></p>
                             <p>Thank your for Banking with us.</p>
                                            ";
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



        private async Task<bool> CreateProfile(long Regid, string RequestReference, int ChannelId, IDbConnection con, string secretvalue)
        {
            try
            {
                // var chkUser = await con.QueryAsync<long>($"select id from users where customerid = (select customerid from registration where RequestReference = '{RequestReference}')");
                var chkUser = await con.QueryAsync<long>($"select id from users where username = (select username from registration where RequestReference = '{RequestReference}')");
                Console.WriteLine($"user table checked {chkUser.FirstOrDefault()}");
                var reg = await con.QueryAsync<Registration>($"select * from registration where RequestReference='{RequestReference}'");
                _logger.LogInformation("chkUser ....." + chkUser.FirstOrDefault());
                if (chkUser.Count() == 0)
                {
                    _logger.LogInformation($"inserting into user");
                    string sql = $@"insert into users (customerid, phonenumber,email, firstname,createdon,channel_created,status,bvn,username,lastname,migrateduser)
                    select customerid,phonenumber,email,firstname,sysdate(),{ChannelId},1,bvn,username,lastname,migrateduser from registration where RequestReference=@RequestReference";
                   // Console.WriteLine($"inserted into user successfully");
                    await con.ExecuteAsync(sql,new { RequestReference = RequestReference});
                }
                Console.WriteLine($"RequestReference {RequestReference}");
                // Console.WriteLine($" reg details for users {reg} CustomerId");
                // var getCustomer = await _genServ.GetUserbyCustomerId(reg.FirstOrDefault().CustomerId, con);
                var getCustomer = await _genServ.GetUserbyCustomerId(reg.FirstOrDefault().CustomerId, con);
                _genServ.LogRequestResponse($"customer checked {getCustomer}", "", "");
                await con.ExecuteAsync("update customerdatanotfrombvn set userid=@userid where regid=(select id from registration where requestReference=@Requestref)", new { Requestref = RequestReference, userid = getCustomer.Id });
                await _genServ.SetUserCredential(CredentialType.Password, getCustomer.Id, reg.FirstOrDefault().Password, con, false);
                _logger.LogInformation("setting TransactionPin ....." + secretvalue);
                // await _genServ.SetUserCredential(CredentialType.TransactionPin, getCustomer.Id, reg.FirstOrDefault().TransPin, con, false);
                await _genServ.SetUserCredential(CredentialType.TransactionPin, getCustomer.Id, secretvalue, con, true); // this shd encript the pin
                _logger.LogInformation("TransactionPin pin set....." + secretvalue);
                _logger.LogInformation("getCustomer.Id "+ getCustomer.Id + "reg ..."+JsonConvert.SerializeObject(reg.FirstOrDefault()));
                await _genServ.SetMobileDevice(getCustomer.Id, reg.FirstOrDefault().DeviceID, reg.FirstOrDefault().DeviceName, 1, con);
                Console.WriteLine("mobile device set.....");
              //  await con.ExecuteAsync($"update reg_session set status = 0 where status = 1 and regid ={Regid}");
                await con.ExecuteAsync($"update registration set ProfiledOpened = 1,password='',transpin='' where RequestReference = '{RequestReference}'");
                Console.WriteLine("successful..");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return false;
            }
        }
        /*
        private async Task<bool> CreateProfile(long Regid,string RequestReference, int ChannelId, IDbConnection con)
        {
            try
            {
               // var chkUser = await con.QueryAsync<long>($"select id from users where customerid = (select customerid from registration where RequestReference = '{RequestReference}')");
                var chkUser = await con.QueryAsync<long>($"select id from users where username = (select username from registration where RequestReference = '{RequestReference}')");
                Console.WriteLine($"user table checked {chkUser.FirstOrDefault()}");
                _logger.LogInformation("chkUser ....." + chkUser.FirstOrDefault());
                if (chkUser.Count() == 0)
                {
                    Console.WriteLine($"inserted into user");
                    string sql = $@"insert into users (customerid, phonenumber,email, firstname,createdon,channel_created,status,bvn,username,lastname)
                    select customerid,phonenumber,email,firstname,sysdate(),{ChannelId},1,bvn,username,lastname from registration where RequestReference='{RequestReference}'";
                    Console.WriteLine($"inserted into user successfully");
                    await con.ExecuteAsync(sql);
                }
                Console.WriteLine($"RequestReference {RequestReference}");
                var reg = await con.QueryAsync<Registration>($"select * from registration where RequestReference='{RequestReference}'");
               // Console.WriteLine($" reg details for users {reg} CustomerId");
               // var getCustomer = await _genServ.GetUserbyCustomerId(reg.FirstOrDefault().CustomerId, con);
                var getCustomer = await _genServ.GetUserbyCustomerId(reg.FirstOrDefault().CustomerId, con);
                _genServ.LogRequestResponse($"customer checked {getCustomer}","","");
                await _genServ.SetUserCredential(CredentialType.Password, getCustomer.Id, reg.FirstOrDefault().Password, con, false);
                _logger.LogInformation("password set .......");
                _logger.LogInformation("setting TransactionPin ....." + reg.FirstOrDefault().TransPin);
                // await _genServ.SetUserCredential(CredentialType.TransactionPin, getCustomer.Id, reg.FirstOrDefault().TransPin, con, false);
                await _genServ.SetUserCredential(CredentialType.TransactionPin, getCustomer.Id,, con,true); // this shd encript the pin
                _logger.LogInformation("TransactionPin pin set....."+ reg.FirstOrDefault().TransPin);
                await _genServ.SetMobileDevice(getCustomer.Id, reg.FirstOrDefault().DeviceID, reg.FirstOrDefault().DeviceName, 1, con);
                Console.WriteLine("mobile device set.....");
                await con.ExecuteAsync($"update reg_session set status = 0 where status = 1 and regid ={Regid}");
                await con.ExecuteAsync($"update registration set ProfiledOpened = 1,password='',transpin='' where RequestReference = '{RequestReference}'");
                Console.WriteLine("successful..");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.message + " " + ex.StackTrace);
                return false;
            }
        }
        */
        // the original one that was there before 
        private async Task<bool> CreateProfile(long RegId, int ChannelId, IDbConnection con)
        {
            try
            {
                var chkUser = await con.QueryAsync<long>($"select id from users where customerid = (select customerid from registration where id = {RegId})");
                Console.WriteLine($"user table checked {chkUser.FirstOrDefault()}");
                if (chkUser.Count() == 0)
                {
                    Console.WriteLine($"inserted into user");
                    string sql = $@"insert into users (customerid, phonenumber,email, firstname,createdon,channel_created,status,bvn,username)
                    select customerid,phonenumber,email,firstname,sysdate(),{ChannelId},1,bvn,username from registration where id = {RegId}";
                    Console.WriteLine($"inserted into user successfully");
                    await con.ExecuteAsync(sql);
                }
                Console.WriteLine($"Regid {RegId}");
                var reg = await con.QueryAsync<Registration>($"select * from registration where id ={RegId}");
                Console.WriteLine($" reg detals for users {reg} {reg.FirstOrDefault().ID} CustomerId {reg.FirstOrDefault().CustomerId} {ChannelId}");
                var getCustomer = await _genServ.GetUserbyCustomerId(reg.FirstOrDefault().CustomerId, con);
                if (getCustomer == null)
                {
                    Console.WriteLine("customer is null ....");
                }
                Console.WriteLine($"customer checked {getCustomer}");
                await _genServ.SetUserCredential(CredentialType.Password, getCustomer.Id, reg.FirstOrDefault().Password, con, false);
                Console.WriteLine("password set ");
                await _genServ.SetUserCredential(CredentialType.TransactionPin, getCustomer.Id, reg.FirstOrDefault().TransPin, con, false);
                Console.WriteLine("TransactionPin set.....");
                await _genServ.SetMobileDevice(getCustomer.Id, reg.FirstOrDefault().DeviceID, reg.FirstOrDefault().DeviceName, 1, con);
                Console.WriteLine("mobile device set.....");
                await con.ExecuteAsync($"update reg_session set status = 0 where status = 1 and regid ={RegId}");
                await con.ExecuteAsync($"update registration set ProfiledOpened = 1,password='',transpin='' where id = {RegId}");
                Console.WriteLine("successful..");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return false;
            }
        }

        private async Task InsertRegistrationSession(long Id, string Session, int ChannelId, IDbConnection con)
        {
            try
            {
                Console.WriteLine($" InsertRegistrationSession {Session} ChannelId {ChannelId} ");
                var regId = await con.QueryAsync<string>($"select regid from reg_session where regid ={Id}");
                var regIdlong = regId.FirstOrDefault();
                if (regIdlong != null)
                {
                    await con.ExecuteAsync($"update reg_session set status = 1, Session='{Session}' where regid = {Id}");
                }
                else
                {
                    string sql = $@"insert into reg_session (regid, channelId, session,status, createdon) 
                    values ({Id},{ChannelId},'{Session}',1,sysdate())";
                    await con.ExecuteAsync(sql);
                }
                // await con.ExecuteAsync($"update reg_session set status = 0 where regid = {Id}");
                // string sql = $@"insert into reg_session (regid, channelId, session,status, createdon) 
                //   values ({Id},{ChannelId},'{Session}',1,sysdate())";
                //await con.ExecuteAsync(sql);
                Console.WriteLine("inserted successfully ....");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
            }
        }

        private async Task<long> BackUpValidateRegSession(long ChannelId, string Session, IDbConnection con)
        {
            try
            {
                //var resp = await con.QueryAsync<SessionObj>($"select regid id, createdon SessionTime from reg_session where ChannelId = {ChannelId} and Status = 1 and session = '{Session}'");
                Console.WriteLine($"ChannelId {ChannelId} Session {Session} {Session == "0252e78659598e6780f080ada87cca2667ea44e3d382471f813b52f17caa688af49c43622c17f53f91951c8465858b0adab9d5c340bf40582e1a81ba3531efb0"}");
                //var resp = await con.QueryAsync<SessionObj>($"select regid, id, createdon, SessionTime from reg_session where ChannelId = {ChannelId} and Status = 1 and session = '{Session}'");
                var resp = await con.QueryAsync<SessionObj>($"select regid, id, createdon, session from reg_session where ChannelId = {ChannelId} and Status = 1 and session = '{Session}'");
                Console.WriteLine($"resp {resp} {resp.FirstOrDefault().Id} and {resp.FirstOrDefault().SessionTime}");
                if (!resp.Any() || DateTime.Now.Subtract(resp.FirstOrDefault().SessionTime).TotalMinutes > _settings.RegMaxTime)
                    return 0;
                return resp.FirstOrDefault().Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return 0;
            }
        }
        private async Task<long> ValidateRegSession(long ChannelId, string Session, IDbConnection con)
        {
            try
            {
                Console.WriteLine($"ChannelId {ChannelId} Session {Session}");
                // var resp = await con.QueryAsync<SessionObj>($"select regid, id, createdon, SessionTime from reg_session where ChannelId = {ChannelId} and Status = 1 and session = '{Session}'");
                var resp = await con.QueryAsync<SessionObj>($"select regid, id, createdon, session from reg_session where ChannelId = {ChannelId} and Status = 1 and session = '{Session}'");
                // Console.WriteLine($"resp RequestReference {resp}{resp.Any()}{DateTime.Now.Subtract(resp.FirstOrDefault().createdon).TotalMinutes}");
                //  Console.WriteLine($"checking {!resp.Any() || DateTime.Now.Subtract(resp.FirstOrDefault().createdon).TotalMinutes > _settings.RegMaxTime}");
                // if (!resp.Any() || DateTime.Now.Subtract(resp.FirstOrDefault().SessionTime).TotalMinutes > _settings.RegMaxTime)
                //   return 0;
                if (!resp.Any() || (DateTime.Now.Subtract(resp.FirstOrDefault().createdon).TotalMinutes > _settings.RegMaxTime))
                {
                    // update the date to enable the user to start again 
                    await con.ExecuteAsync($"update reg_session set createdon=sysdate() where id = {resp.FirstOrDefault().Id}");
                    //await con.ExecuteAsync($"update reg_session set createdon=sysdate(),session='{Session}' where id = {resp.FirstOrDefault().Id}");
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
        public async Task<ValidateOtpResponse> CheckOtp(string ClientKey, ValidateOtp Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var getReg = await ValidateRegSession(Request.ChannelId, Request.Session, con);
                    if (getReg == 0)
                        return new ValidateOtpResponse() { Response = EnumResponse.InvalidRegSession };

                    var resp = await _genServ.ValidateSessionOtp(OtpType.Registration, Request.Session, con);
                    if (resp == null || resp.OTP != Request.Otp)
                        return new ValidateOtpResponse() { Response = EnumResponse.InvalidOtp };

                    await con.ExecuteAsync($"update otp_session set status = 0 where id = {resp.ID}");

                    var reg = await con.QueryAsync<Registration>($"select * from registration where id = {resp.ObjId}");

                    var checkcbaforbvn = await CheckCbaByBvn(reg.FirstOrDefault().BVN);
                    if (checkcbaforbvn == null || !checkcbaforbvn.success)
                        return new ValidateOtpResponse() { Success = true, Response = EnumResponse.Successful };

                    var checkprofile = await con.QueryAsync<Users>($"select * from users where bvn = @bv", new { bv = reg.FirstOrDefault().BVN });
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

        public async Task<RegistrationResponse> StartRegistrationNin(string ClientKey, RegistrationRequestNin Request)
        {
            try
            {
                string sess = _genServ.GetSession();
                string otp = _genServ.GenerateOtp();
                var uniRef = $"Reg{new Random().Next(11111, 99999)}{DateTime.Now.ToString("ddMMyyyyHHmm")}";
                if (Request.ChannelId == 0)
                    Request.ChannelId = 1;
                Console.WriteLine($"session {sess},otp {otp} uniref {uniRef}");
                using (IDbConnection con = _context.CreateConnection())
                {
                    var checkreq = await con.QueryAsync<RegistrationNin>($"SELECT * FROM registration where nin = @nins", new { nins = Request.Nin });
                    // Console.WriteLine("checkreq ", checkreq.Any(),$"{checkreq.FirstOrDefault().BVN}");
                    if (checkreq.Any())
                    {
                        /*
                         if (!checkreq.FirstOrDefault().ValidNin)
                         {
                             Console.WriteLine("returning from here ");
                             return new RegistrationResponse() { Response = EnumResponse.InvalidNin};
                         }
                        */
                        await _genServ.InsertOtp(OtpType.Registration, checkreq.FirstOrDefault().ID, sess, otp, con);
                        await _genServ.SendOtp(OtpType.Registration, otp, checkreq.FirstOrDefault().PhoneNumber, checkreq.FirstOrDefault().Email);
                        Console.WriteLine("got here after sending otp ....");
                        await InsertRegistrationSession(checkreq.FirstOrDefault().ID, sess, Request.ChannelId, con);
                        return new RegistrationResponse() { Email = _genServ.MaskEmail(checkreq.FirstOrDefault().Email), Success = true, PhoneNumber = _genServ.MaskPhone(checkreq.FirstOrDefault().PhoneNumber), Response = EnumResponse.Successful, SessionID = sess, RequestReference = checkreq.FirstOrDefault().RequestReference };
                    }
                    Console.WriteLine($"requestreference {uniRef}");
                    _logger.LogWarning($"requestreference {uniRef}");
                    string sql = $@"insert into registration (channelid, bvn, requestreference, createdon)
                              values ({Request.ChannelId},@nin,'{uniRef}',sysdate())";
                    await con.ExecuteAsync(sql, new { nin = Request.Nin });
                    Console.WriteLine("query successsful");
                    var regId = await con.QueryAsync<long>($"select id from registration where requestreference = @reqRef", new { reqRef = uniRef });
                    long regIds = regId.FirstOrDefault();
                    Console.WriteLine("regIds ", regIds);
                    _logger.LogWarning($"regIds {regIds}");
                    //var resp = await CheckCbaByBvn(Request.Bvn);
                    var token = await _genServ.GetNinToken();
                    if (string.IsNullOrEmpty(token))
                        return new RegistrationResponse() { Response = EnumResponse.InvalidToken };
                    Console.WriteLine($"token {token}");
                    var resp = await CheckCbaByNin(Request.Nin, token);

                    Console.WriteLine($"resp.success {resp.status}");
                    if (resp.status != false)
                    {
                        await con.ExecuteAsync($"update registration set phonenumber = @phn, CustomerId=@cust,FirstName=@fnm,AccountOpened = 1,email=@emi,ValidBvn = 1 where id = {regIds}", new
                        {
                            phn = resp.nin_data.telephoneno,
                            cust = resp.nin_data.userid,
                            fnm = resp.nin_data.firstname,
                            emi = resp.nin_data.email,
                        });
                        await _genServ.InsertOtp(OtpType.Registration, regIds, sess, otp, con);
                        await _genServ.SendOtp(OtpType.Registration, otp, resp.nin_data.telephoneno, resp.nin_data.telephoneno);
                        await InsertRegistrationSession(regIds, sess, Request.ChannelId, con);
                        return new RegistrationResponse() { Email = _genServ.MaskEmail(resp.nin_data.telephoneno), Success = true, PhoneNumber = _genServ.MaskPhone(resp.nin_data.telephoneno), Response = EnumResponse.Successful, SessionID = sess, RequestReference = uniRef };
                    }
                    else
                    {
                        if (resp.response_code.Equals("03"))
                        { // this will send email  incase there is no subscription to the nin api
                            Thread myThread = new Thread(new ThreadStart(sendTheMail));
                            myThread.Start();
                            return new RegistrationResponse() { Response = EnumResponse.InvalidAccountOpeningWithNin };
                        }
                        return new RegistrationResponse() { Response = EnumResponse.InvalidNin };
                    }

                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new RegistrationResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }
        public void sendTheMail()
        {
            _genServ.SendMail(new SendMailObject()
            {
                Email = "customerservice@trustbancgroup.com",
                Firstname = "Customer Service",
                Html = "<p>Kindly renew the Nin API subscription as this is urgently needed on the Mobile Platform</p>",
                Subject = "Notification to renew Nin API Subscription"

            });
        }

        public async Task<GenericResponse> UploadProfilePicture(string ClientKey, ProfilePicture Request, [FromForm] IFormFile file)
        {
            string uploadPath = _settings.PicFileUploadPath;
            _logger.LogInformation("_settings.PicFileUploadPath " + _settings.PicFileUploadPath);
            string fileName = Path.GetRandomFileName() + Path.GetExtension(file.FileName);
            string path = Path.Combine(uploadPath, fileName);
            _logger.LogInformation("filePath " + path);
            if (file.FileName.EndsWith(".jpeg") || file.FileName.EndsWith(".png") || file.FileName.EndsWith(".jpg"))
            {
                using (IDbConnection con = _context.CreateConnection())
                {

                    var getReg = await ValidateRegSession(Request.ChannelId, Request.Session, con);
                    _logger.LogInformation("getReg " + getReg);
                    if (getReg == 0)
                        return new GenericResponse() { Response = EnumResponse.InvalidRegSession };
                    using (var stream = new FileStream(path, FileMode.Create, access: FileAccess.ReadWrite))
                    {
                        _logger.LogInformation("Pic Request " + JsonConvert.SerializeObject(Request));
                        await con.ExecuteAsync($"update registration set imagepath =@path,address=@Address where requestreference =@RequestReference", new { path = path, Address = Request.Address, RequestReference = Request.RequestReference});
                        var username = await con.QueryAsync<string>($"select username from registration where requestreference = '{Request.RequestReference}'");
                        var bvn = (await con.QueryAsync<string>($"select Bvn from registration where requestreference = '{Request.RequestReference}'")).FirstOrDefault();
                        _logger.LogInformation("reg username ...." + username.FirstOrDefault());
                        _logger.LogInformation("reg bvn ...." + bvn);
                        // string Address = username.FirstOrDefault().Address == null ? "" : username.FirstOrDefault().Address;
                        if (!string.IsNullOrEmpty(username.FirstOrDefault()))
                        {
                            await con.ExecuteAsync($"update users set profilepic=@path, address=@Address where username =@username2", new { path = path, Address = string.IsNullOrEmpty(Request.Address)?"":Request.Address, username2 = username });
                        }else if(!string.IsNullOrEmpty(bvn))
                        {
                            await con.ExecuteAsync($"update users set profilepic=@path, address=@Address where bvn=@bvn", new { path = path, Address = string.IsNullOrEmpty(Request.Address) ? "" : Request.Address, bvn = bvn });
                        }
                        await file.CopyToAsync(stream);
                        await _fileService.SaveFileAsyncForProfilePicture(file);
                        // set the device here at point of complete registration for easy device tracking
                        var device = (await con.QueryAsync<string>($"select device from customer_devices where device='{Request.Device}'")).FirstOrDefault(); // incase it does not exists
                        if (string.IsNullOrEmpty(device))
                        {
                            string sql = "insert into customer_devices(username,device,trackdevice) values(@username, @device,'present')";
                            await con.ExecuteAsync(sql, new { username = Request.Username, device = Request.Device });
                        }
                        else
                        {
                            //var DeviceIdPresent = (await con.QueryAsync<string>($"select device from customer_devices where device='{Request.Device}' and trackdevice='present'")).FirstOrDefault();
                            string sql = "insert into customer_devices(username,device,trackdevice) values(@username, @device,'recent')";
                            await con.ExecuteAsync(sql, new { username = Request.Username, device = Request.Device });
                            //await con.ExecuteAsync($"update customer_devices set trackdevice='present' where username='{Request.Username}' and device='{Request.Device}'");
                            //  await con.ExecuteAsync($"update customer_devices set trackdevice='recent' where username!='{Request.Username}' and device='{Request.Device}'");
                        }
                        _logger.LogInformation("inserted into users table successfully ....", null);
                    }
                }
                return new GenericResponse() { Response = EnumResponse.Successful, Success = true };
            }
            else
            {
                return new GenericResponse() { Response = EnumResponse.InvalidFileformat };
            }
            // string fileName = "nepabill.jpg";
            //string path1 = @"wwwroot";
            //string fullPath = Path.GetFullPath(path1);
            //Console.WriteLine("GetFullPath('{0}') returns '{1}'",
            //  path1, fullPath);
            //fullPath = Path.GetFullPath(fileName);
            //Console.WriteLine("GetFullPath('{0}') returns '{1}'",
            //  fileName, fullPath);
            //C:\Users\samson.oluwaseyi\Documents\dotapps\Retailbanking\Retailbanking.Authentication\wwwroot\

        }

        public async Task<GenericResponse> PhoneAndEmailAtFirstAttempt(string clientKey, PhoneAndEmail Request)
        {
            // string pattern = @"^\w+([-+.']\w+)*@\w+([-.]\w+)*\.\w+([-.]\w+)*$";
            try
            {
                string pattern = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";
                bool allDigits = Request.PhoneNumber.All(char.IsDigit);
                if (string.IsNullOrEmpty(Request.PhoneNumber) || !allDigits)
                {
                    return new GenericResponse() { Response = EnumResponse.PhoneNumberRequired };
                }
                Console.WriteLine(Regex.IsMatch(Request.Email, pattern));
                if (string.IsNullOrEmpty(Request.Email) || !Regex.IsMatch(Request.Email, pattern))
                {
                    return new GenericResponse() { Response = EnumResponse.EmailRequired };
                }
                using (IDbConnection con = _context.CreateConnection())
                {

                    string query = "select email from phone_mail_for_unfinishedreg where email = @Email and phonenumber=@PhoneNumber";
                    var check = (await con.QueryAsync<string>(query, new { Email = Request.Email, Request.PhoneNumber })).FirstOrDefault();
                    _logger.LogInformation("checking mail ..." + check);
                    if (!string.IsNullOrEmpty(check))
                    {
                        return new GenericResponse() { Response = EnumResponse.DetailsAlreadyExists };
                    }
                    string sql = "insert into phone_mail_for_unfinishedreg (PhoneNumber, Email) values (@PhoneNumber, @Email)";
                    await con.ExecuteAsync(sql, new { PhoneNumber = Request.PhoneNumber, Email = Request.Email });

                }
                return new GenericResponse() { Response = EnumResponse.Successful, Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new RegistrationResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> ContactSupportForRegistration(string clientKey, ContactSupport Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var getReg = await ValidateRegSession(Request.ChannelId, Request.Session, con);
                    if (getReg == 0)
                        return new ValidateOtpResponse() { Response = EnumResponse.InvalidRegSession };
                    var username = (await con.QueryAsync<string>($"select username from registration where requestreference = '{Request.RequestReference}'")).FirstOrDefault();
                    Request.Comment = _genServ.RemoveSpecialCharacters(Request.Comment);
                    var userid = await _genServ.GetUserbyUsername(username, con);
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
                        sendMailObject.Html = $@"<p>The Customer {userid.Firstname.ToUpper()} {userid.LastName.ToUpper()} with phonenumber {Request.PhoneNumber} has the following comment on Mobile Registration on Otp:
                                                 </p>
                                                 <p> 
                                                  '{Request.Comment}'
                                                 </p>
                                                  <p>Kindly respond as soon as possible</p>
                                                 ";
                       // string email = !string.IsNullOrEmpty(_settings.CustomerServiceEmail) ? _settings.CustomerServiceEmail : "opeyemi.adubiaro@trustbancgroup.com";
                        _logger.LogInformation("email " + _settings.CustomerServiceEmail);
                        sendMailObject.Email = _settings.CustomerServiceEmail; // send mail to admin
                        sendMailObject.Subject = "TrustBanc-Mobile Registration";
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

        public async Task<GenericResponse> CustomerReasonForNotReceivngOtp(string clientKey, CustomerReasonForNotReceivngOtp Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var getReg = await ValidateRegSession(Request.ChannelId, Request.Session, con);
                    if (getReg == 0)
                        return new ValidateOtpResponse() { Response = EnumResponse.InvalidRegSession };
                    string reqRef = (await con.QueryAsync<string>("select bvn from customerreasonfornotreceivngotpatreg where bvn=@bvn", new { bvn = Request.Bvn })).FirstOrDefault();
                    _logger.LogInformation("reqRef " + reqRef);
                    if (!string.IsNullOrEmpty(reqRef))
                    {
                        return new GenericResponse() { Success = true, Message = "Done already", Response = EnumResponse.Successful };
                    }
                    string sql = "insert into customerreasonfornotreceivngotpatreg(bvn,reason,requestreference) " +
                            "values (@bvn,@reason,@requestreference)";
                    //  _logger.LogInformation("username " + username + "userid " + userid);
                    await con.ExecuteAsync(sql, new
                    {
                        bvn = Request.Bvn,
                        reason = Request.Reason,
                        requestreference = Request.RequestReference,
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

        public async Task<RegistrationResponse> IsUserHasAccountNumber(string clientKey, CheckRegistrationRequest request)
        {
            FinedgeSearchBvn finedgeSearchBvn = await CheckCbaByBvn(request.Bvn);
            if (finedgeSearchBvn != null)
            {
                if (finedgeSearchBvn.success)
                {
                    return new RegistrationResponse() { Success = true, Response = EnumResponse.UserAlreadyHasAnAccount };
                }
                else
                {
                    return new RegistrationResponse() { Success = false, Response = EnumResponse.Successful };
                }
            }
            else
            {
                return new RegistrationResponse() { Success = false, Response = EnumResponse.Successful };
            }
            // throw new NotImplementedException();
        }

        public async Task<ValidateOtpResponse> ValidateOtpForMigratedCustomer(string clientKey, ValidateOtpForMigratedCustomer Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    //get current user from memory                   
                  // MigratedCustomer migratedCustomer2  = _userCacheService.GetUserData(Request.PhoneNumber,_cache);
                  // MigratedCustomer migratedCustomer1 = await _redisStorageService.GetCacheDataAsync<MigratedCustomer>(Request.PhoneNumber);
                    //MigratedCustomer migratedCustomer1 = await _redisStorageService.GetCacheDataAsync<MigratedCustomer>(Request.PhoneNumber);
                    string migratedCustomerString = await _redisStorageService.GetCustomerAsync(Request.PhoneNumber);
                   // Console.WriteLine("migratedCustomer1 " + migratedCustomerString);
                    _logger.LogInformation("migratedCustomer1 " + migratedCustomerString);
                    MigratedCustomer migratedCustomer1 = JsonConvert.DeserializeObject<MigratedCustomer>(migratedCustomerString);
                    _logger.LogInformation("migratedCustomer1 "+ JsonConvert.SerializeObject(migratedCustomer1));
                    if (migratedCustomer1==null)
                    {
                        return new ValidateOtpResponse() { Response = EnumResponse.InvalidRegSession};
                    }
                    DateTime dateTime = DateTime.Now;
                    // Calculate the difference
                    TimeSpan difference = dateTime - migratedCustomer1.dateTime;
                    // Check if the difference is not greater than 3 minutes
                    if (Math.Abs(difference.TotalMinutes)>=5)
                    {
                        return new ValidateOtpResponse() { Response = EnumResponse.OtpTimeOut,Success=false};
                    }
                    if (migratedCustomer1.Otp != Request.Otp)
                        return new ValidateOtpResponse() { Response = EnumResponse.InvalidOtp };
                    if(!migratedCustomer1.Session.Equals(Request.Session,StringComparison.CurrentCultureIgnoreCase))
                    {
                     return new ValidateOtpResponse() { Response = EnumResponse.InvalidSession};
                    }
                    // proceed and profile customer if he is the one .
                   // new Thread(async() =>
                   // {
                 var uniRef=await MigratedExistinguser(Request.Session,Request.Otp,Request.ChannelId, migratedCustomer1,con,"");
                  // }).Start();
                  return new ValidateOtpResponse() { Response = EnumResponse.Successful,Success=true,data=new {RequestReference=uniRef} };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new ValidateOtpResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }
    }
}

















































































































