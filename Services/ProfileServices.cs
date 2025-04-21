using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Retailbanking.BL.Services
{
    public class ProfileServices : IProfile
    {
        private readonly ILogger<ProfileServices> _logger;
        private readonly AppSettings _settings;
        private readonly IGeneric _genServ;
        private readonly IMemoryCache _cache;
        private readonly DapperContext _context;

        public ProfileServices(ILogger<ProfileServices> logger, IOptions<AppSettings> options, IGeneric genServ, IMemoryCache memoryCache, DapperContext context)
        {
            _logger = logger;
            _settings = options.Value;
            _genServ = genServ;
            _cache = memoryCache;
            _context = context;
        }

        public async Task<GenericResponse> GetProfileStatus(string ClientKey, GenericRequest Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Request.Username, Request.Session, Request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };

                    var usr = await _genServ.GetUserbyUsername(Request.Username, con);
                    var idcard = await ProfileInfo(con, usr.Id, ProfileType.IdCard);
                    var empInfo = await ProfileInfo(con, usr.Id, ProfileType.EmploymentInformation);
                    var nextKin = await ProfileInfo(con, usr.Id, ProfileType.NextOfKin);
                    var bnkDet = await ProfileInfo(con, usr.Id, ProfileType.BankDetail);
                    var otherCred = await ProfileInfo(con, usr.Id, ProfileType.OtherCredentials);

                    int totalComplete = Convert.ToInt32(idcard) + Convert.ToInt32(empInfo) + Convert.ToInt32(nextKin) + Convert.ToInt32(bnkDet) + Convert.ToInt32(otherCred);
                    double totaldon = (totalComplete * 100) / 5;
                    return new GenericResponse() { Response = EnumResponse.Successful, Success = true, Message = $"{totaldon}% profile completed" };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<List<GenericIdStatus>> ViewProfileStatus(string ClientKey, GenericRequest Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Request.Username, Request.Session, Request.ChannelId, con);
                    if (!validateSession)
                        return new List<GenericIdStatus>();

                    var usr = await _genServ.GetUserbyUsername(Request.Username, con);
                    var idcard = await ProfileInfo(con, usr.Id, ProfileType.IdCard);
                    var empInfo = await ProfileInfo(con, usr.Id, ProfileType.EmploymentInformation);
                    var nextKin = await ProfileInfo(con, usr.Id, ProfileType.NextOfKin);
                    var bnkDet = await ProfileInfo(con, usr.Id, ProfileType.BankDetail);
                    var otherCred = await ProfileInfo(con, usr.Id, ProfileType.OtherCredentials);

                    var result = new List<GenericIdStatus>
                    {
                        new GenericIdStatus() {  Id = 1, Success = idcard, Value = "ID Card Information" },
                        new GenericIdStatus() { Id = 2,Success = empInfo, Value = "Employment Information" },
                        new GenericIdStatus() { Id = 3,Success = nextKin, Value = "Next of Kin Information" },
                        new GenericIdStatus() { Id = 4,Success = bnkDet, Value = "Bank Account Information" },
                        new GenericIdStatus() {Id = 5, Success = otherCred, Value = "Other Credentials" }
                    };
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new List<GenericIdStatus>();
            }
        }

        public async Task<List<DocumentType>> GetDocumentTypes()
        {
            try
            {
                var myData = new List<DocumentType>();
                if (!_cache.TryGetValue(CacheKeys.DocumentType, out myData))
                {
                    using (IDbConnection con = _context.CreateConnection())
                    {
                        // Key not in cache, so get data.
                        var myData1 = await con.QueryAsync<DocumentType>("select * from document_type where status= 1 order by id");
                        myData = myData1.ToList();
                        // Set cache options.
                        var cacheEntryOptions = new MemoryCacheEntryOptions()
                            // Keep in cache for this time, reset time if accessed.
                            .SetSlidingExpiration(TimeSpan.FromDays(1));

                        // Save data in cache.
                        _cache.Set(CacheKeys.DocumentType, myData, cacheEntryOptions);
                    }
                }
                return myData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new List<DocumentType>();
            }
        }

        public async Task<List<GenericValue>> GetOtherCredentials(string ClientKey, GenericRequest Request)
        {
            try
            {
                return new List<GenericValue>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new List<GenericValue>();
            }
        }

        private async Task<bool> ProfileInfo(IDbConnection con, long UserId, ProfileType profileType)
        {
            try
            {
                switch (profileType)
                {
                    case ProfileType.IdCard:
                        var chkupload = await con.QueryAsync<long>($"select id from idcard_upload where userid = {UserId} and status=1");
                        return chkupload.Any();


                    default: return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return false;
            }
        }


        public async Task<GenericResponse> UploadDocument(string ClientKey, UploadDocument Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Request.Username, Request.Session, Request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };

                    var usr = await _genServ.GetUserbyUsername(Request.Username, con);
                    var chkUpload = await ProfileInfo(con, usr.Id, ProfileType.IdCard);
                    if (chkUpload)
                        return new GenericResponse() { Response = EnumResponse.NotSuccessful, Message = "ID Card Already Uploaded" };

                    if (string.IsNullOrEmpty(Request.FileExtension))
                        Request.FileExtension = "." + GetExtensionFromBase64(Request.FileBase64);

                    string filename = $"{DateTime.Now.ToString("ddMMyyHHmmss")}_{new Random().Next(1111, 9999)}{Request.FileExtension}";
                    string path = $"{_settings.FileLocation}\\{usr.Id}";
                    if (!Directory.Exists(path))
                        Directory.CreateDirectory(path);
                    string fileUpload = Path.Combine(path, filename);

                    try
                    {
                        File.WriteAllBytes(fileUpload, Convert.FromBase64String(Request.FileBase64));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Error Reading Base64 - " + ex.Message);
                        Regex regex = new Regex(@"^[\w/\:.-]+;base64,");
                        Request.FileBase64 = regex.Replace(Request.FileBase64, string.Empty);
                        File.WriteAllBytes(fileUpload, Convert.FromBase64String(Request.FileBase64));
                    }

                    string sql = $@"insert into idcard_upload (userid, idtype, idnumber,issuedate,expirydate,status,filelocation,createdon)
                         values ({usr.Id},{Request.IdType},@idno,@issdate,@expdate,1,@fil,sysdate())";
                    await con.ExecuteAsync(sql, new
                    {
                        idno = Request.IdNumber,
                        issdate = _genServ.ConvertDatetime(Request.IssueDate),
                        expdate = _genServ.ConvertDatetime(Request.ExpiryDate),
                        fil = filename
                    });
                    return new GenericResponse() { Response = EnumResponse.Successful, Success = true };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Message = ex.Message, Response = EnumResponse.SystemError };
            }
        }

        public Task<GenericResponse> UploadOtherCredentials(string ClientKey, GenericIdFileUpload Request)
        {
            throw new NotImplementedException();
        }

        public async Task<GenericResponse> AddEmploymentInformation(string ClientKey, AddEmploymentInfo Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Request.Username, Request.Session, Request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };

                    var usr = await _genServ.GetUserbyUsername(Request.Username, con);
                    var chkUpload = await ProfileInfo(con, usr.Id, ProfileType.EmploymentInformation);
                    if (chkUpload)
                        return new GenericResponse() { Response = EnumResponse.NotSuccessful, Message = "Employment Information Already Provided" };


                    string sql = $@"insert into employee_info (userid, occupation,employer, employer_address, phonenumber, employeestatus, annualturnover,sourcefund,createdon,status)
                    values ({usr.Id},@occ,@empl,@empadd,@phn,{Request.EmployeeStatus},{Request.AnnualTurnover},{Request.SourceFund},sysdate(),1) ";
                    await con.ExecuteAsync(sql, new
                    {
                        occ = Request.Occupation,
                        empl = Request.EmployerName,
                        empadd = Request.EmployeeAddress,
                        phn = Request.PhoneNumber
                    });
                    return new GenericResponse() { Response = EnumResponse.Successful, Success = true };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Message = ex.Message, Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> AddNextOfKin(string ClientKey, AddNextKin Request)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(Request.Username, Request.Session, Request.ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };

                    var usr = await _genServ.GetUserbyUsername(Request.Username, con);
                    var chkUpload = await ProfileInfo(con, usr.Id, ProfileType.NextOfKin);
                    if (chkUpload)
                        return new GenericResponse() { Response = EnumResponse.NotSuccessful, Message = "Next of Kin Information Already Provided" };


                    string sql = $@"insert into next_kin_information (userid, nextkinname,gender,datebirth,relationship,address,phonenumber,emailaddress,createdon,status)
values ({usr.Id},@nkin,{Request.Gender},@dob,{Request.Relationship},@add,@phn,@emai,sysdate(),1) ";
                    await con.ExecuteAsync(sql, new
                    {
                        nkin = Request.NameKin,
                        dob = _genServ.ConvertDatetime(Request.DateOfBirth),
                        add = Request.Address,
                        phn = Request.PhoneNumber,
                        emai = Request.Email
                    });
                    return new GenericResponse() { Response = EnumResponse.Successful, Success = true };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Message = ex.Message, Response = EnumResponse.SystemError };
            }
        }

        private string GetExtensionFromBase64(string base64)
        {
            try
            {
                var data = base64.Substring(0, 5);
                switch (data.ToUpper())
                {
                    case "IVBOR":
                        return "png";
                    case "/9J/4":
                        return "jpg";
                    case "AAAAF":
                        return "mp4";
                    case "JVBER":
                        return "pdf";
                    case "AAABA":
                        return "ico";
                    case "UMFYI":
                        return "rar";
                    case "E1XYD":
                        return "rtf";
                    case "U1PKC":
                        return "txt";
                    case "MQOWM":
                    case "77U/M":
                        return "srt";
                    default:
                        return string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return string.Empty;
            }
        }
    }
}
