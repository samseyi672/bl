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
using Retailbanking.Common.DbObj;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Retailbanking.BL.Services
{
    public class OldRegistrationServices
    {
        private readonly ILogger<OldRegistrationServices> _logger;
        private readonly AppSettings _settings;
        private readonly IGeneric _genServ;
        private readonly IMemoryCache _cache;
        private readonly DapperContext _context;

        public OldRegistrationServices(ILogger<OldRegistrationServices> logger, IOptions<AppSettings> options, IGeneric genServ, IMemoryCache memoryCache, DapperContext context)
        {
            _logger = logger;
            _settings = options.Value;
            _genServ = genServ;
            _cache = memoryCache;
            _context = context;
        }

        public async Task<RegistrationResponse> StartRegistration(RegistrationRequest Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    //  var req = new EnquiryObj() { ClientKey = _settings.FinedgeKey, Value = Request.Request };
                    var resp = await _genServ.CallServiceAsync<SearchResponse>(Method.POST, $"{_settings.FinedgeUrl}api/enquiry/SearchCustomerAccountPhoneNumberBvn", null, true);

                    string sess = _genServ.GetSession();
                    string otp = _genServ.GenerateOtp();
                    var uniRef = $"Reg{new Random().Next(11111, 99999)}{DateTime.Now.ToString("ddMMyyyy")}";

                    if (resp.Success && resp.Result.Any())
                    {
                        var acctresp = resp.Result.FirstOrDefault();
                        var chkProfile = await con.QueryAsync<long>($"select id from users where CustomerId = {acctresp.CustomerID}");
                        if (chkProfile.Any())
                            return new RegistrationResponse() { Response = EnumResponse.ProfileAlreadyExist };

                        var getReg = await con.QueryAsync<long>($"select id from registration where CustomerId = @cst", new { cst = acctresp.CustomerID });
                        long regIds = 0;

                        if (getReg.Any())
                            regIds = getReg.FirstOrDefault();
                        else
                        {
                            string sql = $@"insert into registration (channelid, existingaccount,requesttype, requestvalue, requestreference, createdon)
                              values ({Request.ChannelId},1,1,@reqVal,'{uniRef}',sysdate())";
                            await con.ExecuteAsync(sql, new { reqVal = Request.Bvn });
                            var regId = await con.QueryAsync<long>($"select id from registration where requestreference = @reqRef", new { reqRef = uniRef });
                            regIds = regId.FirstOrDefault();
                        }

                        await _genServ.InsertOtp(OtpType.Registration, regIds, sess, otp, con);
                        await _genServ.SendOtp(OtpType.Registration, otp, acctresp.Mobile, acctresp.Email);
                        await InsertRegistrationSession(regIds, sess, Request.ChannelId, con);
                        await con.ExecuteAsync($"update registration set bvn = @bvn,phonenumber = @phn, CustomerId=@cust,FirstName=@fnm,AccountOpened = 1 where id = {regIds}", new
                        {
                            bvn = acctresp.BVN,
                            phn = acctresp.Mobile,
                            cust = acctresp.CustomerID,
                            fnm = acctresp.Firstname
                        });
                        return new RegistrationResponse() { Email = _genServ.MaskEmail(acctresp.Email), Success = true, PhoneNumber = _genServ.MaskPhone(acctresp.Mobile), Response = EnumResponse.Successful, SessionID = sess };
                    }

                    long reqbvnId = 0;
                    var regbvn = await con.QueryAsync<long>($"select id from registration where bvn = @bvs", new { bvs = Request.Bvn });
                    if (regbvn.Any())
                        reqbvnId = regbvn.FirstOrDefault();
                    else
                    {
                        string sql1 = $@"insert into registration (channelid, existingaccount,requesttype, requestvalue, requestreference, createdon,bvn)
                    values ({Request.ChannelId},0,1,@reqVal,'{uniRef}',sysdate(),@bv)";
                        await con.ExecuteAsync(sql1, new { reqVal = Request.Bvn, bv = Request.Bvn });
                        var regId1 = await con.QueryAsync<long>($"select id from registration where requestreference = @reqRef", new { reqRef = uniRef });
                        reqbvnId = regId1.FirstOrDefault();
                    }
                    var getBvn = await ValidateBvn(Request.Bvn, con);
                    if (!getBvn.Success)
                        return new RegistrationResponse() { Response = EnumResponse.InvalidBvn };

                    await _genServ.InsertOtp(OtpType.Registration, reqbvnId, sess, otp, con);
                    await _genServ.SendOtp(OtpType.Registration, otp, getBvn.BvnDetails.PhoneNumber, getBvn.BvnDetails.Email);
                    await InsertRegistrationSession(reqbvnId, sess, Request.ChannelId, con);
                    await con.ExecuteAsync($"update registration set phonenumber = @ph, email = @em,firstname = @fn where id= {reqbvnId}", new
                    {
                        ph = getBvn.BvnDetails.PhoneNumber,
                        em = getBvn.BvnDetails.Email,
                        fn = getBvn.BvnDetails.Firstname
                    });

                    return new RegistrationResponse()
                    {
                        Email = _genServ.MaskEmail(getBvn.BvnDetails.Email),
                        Success = true,
                        PhoneNumber = _genServ.MaskPhone(getBvn.BvnDetails.PhoneNumber),
                        Response = EnumResponse.Successful,
                        SessionID = sess
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new RegistrationResponse() { Response = EnumResponse.SystemError };
            }
        }

        private async Task<ValidateBvn> ValidateBvn(string Bvn, IDbConnection con)
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
                var req = new EnquiryObj() { ClientKey = _settings.FinedgeKey, Value = Bvn };
                var resp = await _genServ.CallServiceAsync<BvnResponse>(Method.POST, $"{_settings.FinedgeUrl}api/bvn/ValidateBvnFull", req, true);

                if (resp.ResponseCode != "00")
                    return new ValidateBvn() { Response = EnumResponse.NotSuccessful };

                await InsertBvn(resp, con);
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

        private async Task InsertBvn(BvnResponse BvnDetails, IDbConnection con)
        {
            try
            {
                string sql = $@"INSERT INTO bvn_validation (BVN,BvnPhoneNumber,PhoneNumber2,BvnEmail,Gender,LgaResidence,
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

        public async Task<BvnSubDetails> ValidateDob(SetRegristationCredential Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var getReg = await ValidateRegSession(Request.ChannelId, Request.Session, con);
                    if (getReg == 0)
                        return new BvnSubDetails() { Response = EnumResponse.InvalidRegSession };

                    var bvndetails = await con.QueryAsync<BvnValidation>($"select * from bvn_validation where bvn = (select bvn from registration where id = {getReg})");
                    if (bvndetails.FirstOrDefault().DOB.GetValueOrDefault().Year.ToString() != Request.SecretValue)
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
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new BvnSubDetails() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<RegistrationResponse> ResendOtp(GenericRegRequest Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var getReg = await ValidateRegSession(Request.ChannelId, Request.Session, con);
                    if (getReg == 0)
                        return new RegistrationResponse() { Response = EnumResponse.InvalidRegSession };

                    string otp = _genServ.GenerateOtp();
                    await con.ExecuteAsync($"update otp_session set status = 0 where otp_type = {(int)OtpType.Registration} and objId = {getReg}");
                    await _genServ.InsertOtp(OtpType.Registration, getReg, Request.Session, otp, con);
                    return new RegistrationResponse() { Response = EnumResponse.Successful, Success = true };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new RegistrationResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<AccountOpeningResponse> OpenAccount(GenericRegRequest Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var getReg = await ValidateRegSession(Request.ChannelId, Request.Session, con);
                    if (getReg == 0)
                        return new AccountOpeningResponse() { Response = EnumResponse.InvalidRegSession };

                    var bvndetails = await con.QueryAsync<BvnValidation>($"select * from bvn_validation where bvn = (select bvn from registration where id = {getReg})");
                    var address = new List<Address>();
                    address.Add(new Address()
                    {
                        addressLine1 = bvndetails.FirstOrDefault().RESIDENTIALADDRESS,
                        street = bvndetails.FirstOrDefault().LgaResidence,
                        city = bvndetails.FirstOrDefault().StateResidence
                    });

                    var dtables = new List<Datatable>();
                    var data = new Data() { bvn = bvndetails.FirstOrDefault().BVN };
                    dtables.Add(new Datatable() { data = data, registeredTableName = "Additional Information" });

                    var request = new OpenAccount()
                    {
                        clientKey = _settings.FinedgeKey,
                        firstname = bvndetails.FirstOrDefault().FIRSTNAME,
                        middlename = bvndetails.FirstOrDefault().MIDDLENAME,
                        lastname = bvndetails.FirstOrDefault().LASTNAME,
                        dateOfBirth = bvndetails.FirstOrDefault().DOB.GetValueOrDefault().ToString("dd/MMM/yyyy"),
                        male = bvndetails.FirstOrDefault().GENDER.StartsWith('M'),
                        mobileNo = bvndetails.FirstOrDefault().PHONENUMBER,
                        savingsProductId = _settings.SavingAccountProduct,
                        address = address.ToArray(),
                        emailAddress = bvndetails.FirstOrDefault().EMAIL,
                        idCardNo = bvndetails.FirstOrDefault().BVN,
                        datatables = dtables.ToArray()
                    };

                    var opnAcct = await _genServ.CallServiceAsync<OpenAccountResponse>(Method.POST, $"{_settings.FinedgeUrl}api/customer/CreateRetailAccount", request, true);
                    if (opnAcct.success)
                    {
                        await con.ExecuteAsync($"update registration set customerid = '{opnAcct.clientId}', accountopened = 1 where id = {getReg}");
                        return new AccountOpeningResponse() { AccountNumber = opnAcct.accountNumber, Response = EnumResponse.Successful, Success = true };
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

        public async Task<GenericResponse> ValidateOtp(ValidateOtp Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var getReg = await ValidateRegSession(Request.ChannelId, Request.Session, con);
                    if (getReg == 0)
                        return new GenericResponse() { Response = EnumResponse.InvalidRegSession };

                    string sql = $@"select id, otp value from otp_session where otp_type= {(int)OtpType.Registration} and status = 1 and session = @sess";
                    var resp = await con.QueryAsync<GenericValue>(sql, new { sess = Request.Session });
                    if (!resp.Any() || resp.FirstOrDefault().Value != Request.Otp)
                        return new GenericResponse() { Response = EnumResponse.InvalidOtp };

                    await con.ExecuteAsync($"update otp_session set status = 0 where id = {resp.FirstOrDefault().Id}");
                    return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> CreateUsername(SetRegristationCredential Request)
        {
            try
            {
                return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> CreatePassword(SavePasswordRequest Request)
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

                    if (string.IsNullOrEmpty(Request.DeviceId))
                        return new GenericResponse() { Response = EnumResponse.DeviceIdRequired };
                    var createprofile = await CreateProfile(getReg, Request.ChannelId, con);
                    if (!createprofile)
                        return new GenericResponse() { Response = EnumResponse.ErrorCreatingProfile };

                    var usrId = await con.QueryAsync<long>($"select id from users where CustomerId = (select customerid from registration where id={getReg})");
                    if (usrId.FirstOrDefault() == 0)
                        return new GenericResponse() { Response = EnumResponse.InvalidRegSession };

                    await con.ExecuteAsync($"delete from user_credentials where userid = {usrId.FirstOrDefault()} and CredentialType = 1");
                    string sql = $@"insert into user_credentials (userid,credentialtype,status,createdon,credential)
                    values ((select id from users where CustomerId = (select customerid from registration where id={getReg})),1,1,sysdate(),@cred)";
                    await con.ExecuteAsync(sql, new { cred = _genServ.EncryptString(Request.SecretValue) });

                    string sql2 = $@"insert into mobiledevice (userid, device, status, devicename, createdon)
                        values ((select id from users where CustomerId = (select customerid from registration where id={getReg})),@devId,1,@devName,sysdate())";
                    await con.ExecuteAsync(sql2, new { devId = Request.DeviceId, devName = Request.DeviceName });

                    var bvn = await con.QueryAsync<string>($"select bvn from registration where id ={getReg}");

                    var benfinary = await _genServ.CallServiceAsync<GetDigitalBankingBeneficiaries>(Method.POST, $"{_settings.FinedgeUrl}api/enquiry/GetDigitalBanking", new EnquiryObj() { ClientKey = _settings.FinedgeKey, Value = bvn.FirstOrDefault() }, true);
                    var bnks = await _genServ.GetBanks();
                    _logger.LogInformation("Bank List for Registration " + bnks.Count);

                    if (benfinary.Success && bnks.Any())
                    {
                        foreach (var n in benfinary.Beneficiaries)
                        {
                            var bnk = bnks.FirstOrDefault(x => x.BankCode == n.BeneficiaryBank);
                            try
                            {
                                string sql_ben = $@"INSERT INTO beneficiary (UserId,BeneficiaryType,Name,Value,ServiceName,IsDeleted,Code,ChannelId,CreatedOn) 
                                    VALUES ({usrId.FirstOrDefault()},1,@name,@vals,@servs,0,@cod,{Request.ChannelId},sysdate())";

                                await con.ExecuteAsync(sql_ben, new
                                {
                                    name = n.BeneficiaryName,
                                    vals = n.BeneficiaryAccount,
                                    servs = bnk != null ? bnk.Bankname : "",
                                    cod = n.BeneficiaryBank
                                });
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError("Beneficiary -" + JsonConvert.SerializeObject(n));
                                _logger.LogError("Bank " + JsonConvert.SerializeObject(bnk));
                                _logger.LogError(ex.Message + " " + ex.StackTrace);
                            }
                        }
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

        public async Task<GenericResponse> CreateTransPin(SetRegristationCredential Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var getReg = await ValidateRegSession(Request.ChannelId, Request.Session, con);
                    if (getReg == 0)
                        return new GenericResponse() { Response = EnumResponse.InvalidRegSession };

                    string sql = $@"insert into user_credentials (userid,credentialtype,status,createdon,credential)
                    values ((select id from users where CustomerId = (select customerid from registration where id={getReg})),2,1,sysdate(),@cred)";
                    await con.ExecuteAsync(sql, new { cred = _genServ.EncryptString(Request.SecretValue) });
                    await con.ExecuteAsync($"update reg_session set status = 0 where status = 1 and regid ={getReg}");
                    await con.ExecuteAsync($"update registration set ProfiledOpened = 1 where id = {getReg}");
                    return new GenericResponse() { Response = EnumResponse.Successful, Success = true };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        private async Task<bool> CreateProfile(long RegId, int ChannelId, IDbConnection con)
        {
            try
            {
                var usr = await con.QueryAsync<long>($"select id from users where CustomerId = (select customerid from registration where id = {RegId})");
                if (usr.Any())
                    return true;
                string sql = $@"insert into users (CustomerId,phonenumber,email, firstname,createdon, channel_created, status,bvn)
                select customerid, phonenumber, email,firstname,sysdate(),{ChannelId},1,bvn from registration where id= {RegId}";
                await con.ExecuteAsync(sql);
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
                await con.ExecuteAsync($"update reg_session set status = 0 where regid = {Id}");
                string sql = $@"insert into reg_session (regid, channelId, session,status, createdon) 
                    values ({Id},{ChannelId},'{Session}',1,sysdate())";
                await con.ExecuteAsync(sql);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
            }
        }

        private async Task<long> ValidateRegSession(long ChannelId, string Session, IDbConnection con)
        {
            try
            {
                var resp = await con.QueryAsync<SessionObj>($"select regid id, createdon SessionTime from reg_session where ChannelId = {ChannelId} and Status = 1 and session = '{Session}'");
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

    }
}
