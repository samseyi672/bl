using Dapper;
using iText.Kernel.Geom;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlX.XDevAPI.Common;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using Retailbanking.Common.DbObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Path = System.IO.Path;
using Newtonsoft.Json;
using Org.BouncyCastle.Asn1.Ocsp;
using System.Globalization;
using System.Threading;
using Microsoft.AspNetCore.Http;
using System.Net.NetworkInformation;
using RestSharp;
using Microsoft.Extensions.Hosting;
using MySqlX.XDevAPI;
using System.Threading.Channels;
using iText.Html2pdf.Attach;
using iText.Forms.Form.Renderer;

namespace Retailbanking.BL.Services
{

    public class MobileUserService : IMobileUserService
    {

        private readonly ILogger<AuthenticationServices> _logger;
        private readonly AppSettings _settings;
        private readonly SmtpDetails smtpDetails;
        private readonly IGeneric _genServ;
        private readonly IMemoryCache _cache;
        private readonly DapperContext _context;
        private readonly IAccounts _acctServ;
        private readonly INotification _notification;
        private readonly IFileService _fileService;
        private readonly IStaffUserService _staffUserService;
        public MobileUserService(IStaffUserService staffUserService, IFileService fileService, INotification notification, ILogger<AuthenticationServices> logger, IOptions<AppSettings> options, IOptions<SmtpDetails> options1, IGeneric genServ, IMemoryCache memoryCache, DapperContext context)
        {
            _staffUserService = staffUserService;
            _logger = logger;
            _settings = options.Value;
            smtpDetails = options1.Value;
            _genServ = genServ;
            _cache = memoryCache;
            _context = context;
            _notification = notification;
            _fileService = fileService;
        }

        public Task<GenericResponse> ActivateAUser(int userId)
        {
            throw new NotImplementedException();
        }

        public Task<GenericResponse> DeactivateAUser(int userId)
        {
            throw new NotImplementedException();
        }

        public async Task<GenericResponse> GetPrimeUsers(int page, int size, string host)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                int skip = (page - 1) * size;
                int take = size;
                // string sql = @"SELECT * FROM users ORDER BY id ASC OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";
                // string sql = "SELECT (select nin from registration where username=u.username) as nin,u.LastName,u.Username,u.Address,u.Bvn,u.CustomerId,u.Firstname,u.BvnPhoneNumber,u.BvnEmail,if(u.Status==1) then true else false,u.ProfilePicture FROM users u ORDER BY id ASC LIMIT @Take OFFSET @Skip";
                /*
                string sql = $@"SELECT  (SELECT Email FROM customerdatanotfrombvn WHERE userId = u.id) as CustomerEmail,
                                        (SELECT PhoneNumber FROM customerdatanotfrombvn WHERE userId = u.id) as CustomerPhoneNumber,
                                        (SELECT Address FROM customerdatanotfrombvn WHERE userId = u.id) as CustomerAddress,
                                        (SELECT nin FROM registration WHERE username = u.username) AS nin,
                                        u.LastName,
                                        u.Username,
                                        u.Address,
                                        u.Bvn,
                                        u.CustomerId,
                                        u.Firstname,
                                        u.PhoneNumber as BvnPhoneNumber,
                                        u.Email as BvnEmail,
                                        CASE WHEN u.Status = 1 THEN TRUE ELSE FALSE END AS Active,
                                        u.ProfilePic as ProfilePicture 
                                    FROM 
                                        users u 
                                    ORDER BY 
                                        id ASC 
                                    LIMIT 
                                        @Take OFFSET @Skip;
                                    ";
                              */
                string sql = @"
                                    SELECT  
                                        cdnb.Email AS CustomerEmail,
                                        cdnb.PhoneNumber AS CustomerPhoneNumber,
                                        cdnb.Address AS CustomerAddress,
                                        reg.nin AS nin,
                                        u.LastName,
                                        u.Username,
                                        u.Address,
                                        u.Bvn,
                                        u.CustomerId,
                                        u.Firstname,
                                        u.PhoneNumber AS BvnPhoneNumber,
                                        u.Email AS BvnEmail,
                                        CASE WHEN u.Status = 1 THEN TRUE ELSE FALSE END AS Active,
                                        u.ProfilePic AS ProfilePicture 
                                    FROM 
                                        users u
                                    LEFT JOIN 
                                        customerdatanotfrombvn cdnb ON cdnb.userId = u.id
                                    LEFT JOIN 
                                        registration reg ON reg.username = u.username
                                    ORDER BY 
                                        u.id ASC 
                                    LIMIT 
                                        @Take OFFSET @Skip;
                                ";

                var parameters = new { Take = take, Skip = skip };

                // var result = (await con.QueryAsync<MobileUsers>(sql, new { Skip = skip, Take = take })).ToList();
                var result = (await con.QueryAsync<MobileUsers>(sql, parameters)).ToList();
                result.ForEach(x =>
                {
                    x.ProfilePicture = x.ProfilePicture != null ? host + _settings.Urlfilepath + "/KycView2/" + Path.GetFileName(x.ProfilePicture) : null;
                    x.AccountName = x.AccountName == null ? x.Firstname + " " + (!string.IsNullOrEmpty(x.LastName) ? x.LastName : "") : x.AccountName;
                });
                return new PrimeAdminResponse() { Success = true, Data = new { response = result, page = page, size = size }, Response = EnumResponse.Successful };

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> GetCustomerAllAccountBalance(string Username)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var usr = await _genServ.GetUserbyUsername(Username, con);
                var balanceEnquiryResponse = _genServ.GetCustomerAllAccountBalance(usr.CustomerId);
                Console.WriteLine("finedge url" + _settings.FinedgeUrl);
                return (await balanceEnquiryResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> InitiateActivateACustomer(string UserName, string status, string StaffNameAndRole)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var usr = await _genServ.GetUserbyUsername(UserName, con);
                _logger.LogInformation("staffNameAndRole " + StaffNameAndRole);
                string name = StaffNameAndRole.Contains("_") ? StaffNameAndRole.Split('_')[0] : StaffNameAndRole; // this will be staff that will approves
                _logger.LogInformation("name " + name);
                // await con.ExecuteAsync("update users set Status=@status where username=@username", new { status = status, username = UserName });
                var staffid = (await con.QueryAsync<string>("select id from staff where email=@email", new { email = name + "@trustbancgroup.com" })).FirstOrDefault();
                if (string.IsNullOrEmpty(staffid))
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.InvalidStaffid };
                }
                return await _staffUserService.InitiateTask((int)usr.Id, status, StaffNameAndRole);
            }
            // return new GenericResponse() { Response = EnumResponse.Successful, Success = true };  
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public Task<GenericResponse> GetAUserKyc(int userId)
        {
            throw new NotImplementedException();
        }

        public Task<GenericResponse> GetAUserTransactionsOnMobile(int userId, string date1, string date2)
        {
            throw new NotImplementedException();
        }

        public Task<GenericResponse> GetDetailsOfAUser(int userId)
        {
            throw new NotImplementedException();
        }

        public Task<GenericResponse> SendMessageToUser(string clientKey)
        {
            throw new NotImplementedException();
        }

        private async void performSideTaskforAccountUpgrade(IDbConnection con, long userid, UpgradeAccountNo upgradeAccountNo)
        {
            await con.ExecuteAsync("update accountupgradedviachannel set accountiter=@accountiter,upgradedstatus=true,inititiated=true where userid=@id and accountnumber=@accountnumber", new { id = userid, accountnumber = upgradeAccountNo.AccountNo, accountiter = upgradeAccountNo.AccountTier });
        }
        public async Task<GenericResponse> UpgradeAUserAccount(IDbConnection con, string UserName, UpgradeAccountNo upgradeAccountNo)
        {
            try
            {
                GenericBLServiceHelper genericBLServiceHelper = new GenericBLServiceHelper();
                // 1 for active , 2 for deactivation
                // using IDbConnection con = _context.CreateConnection();
                var usr = await _genServ.GetUserbyUsername(UserName, con);
                var CustomerAccount = await _genServ.GetAccountbyCustomerId(usr.CustomerId);
                string AccountTier = upgradeAccountNo.AccountTier;
                if (!AccountTier.Contains("2") && !AccountTier.Contains("3"))
                {
                    return new GenericResponse() { Response = EnumResponse.WrongAccountTier, Success = false };
                }

                bool CheckAccount = CustomerAccount.balances.Any()
                  ? CustomerAccount.balances.Any(e => e.accountNumber.Equals(upgradeAccountNo.AccountNo, StringComparison.OrdinalIgnoreCase))
                   : false;
                if (!CheckAccount)
                {
                    return new GenericResponse() { Response = EnumResponse.WrongDetails, Success = false };
                }
                KycResponse kycResponse = (KycResponse)await this.CheckAUserKyc(UserName);
                if (kycResponse != null)
                {
                    if (!kycResponse.ProfileCompleted && AccountTier.Contains("3"))
                    {
                        return new GenericResponse() { Response = EnumResponse.ProfileNotCompleted, Success = false };
                    }
                    if (!kycResponse.idCard && !kycResponse.UtilityBill && AccountTier.Contains("2"))
                    {
                        return new GenericResponse() { Response = EnumResponse.UtilityBillError, Success = false };
                    }
                }
                CustomerDataNotFromBvn customerDataNotFromBvn = (await con.QueryAsync<CustomerDataNotFromBvn>("select * from customerdatanotfrombvn where userId=@userId", new { userId = usr.Id })).FirstOrDefault();
                if (kycResponse.IsNin & kycResponse.UtilityBill && kycResponse.idCard && AccountTier.Contains("2"))
                {
                    var response = await _genServ.UpgradeAccountNo(upgradeAccountNo);
                    var token = (await con.QueryAsync<string>("select DeviceToken from mobiledevice where userId=@userId", new { userId = usr.Id })).FirstOrDefault();
                    if (response.Success)
                    {
                        new Thread(async () =>
                        {
                            genericBLServiceHelper.sendMail(_genServ, customerDataNotFromBvn.Email, "TrustBanc Mobile App", $@"Dear {usr.Firstname.ToUpper()} {usr.LastName.ToUpper()} , 
                       your customer has been upgraded to {upgradeAccountNo.AccountTier.Replace("0", "")}.<br/>Thank you for Banking with us");
                            // send real time notififcations
                            await _notification.SendNotificationAsync(token, "TrustBanc Mobile App", $@"Dear {usr.Firstname.ToUpper()} {usr.LastName.ToUpper()}, your account has been upgraded to tier {upgradeAccountNo.AccountTier.Replace("0", "")}");
                        }).Start();
                        performSideTaskforAccountUpgrade(con, usr.Id, upgradeAccountNo);
                    }
                    return response;
                }
                if (kycResponse.IsNin && kycResponse.Passport && kycResponse.idCard
                    && kycResponse.Signature && kycResponse.isFilledEmploymentInfo
                    && kycResponse.isFillNextOfKininfo && AccountTier.Contains("3"))
                {
                    var response = await _genServ.UpgradeAccountNo(upgradeAccountNo);
                    _logger.LogInformation("upgrade account " + JsonConvert.SerializeObject(response));
                    var token = (await con.QueryAsync<string>("select DeviceToken from mobiledevice where userId=41 and DeviceToken is not null and DeviceToken!='';", new { userId = usr.Id })).ToList();
                    _logger.LogInformation("customer token " + token.Count);
                    if (response.Success)
                    {
                        new Thread(async () =>
                        {
                            genericBLServiceHelper.sendMail(_genServ, customerDataNotFromBvn.Email, "TrustBanc Mobile App", $@"Dear {usr.Firstname.ToUpper()} {usr.LastName.ToUpper()} , 
                       your customer has been upgraded to {upgradeAccountNo.AccountTier.Replace("0", "")}.<br/>Thank you for Banking with us");
                            // send real time notififcations
                            token.ForEach(async t =>
                            {
                                await _notification.SendNotificationAsync(t, "TrustBanc Mobile App", $@"Dear {usr.Firstname.ToUpper()} {usr.LastName.ToUpper()}, your account has been upgraded to tier {upgradeAccountNo.AccountTier.Replace("0", "")}");
                            });
                        }).Start();
                        performSideTaskforAccountUpgrade(con, usr.Id, upgradeAccountNo);
                    }
                    return response;
                    // return null;
                }
                return new GenericResponse() { Response = EnumResponse.ProfileNotCompleted, Success = false };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> CheckAUserKyc(string UserName)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var usr = await _genServ.GetUserbyUsername(UserName, con);
                var listOfDocumentType = (await con.QueryAsync<KycDocumentType>($"select * from document_type where userid=@userid", new { userid = usr.Id }));
                var idNumber = (await con.QueryAsync<string>($"select IdNumber from idcard_upload where userid=@userid", new { userid = usr.Id })).FirstOrDefault();
                var Nin = (await con.QueryAsync<string>($"select nin from registration where username=@username", new { username = usr.Username })).FirstOrDefault();
                _logger.LogInformation("customer nin " + Nin);
                var userid_customeremploymentinfo = (await con.QueryAsync<string>($"select * from customeremploymentinfo where userid=@userid", new { userid = usr.Id })).FirstOrDefault();
                var userid_nextofkininfo = (await con.QueryAsync<string>($"select * from next_kin_information where userid=@userid", new { userid = usr.Id })).FirstOrDefault();
                KycResponse kycResponse = new KycResponse();
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
                }
                kycResponse.Response = EnumResponse.Successful;
                return kycResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> GetUserKycEmploymentInfo(string userName)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var usr = await _genServ.GetUserbyUsername(userName, con);
                var userid_customeremploymentinfo = (await con.QueryAsync<EmploymentInfo>($"select * from customeremploymentinfo where userid=@userid", new { userid = usr.Id })).FirstOrDefault();
                //  var userid_nextofkininfo = (await con.QueryAsync<string>($"select * from next_kin_information where userid=@userid", new { userid = usr.Id })).FirstOrDefault();
                return new PrimeAdminResponse() { Response = EnumResponse.Successful, Data = userid_customeremploymentinfo, Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> GetUserKycDocument(string userName, string path)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var usr = await _genServ.GetUserbyUsername(userName, con);
                var listOfDocumentType = (await con.QueryAsync<KycDocumentType>($"select Document,filelocation from document_type where userid=@userid", new { userid = usr.Id })).ToList();
                _logger.LogInformation("listOfDocumentType " + listOfDocumentType.Any() + " " + listOfDocumentType);
                /*
                if (!listOfDocumentType.Any())
                {
                    return new GenericResponse() { Response = EnumResponse.Successful, Success = true };
                }
                */
                var idCard = (await con.QueryAsync<IdCard>($"select ExpiryDate,IssueDate,IdType,IdNumber,Filelocation from idcard_upload where userId=@userid", new { userid = usr.Id })).FirstOrDefault();
                if (idCard != null)
                {
                    idCard.Filelocation = path + _settings.Urlfilepath + "/KycView/" + Path.GetFileName(idCard.Filelocation);
                }
                // var selectedImagePaths = listOfDocumentType.Select(file => new { filelocation = path + _settings.Urlfilepath + "/KycView/" + Path.GetFileName(file.filelocation), Document = file.document }).ToList();
                //FileLocation = Path.Combine(path, _settings.Urlfilepath, "KycView", Path.GetFileName(file.filelocation)),
                //Document = file.document
                var selectedImagePaths = listOfDocumentType
                    .Where(file => file != null) // Ensure we filter out null files
                    .Select(file => new
                    {
                        FileLocation = path + _settings.Urlfilepath + "/KycView/" + Path.GetFileName(file.filelocation),
                        Document = file.document
                    })
                    .ToList();
                return new PrimeAdminResponse() { Response = EnumResponse.Successful, Data = new { selectedImagePaths, idCard }, Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }
        private bool IsImageFile(string filePath)
        {
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp" };
            return imageExtensions.Contains(Path.GetExtension(filePath).ToLower());
        }

        public async Task<GenericResponse> GetKycNextOfKinInfo(string userName)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var usr = await _genServ.GetUserbyUsername(userName, con);
                var nextOfKinInfos = (await con.QueryAsync<NextOfKinInfoInPrimeAdmin>($"select * from next_kin_information where userId=@userid", new { userid = usr.Id })).ToList();
                return new PrimeAdminResponse() { Response = EnumResponse.Successful, Success = true, Data = nextOfKinInfos };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> GetAUserMobileTransactionHistory(int page, int size, string username, string StartDate, string EndDate)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    Console.WriteLine(con.ConnectionString, "database " + con.Database);
                    con.Open();
                    Console.WriteLine("state " + con.State);
                    var usr = await _genServ.GetUserbyUsername(username, con);
                    StartDate = DateTime.ParseExact(StartDate, "dd-MM-yyyy", CultureInfo.InvariantCulture)
                         .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    EndDate = DateTime.ParseExact(EndDate, "dd-MM-yyyy", CultureInfo.InvariantCulture).AddDays(1)
                        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    int skip = (page - 1) * size;
                    int take = size;
                    var parameters = new
                    {
                        StartDate = StartDate,
                        EndDate = EndDate,
                        Skip = skip,
                        Take = take,
                        id = usr.Id
                    };
                    Console.WriteLine("parameters " + parameters);
                    string query = $@"
                                            select  transtype,
                                            DATE_FORMAT(createdon, '%d/%m/%Y %H:%i:%S') AS createdon,
                                            Transaction_Ref AS TransRef,Destination_Account,
                                            TransID,
                                            Source_Account,
                                            Channel_Id,
                                            Charge,
                                            Success,
                                            Amount,
                                            Destination_AccountName,
                                            Narration,Destination_BankName,
                                            Destination_BankCode
                                            from transfer where User_id=@id and createdon >= @StartDate and createdon <= @EndDate order by createdon desc limit @Take offset @Skip";
                    var listoftransaction = await con.QueryAsync<MobileTransactionListRequest>(query, new { id = usr.Id, StartDate = StartDate, EndDate = EndDate, Take = take, Skip = skip });
                    if (!listoftransaction.Any())
                        return new TransactionRequest() { Response = EnumResponse.NoTransactionFound, Success = true, Transactions = new List<TransactionListRequest>() };
                    return new PrimeAdminResponse()
                    {
                        Response = EnumResponse.Successful,
                        Success = true,
                        Data = new { transactions = listoftransaction.Any() ? listoftransaction.ToList() : Enumerable.Empty<MobileTransactionListRequest>(), page = page, size = size }
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                Console.WriteLine(ex.Message + " " + ex.StackTrace);
                return new TransactionRequest() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> GetUserAccountDetailsWithKycLevel(string userName)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var usr = await _genServ.GetUserbyUsername(userName, con);
                    BalanceEnquiryResponse balanceEnquiryResponse = await _genServ.GetAccountbyCustomerId(usr.CustomerId);
                    return new PrimeAdminResponse() { Response = EnumResponse.Successful, Data = new { balanceEnquiryResponse, channelstatus = usr.Status == 1 ? "active" : "inactive" }, Success = true };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new TransactionRequest() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> SearchUserByName(int page, int size, string host, string searchTerm)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                int skip = (page - 1) * size;
                int take = size;
                Console.WriteLine("searchTerm " + searchTerm);
                // string sql = @"SELECT * FROM users ORDER BY id ASC OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";
                // string sql = "SELECT (select nin from registration where username=u.username) as nin,u.LastName,u.Username,u.Address,u.Bvn,u.CustomerId,u.Firstname,u.BvnPhoneNumber,u.BvnEmail,if(u.Status==1) then true else false,u.ProfilePicture FROM users u ORDER BY id ASC LIMIT @Take OFFSET @Skip";
                /*
                string sql = $@"SELECT  (SELECT Email FROM customerdatanotfrombvn WHERE userId = u.id) as CustomerEmail,
                                        (SELECT PhoneNumber FROM customerdatanotfrombvn WHERE userId = u.id) as CustomerPhoneNumber,
                                        (SELECT Address FROM customerdatanotfrombvn WHERE userId = u.id) as CustomerAddress,
                                        (SELECT nin FROM registration WHERE username = u.username) AS nin,
                                        u.LastName,
                                        u.Username,
                                        u.Address,
                                        u.Bvn,
                                        u.CustomerId,
                                        u.Firstname,
                                        u.PhoneNumber as BvnPhoneNumber,
                                        u.Email as BvnEmail,
                                        CASE WHEN u.Status = 1 THEN TRUE ELSE FALSE END AS Active,
                                        u.ProfilePic as ProfilePicture 
                                    FROM 
                                        users u where u.Firstname like '%{_genServ.RemoveSpecialCharacters(searchTerm)}%'
                                    ";
                               */
                string sql = @"
                                                SELECT  
                                                    cdnb.Email AS CustomerEmail,
                                                    cdnb.PhoneNumber AS CustomerPhoneNumber,
                                                    cdnb.Address AS CustomerAddress,
                                                    reg.nin AS nin,
                                                    u.LastName,
                                                    u.Username,
                                                    u.Address,
                                                    u.Bvn,
                                                    u.CustomerId,
                                                    u.Firstname,
                                                    u.PhoneNumber AS BvnPhoneNumber,
                                                    u.Email AS BvnEmail,
                                                    CASE WHEN u.Status = 1 THEN TRUE ELSE FALSE END AS Active,
                                                    u.ProfilePic AS ProfilePicture 
                                                FROM 
                                                    users u
                                                LEFT JOIN 
                                                    customerdatanotfrombvn cdnb ON cdnb.userId = u.id
                                                LEFT JOIN 
                                                    registration reg ON reg.username = u.username
                                                WHERE 
                                                    u.Firstname LIKE @searchTerm
                                            ";
                var parameters = new { searchTerm = "%" + _genServ.RemoveSpecialCharacters(searchTerm) + "%" };
                var result = (await con.QueryAsync<MobileUsers>(sql, parameters)).ToList();
                result.ForEach(x =>
                {
                    _logger.LogInformation("ProfilePicture " + Path.GetFileName(x.ProfilePicture));
                    x.ProfilePicture = x.ProfilePicture != null ? host + _settings.Urlfilepath + "/KycView2/" + Path.GetFileName(x.ProfilePicture) : null;
                });
                return new PrimeAdminResponse() { Success = true, Data = new { response = result, page = page, size = size }, Response = EnumResponse.Successful };

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> SearchUserByBvn(int page, int size, string host, string bvnSearch)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                int skip = (page - 1) * size;
                int take = size;
                Console.WriteLine("bvnSearch " + bvnSearch);
                // string sql = @"SELECT * FROM users ORDER BY id ASC OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";
                // string sql = "SELECT (select nin from registration where username=u.username) as nin,u.LastName,u.Username,u.Address,u.Bvn,u.CustomerId,u.Firstname,u.BvnPhoneNumber,u.BvnEmail,if(u.Status==1) then true else false,u.ProfilePicture FROM users u ORDER BY id ASC LIMIT @Take OFFSET @Skip";
                /*
                string sql = $@"SELECT  (SELECT Email FROM customerdatanotfrombvn WHERE userId = u.id) as CustomerEmail,
                                        (SELECT PhoneNumber FROM customerdatanotfrombvn WHERE userId = u.id) as CustomerPhoneNumber,
                                        (SELECT Address FROM customerdatanotfrombvn WHERE userId = u.id) as CustomerAddress,
                                        (SELECT nin FROM registration WHERE username = u.username) AS nin,
                                        u.LastName,
                                        u.Username,
                                        u.Address,
                                        u.Bvn,
                                        u.CustomerId,
                                        u.Firstname,
                                        u.PhoneNumber as BvnPhoneNumber,
                                        u.Email as BvnEmail,
                                        CASE WHEN u.Status = 1 THEN TRUE ELSE FALSE END AS Active,
                                        u.ProfilePic as ProfilePicture 
                                    FROM 
                                        users u where u.Bvn like '%{_genServ.RemoveSpecialCharacters(bvnSearch)}%'
                                    ";
                             */
                string sql = @"
                                        SELECT  
                                            cdnb.Email AS CustomerEmail,
                                            cdnb.PhoneNumber AS CustomerPhoneNumber,
                                            cdnb.Address AS CustomerAddress,
                                            reg.nin AS nin,
                                            u.LastName,
                                            u.Username,
                                            u.Address,
                                            u.Bvn,
                                            u.CustomerId,
                                            u.Firstname,
                                            u.PhoneNumber AS BvnPhoneNumber,
                                            u.Email AS BvnEmail,
                                            CASE WHEN u.Status = 1 THEN TRUE ELSE FALSE END AS Active,
                                            u.ProfilePic AS ProfilePicture 
                                        FROM 
                                            users u
                                        LEFT JOIN 
                                            customerdatanotfrombvn cdnb ON cdnb.userId = u.id
                                        LEFT JOIN 
                                            registration reg ON reg.username = u.username
                                        WHERE 
                                            u.Bvn LIKE @bvnSearch
                                    ";

                var parameters = new { bvnSearch = "%" + _genServ.RemoveSpecialCharacters(bvnSearch) + "%" };

                //22206908517
                var result = (await con.QueryAsync<MobileUsers>(sql, parameters)).ToList();
                result.ForEach(x =>
                {
                    x.ProfilePicture = x.ProfilePicture != null ? host + _settings.Urlfilepath + "/KycView2/" + Path.GetFileName(x.ProfilePicture) : null;
                });
                return new PrimeAdminResponse() { Success = true, Data = new { response = result, page = page, size = size }, Response = EnumResponse.Successful };

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> AddAdvertImageOrPictures(bool active, string userName, IFormFile image)
        {
            try
            {
                if (image == null || image.Length == 0)
                {
                    return new GenericResponse() { Response = EnumResponse.WrongFileFormat, Success = false };
                }
                // Get the file extension
                var fileExtension = Path.GetExtension(image.FileName).ToLowerInvariant();
                // Check for specific file extensions (e.g., .jpg, .png, .jpeg)
                if (fileExtension != ".png" && fileExtension != ".jpeg")
                {
                    return new GenericResponse()
                    {
                        Response = EnumResponse.WrongFileFormat,
                        Message = "Wrong file format."
                    };
                }
                string filePath = await _fileService.SaveAdvertFileAsync(image, active);
                Console.WriteLine("filePath " + filePath);
                return new GenericResponse()
                {
                    Success = true,
                    Response = EnumResponse.Successful,
                    Message = "file uploaded successfully"
                };
            }
            catch (Exception ex)
            {
                return new GenericResponse()
                {
                    Success = false,
                    Response = EnumResponse.NotSuccessful,
                    Message = ex.Message
                };
            }
        }

        public async Task<GenericResponse> GetAdvertImagesorPromoImage(string baseurl, int page, int size)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var response = await _fileService.GetAdvertImageOrPictures(baseurl, page, size);
                    // var imageList = await con.QueryAsync<string>("SELECT id,activeimg,imagename FROM advertimage WHERE activeimg = true limit 10");
                    //var selectedImagePaths = imageList.Select(file => Path.Combine(_folderPaths.Uploads, file)).ToList();
                    // var imageFiles = selectedImagePaths.Where(file => IsImageFile(file));
                    // return imageFiles.Select(file => $"{baseUrl}/api/FileService/BrowserView/{Path.GetFileName(file)}");
                    // return imageFiles.Select(file => $"{baseUrl}/omnichannel_authentication/api/FileService/BrowserView/{Path.GetFileName(file)}");
                    return new PrimeAdminResponse() { Response = EnumResponse.Successful, Success = true, Message = "Successful", Data = new { response, page, size } };
                }
            }
            catch (Exception ex)
            {
                // Log the error
                _logger.LogInformation(ex.Message);
                return new GenericResponse() { Response = EnumResponse.NotSuccessful, Message = ex.Message };
            }

        }

        public async Task<GenericResponse> EditAdvertImagesorPromoImage(List<ImageUpdate> ids)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    // var  myIds = ids.Select(e=>e.id);
                    // var myActiveImg = ids.Select(e=>e.status);//status is a bool
                    //var myListofIds =(await con.QueryAsync<long>("select id from advertimage where id in @Ids and activeimg=@imgstatus",new { Ids=myIds, imgstatus = myActiveImg })).ToList();
                    var myIds = new List<long>();
                    var myStatus = new List<bool>();
                    /*
                     AdvertImageUpdate matchedItem = null;
                     foreach (var item in ids)
                     {
                         var id = item.id;
                         var status = item.status;
                        matchedItem = (await con.QueryAsync<AdvertImageUpdate>("SELECT id, activeimg FROM advertimage WHERE id = @Id AND activeimg = @ImgStatus",new { Id = id, ImgStatus = status })).FirstOrDefault();
                         if (matchedItem!=null) { 
                           myIds.Add(matchedItem.Id);
                           myStatus.Add(matchedItem.ActiveImg);
                           }
                     }
                     */
                    AdvertImageUpdate matchedItem = null;
                    foreach (var item in ids)
                    {
                        var id = item.id;           // Changed to PascalCase
                        var status = item.status;   // Changed to PascalCase
                        matchedItem = (await con.QueryAsync<AdvertImageUpdate>("SELECT id, activeimg FROM advertimage WHERE id = @Id", new { Id = id })).FirstOrDefault();

                        if (matchedItem != null)
                        {
                            myIds.Add(matchedItem.Id);
                            myStatus.Add(status);
                        }
                    }
                    if (!myIds.Any())
                    {
                        return new GenericResponse() { Response = EnumResponse.Successful, Success = false };
                    }
                    var myData = myIds.Select((id, index) => new { id, status = myStatus[index] }).ToList();
                    Console.WriteLine("myData " + myData);
                    _logger.LogInformation("myData " + myData);
                    await con.ExecuteAsync(
                        "UPDATE advertimage SET activeimg = @status WHERE id = @id",
                        myData
                    );
                    //await con.ExecuteAsync("update advertimage set activeimg=@status where id in @Ids", new { Ids = myIds,status=ids.Where(id=>id==ids)});
                    return new GenericResponse() { Response = EnumResponse.Successful, Success = true };
                }
            }
            catch (Exception ex)
            {
                // Log the error
                _logger.LogInformation(ex.Message);
                throw;
            }
        }

        public async Task<GenericResponse2> GetCustomerInterBankTransactionusStatus(string transRef, string transId)
        {
            // GetTransactionStatusV2 / TRST - INT - 034619027497074
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    //  https://localhost:44396/AccessOutward/GetTransactionStatusV2/TRST-INT-034619027497074
                    GetTransactionStatus getTransactionStatus = await _genServ.CallServiceAsync<GetTransactionStatus>(Method.GET, $"{_settings.AccessUrl}AccessOutward/GetTransactionStatusV2/" + transRef, null, true, null);
                    if (getTransactionStatus != null)
                    {
                        if (getTransactionStatus.Status == 2)
                        {
                            getTransactionStatus.StatusRemark = getTransactionStatus.StatusRemark + ",Transaction in Progress to credit DestinationAccount";
                        }
                        else if (getTransactionStatus.Status == 6)
                        {
                            getTransactionStatus.StatusRemark = getTransactionStatus.StatusRemark + ",Transaction in Progress to credit DestinationAccount";
                        }
                        else if (getTransactionStatus.Status == 4)
                        {
                            getTransactionStatus.StatusRemark = getTransactionStatus.StatusRemark + ".SourceAccount debited,DestinationAccount credited";
                            await con.ExecuteAsync("update transfer set Success=1 where Transaction_Ref=@transRef", new { transRef = transRef });
                        }
                        else if (getTransactionStatus.Status == 5)
                        {
                            getTransactionStatus.StatusRemark = getTransactionStatus.StatusRemark + " SourceAccount debited but failed to credit DestinationAccount";
                        }
                        return new GenericResponse2()
                        {
                            Response = EnumResponse.Successful,
                            Success = true,
                            data = getTransactionStatus
                        };
                    }
                    else
                    {
                        return new GenericResponse2()
                        {
                            Response = EnumResponse.NotSuccessful,
                        };
                    }

                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                Console.WriteLine(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }


        public async Task<GenericResponse2> GetCustomerInterBankTransactionusStatusForOutward(string transRef, string transId)
        {
            // GetTransactionStatusV2 / TRST - INT - 034619027497074
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    //  https://localhost:44396/AccessOutward/GetTransactionStatusV2/TRST-INT-034619027497074
                    GetTransactionStatus getTransactionStatus = await _genServ.CallServiceAsync<GetTransactionStatus>(Method.GET, $"{_settings.AccessUrl}AccessOutward/GetTransactionStatusV2/" + transRef, null, true, null);
                    if (getTransactionStatus != null)
                    {
                        if (getTransactionStatus.Status == 2)
                        {
                            getTransactionStatus.StatusRemark = "Transaction in Progress";
                        }
                        else if (getTransactionStatus.Status == 4)
                        {
                            getTransactionStatus.StatusRemark = "Transaction successful";
                        }
                        else if (getTransactionStatus.Status == 5)
                        {
                            getTransactionStatus.StatusRemark = "Account debited but credit failed.Retry/Reversal in progress";
                        }
                        return new GenericResponse2()
                        {
                            Response = EnumResponse.Successful,
                            Success = true,
                            data = getTransactionStatus
                        };
                    }
                    else
                    {
                        return new GenericResponse2()
                        {
                            Response = EnumResponse.NotSuccessful,
                        };
                    }

                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                Console.WriteLine(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse2> GetCustomerIntrabankTransactionusStatus(string transRef, string transId)
        {
            // GetTransactionStatusV2 / TRST - INT - 034619027497074
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {

                    // call finedge here to get details
                    string response = await _genServ.CallServiceAsyncToString(Method.GET, $"{_settings.FinedgeUrl}api/Enquiry/CheckTransactionStatus/" + transRef, null, true, null);
                    //GenericResponse2 fixedDepositsRoot = JsonConvert.DeserializeObject<GenericResponse2>(response);
                    GenericResponse2 getTransactionStatusIntra = JsonConvert.DeserializeObject<GenericResponse2>(response);
                    Console.WriteLine("getTransactionStatusIntra " + getTransactionStatusIntra.data);
                    _logger.LogInformation("getTransactionStatusIntra " + getTransactionStatusIntra.data);
                    GetTransactionStatusIntra getTransactionStatusIntra1 = JsonConvert.DeserializeObject<GetTransactionStatusIntra>(JsonConvert.SerializeObject(getTransactionStatusIntra.data));
                    if (!getTransactionStatusIntra.Success)
                    {
                        return new GenericResponse2()
                        {
                            Success = false,
                            Response = EnumResponse.NotSuccessful
                        };
                    }
                    return new GenericResponse2()
                    {
                        data = getTransactionStatusIntra1,
                        Success = true,
                        Response = EnumResponse.Successful
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                Console.WriteLine(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse2> FetchUser(string userName, string host)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                string sql = $@"SELECT  (SELECT Email FROM customerdatanotfrombvn WHERE userId = u.id) as CustomerEmail,
                                        (SELECT PhoneNumber FROM customerdatanotfrombvn WHERE userId = u.id) as CustomerPhoneNumber,
                                        (SELECT Address FROM customerdatanotfrombvn WHERE userId = u.id) as CustomerAddress,
                                        (SELECT nin FROM registration WHERE username = u.username) AS nin,
                                        u.LastName,
                                        u.Address,
                                        u.Bvn,
                                        u.CustomerId,
                                        u.Firstname,
                                        u.PhoneNumber as BvnPhoneNumber,
                                        u.Email as BvnEmail,
                                        CASE WHEN u.Status = 1 THEN TRUE ELSE FALSE END AS Active,
                                        u.ProfilePic as ProfilePicture 
                                    FROM 
                                        users u where u.Username=@Username
                                    ";
                var result = (await con.QueryAsync<MobileUsers>(sql, new { Username = userName })).ToList();
                result.ForEach(x =>
                {
                    x.ProfilePicture = x.ProfilePicture != null ? host + _settings.Urlfilepath + "/KycView/" + Path.GetFileName(x.ProfilePicture) : null;
                });
                return new GenericResponse2() { Success = true, data = result, Response = EnumResponse.Successful };

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> DeleteAdvertImagesorPromoImage(string host, List<int> ids)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                // var imageList = (await con.QueryAsync<List<string>>($@"SELECT imagename FROM advertimage where id in @ids", new { id = ids })).ToList();
                var imageList = (await con.QueryAsync<string>("SELECT imagename FROM advertimage WHERE id IN @ids", new { ids = ids })).ToList();
                // bool result = await _fileService.DeleteFilesAsync(imageList);
                bool result = await _fileService.DeleteAdvertFilesAsync(imageList);
                if (result)
                {
                    // await con.ExecuteAsync("DELETE FROM advertimage WHERE id IN @imagename", new { imagename = ids });
                    await con.ExecuteAsync("DELETE FROM advertimage WHERE id IN @imagenames", new { imagenames = ids });
                    return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
                }
                else
                    return new GenericResponse() { Success = true, Response = EnumResponse.NotSuccessful };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse2> GetActiveAndInActiveCustomer()
        {
            try
            {
                // call finedge here to get details
                string response = await _genServ.CallServiceAsyncToString(Method.GET, $"{_settings.FinedgeUrl}api/Enquiry/GetActiveAndInActiveAccountsCounter", null, true, null);
                //GenericResponse2 fixedDepositsRoot = JsonConvert.DeserializeObject<GenericResponse2>(response);
                GenericResponse2 getStatus = JsonConvert.DeserializeObject<GenericResponse2>(response);
                Console.WriteLine("getTransactionStatusIntra " + getStatus.data);
                _logger.LogInformation("getTransactionStatusIntra " + getStatus.data);
                GetActiveAndInActiveAccountStatus getTransactionStatusIntra1 = JsonConvert.DeserializeObject<GetActiveAndInActiveAccountStatus>(JsonConvert.SerializeObject(getStatus.data));
                if (!getStatus.Success)
                {
                    return new GenericResponse2()
                    {
                        Success = false,
                        Response = EnumResponse.NotSuccessful
                    };
                }
                return new GenericResponse2()
                {
                    data = getTransactionStatusIntra1,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse2> GetMobileActiveAndInActiveCustomer()
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var ActiveCustomer = (await con.QueryAsync<int>($@"SELECT COUNT(DISTINCT Source_Account) AS ActiveAccounts
                                                            FROM transfer
                                                            WHERE createdon >= DATE_SUB(CURDATE(), INTERVAL 6 MONTH)
                                                            ")).FirstOrDefault();
                var InActiveCustomer = (await con.QueryAsync<int>($@"SELECT COUNT(*) AS InactiveAccounts
                            FROM users u
                            WHERE NOT EXISTS (
                                SELECT 1
                                FROM transfer t
                                WHERE t.user_id = u.id
                                  AND t.createdon >= DATE_SUB(CURDATE(), INTERVAL 6 MONTH)
                            );
                            ")).FirstOrDefault();
                var InActiveCustomerDetails = (await con.QueryAsync<InActiveUsers>($@"SELECT u.username,u.Firstname,u.Lastname,(select c.PhoneNumber from customerdatanotfrombvn c where u.username=c.username) as PhoneNumber,(select c.Email from customerdatanotfrombvn c where u.username=c.username) as Email
                            FROM users u
                            WHERE NOT EXISTS (
                                SELECT 1
                                FROM transfer t
                                WHERE t.user_id = u.id
                                  AND t.createdon >= DATE_SUB(CURDATE(), INTERVAL 6 MONTH)
                            );
                            ")).ToList();
                return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = new { ActiveCustomer = ActiveCustomer, InActiveCustomer = InActiveCustomer, InActiveCustomerDetails = InActiveCustomerDetails } };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse2> GetPendingKycCount()
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var listofpendingKyc = await con.QueryAsync<PendingKycStatus>("select kycstatus,username from customerkycstatus where kycstatus=false");
                    if (listofpendingKyc.Any())
                    {
                        // listofpendingKyc = listofpendingKyc.ToList();
                        var pendingUsernames = listofpendingKyc.Select(kyc => kyc.Username).ToList();
                        return new GenericResponse2() { Success = true, Response = EnumResponse.Successful, data = pendingUsernames.Count };
                    }
                    else
                    {
                        return new GenericResponse2() { Success = true, Response = EnumResponse.Successful, data = 0 };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse2> GetPendingKyc()
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    // submittedrequest 1 for new submission,2 for inititated
                    string submittedrequestquery = "select d.Document as typeofdocument, (select username from users where id=d.USERID) as username from document_type d where d.submittedrequest=0";
                    // var listofpendingKyc = await con.QueryAsync<PendingKycStatus>("select kycstatus,username from customerkycstatus where kycstatus=false");
                    var listofpendingKyc = await con.QueryAsync<PendingKycStatus>(submittedrequestquery);
                    if (listofpendingKyc.Any())
                    {
                        //var pendingUsernames = listofpendingKyc.Select(kyc => kyc.Username).ToList();
                        var pendingUsernames = new List<PendingKycStatus>();
                        foreach (var item in listofpendingKyc)
                        {
                            pendingUsernames.Add(item);
                        }
                        string idcardsubmittedrequestquery = "select (select username from users where id=d.UserId) as username from idcard_upload d where d.submittedrequest=0";
                        var idcardlistofpendingKyc = await con.QueryAsync<PendingKycStatus>(idcardsubmittedrequestquery);
                        if (idcardlistofpendingKyc.Any())
                        {
                            foreach (var idcardpendingKyc in idcardlistofpendingKyc)
                            {
                                idcardpendingKyc.typeofdocument = "idcard";
                                pendingUsernames.Add(idcardpendingKyc);
                            }
                        }
                        return new GenericResponse2() { Success = true, Response = EnumResponse.Successful, data = new { pendingUsernames } };
                    }
                    else
                    {
                        string idcardsubmittedrequestquery = "select (select username from users where id=d.UserId) as username from idcard_upload d where d.submittedrequest=0";
                        var idcardlist = new List<PendingKycStatus>();
                        var idcardlistofpendingKyc = await con.QueryAsync<PendingKycStatus>(idcardsubmittedrequestquery);
                        if (idcardlistofpendingKyc.Any())
                        {
                            foreach (var idcardpendingKyc in idcardlistofpendingKyc)
                            {
                                idcardpendingKyc.typeofdocument = "idcard";
                                idcardlist.Add(idcardpendingKyc);
                            }
                        }
                        return new GenericResponse2() { Success = true, Response = EnumResponse.Successful, data = idcardlist };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse2> SetPendingKycAsTreated(string username)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    await con.ExecuteAsync("update customerkycstatus set kycstatus=@status where username=@username", new { status = true, username = username });
                    return new GenericResponse2() { Success = true, Response = EnumResponse.Successful };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse2> GetPendingAccountLimitUpdate()
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var counter = await con.QueryAsync<int>("select count(indemnitystatus) from customerindemnity where indemnitystatus=@status", new { status = "Awaiting review" });
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = counter };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse2> GetPendingCustomerAccountLimitUpdate()
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var useridlist = (await con.QueryAsync<long>("select userid from customerindemnity where indemnitystatus=@status and (indemnityapproval=false or indemnityapproval is null)", new { status = "Awaiting review" })).ToList();

                    /*
                    var useridlist = (await con.QueryAsync<CustomerIndemnity>(
                                            "SELECT c.accounttier,c.Singledeposittransactionlimit,c.Dailywithdrawaltransactionlimit,createdAt,indemnitystatus,(select Username from users where u.id=c.userid) as Username,(select firstname from users where u.id=c.userid) as firstname,(select lastname from users where u.id=c.userid) as lastname FROM customerindemnity c WHERE c.indemnitystatus = @status and c.indemnityapproval=false",
                                            new { status = "Awaiting review" }
                                        )).ToList();
                    */
                    var usernamelist = (await con.QueryAsync<string>("select username from users where id in @list", new { list = useridlist })).ToList();
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = usernamelist };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse2> SetPendingCustomerAccountLimitUpdateAsTreated(string username)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var usr = await _genServ.GetUserbyUsername(username, con);
                    var indemnitystatus = (await con.QueryAsync<string>("select indemnitystatus from customerindemnity where indemnitystatus='accept' and userid=@userid", new { userid = usr.Id })).FirstOrDefault();
                    if (string.IsNullOrEmpty(indemnitystatus))
                    {
                        return new GenericResponse2() { Response = EnumResponse.NotSuccessful, Success = false };
                    }
                    await con.ExecuteAsync("update customerindemnity set indemnityapproval=@status where userid=@userid and indemnitystatus=@indemnitystatus", new { status = true, userid = usr.Id, indemnitystatus = "accept" });
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> InitiateCustomerIndmnityLimitAcceptance(string username, string status, string StaffNameAndRole)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var usr = await _genServ.GetUserbyUsername(username, con);
                    await con.ExecuteAsync("update customerindemnity set initiated=true where indemnityapproval=false and IndemnityType='customerindemnity' and userid=@id", new { id = usr.Id });
                    return await _staffUserService.InitiateTask((int)usr.Id, status, StaffNameAndRole);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse2> GetCustomerAccountLimitUpdate(string username, string baseUrl)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    // this dto CustomerAccountLimit also contain the customerindemnity details
                    var usr = await _genServ.GetUserbyUsername(username, con);
                    var CustomerAccountLimit = (await con.QueryAsync<CustomerAccountLimit>("select * from customerindemnity where userid=@userid", new { userid = usr.Id })).FirstOrDefault();
                    CustomerAccountLimit.indemnityformpath = $"{baseUrl}/PrimeUser/FileView/{Path.GetFileName(CustomerAccountLimit.indemnityformpath)}";
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = CustomerAccountLimit };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> GetCountOfTransactionFortheMonth()
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var counterbill = (await con.QueryAsync<int>($"SELECT COUNT(*)  FROM transfer WHERE MONTH(createdon) = MONTH(NOW()) AND YEAR(createdon) = YEAR(NOW()) and transtype='bill';")).FirstOrDefault();
                    var counter = (await con.QueryAsync<int>($"SELECT COUNT(*)  FROM transfer WHERE MONTH(createdon) = MONTH(NOW()) AND YEAR(createdon) = YEAR(NOW()) and transtype='transfer';")).FirstOrDefault();
                    return new PrimeAdminResponse() { Response = EnumResponse.Successful, Success = true, Data = new { Biller = counterbill, transfer = counter } };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> InitiateAccountIndemnityLimitAcceptance(string username, string status, string staffNameAndRole, string accountNumber)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var usr = await _genServ.GetUserbyUsername(username, con);
                    await con.ExecuteAsync("update customerindemnity set initiated=true where IndemnityType='accountindemnity' and userid=@id", new { id = usr.Id });
                    return await _staffUserService.InitiateTask((int)usr.Id, status, staffNameAndRole, accountNumber);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse2> UpdatePhoneNumberAndEmail(string userName, string phoneNumber, string email)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var usr = await _genServ.GetUserbyUsername(userName, con);
                    if (usr == null)
                    {
                        return new GenericResponse2() { Response = EnumResponse.UserNotFound, Message = "User not found" };
                    }
                    string query = "";
                    var balanceEnquiryResponse = await _genServ.GetAccountbyCustomerId(usr.CustomerId);
                    var finedgeSearchBvn = await _genServ.GetCustomerbyAccountNo(balanceEnquiryResponse.balances.ElementAtOrDefault(0).accountNumber);
                    _logger.LogInformation("email " + finedgeSearchBvn?.result.email);
                    _logger.LogInformation("phoneNumber " + finedgeSearchBvn?.result.mobile);
                    //do an update on the db side
                    if (string.IsNullOrEmpty(phoneNumber))
                    {
                        await con.ExecuteAsync("update customerdatanotfrombvn set PhoneNumber=@PhoneNumber where userid=@userid", new { PhoneNumber = finedgeSearchBvn?.result.mobile, userid = usr.Id });
                        // await con.ExecuteAsync("update registration set Email=@Email where Username=@Username", new { Username = userName });
                        await con.ExecuteAsync("update users set PhoneNumber=@PhoneNumber where Username=@Username", new { PhoneNumber = finedgeSearchBvn?.result.mobile, Username = userName });
                    }
                    if (string.IsNullOrEmpty(email))
                    {
                        await con.ExecuteAsync("update registration set Email=@Email where Username=@Username", new { Email = finedgeSearchBvn?.result.email, Username = userName });
                        await con.ExecuteAsync("update users set Email=@Email where Username=@Username", new { Email = finedgeSearchBvn?.result.email, Username = userName });
                    }
                    return new GenericResponse2() { Response = EnumResponse.Successful };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> Initiateupgradeaccount(string userName, string accounttier, string AccountNumber, string actionName, string staffNameAndRole)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var usr = await _genServ.GetUserbyUsername(userName, con);
                    if (usr == null)
                    {
                        return new GenericResponse() { Response = EnumResponse.InvalidUsernameOrPassword, Success = false };
                    }
                    //check account
                    long userid = (await con.QueryAsync<long>("select userid from accountupgradedviachannel where userid=@id and accountnumber=@accountnumber", new { id = usr.Id, accountnumber = AccountNumber })).FirstOrDefault();
                    if (userid != 0)
                    {
                        await con.ExecuteAsync("update accountupgradedviachannel set accountiter=@accountiter,upgradedstatus=false,inititiated=true where userid=@id", new { id = usr.Id, accountiter = accounttier });
                        return await _staffUserService.InitiateTask((int)usr.Id, actionName, staffNameAndRole);
                    }
                    //insert the account
                    await con.ExecuteAsync("insert into accountupgradedviachannel(accountnumber,userid,accountiter,inititiated) values(@accountnumber,@id,@accountiter,true)", new { accountnumber = AccountNumber, id = usr.Id, accountiter = accounttier });
                    return await _staffUserService.InitiateTask((int)usr.Id, actionName, staffNameAndRole);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> GetPendingListOfAccountTobeUpgraded()
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                // string query = $@"select sta.id as actionid,(select actioname from checkeraction where id=sta.action) as actioname,concat((select firstname from staff s where s.id=sta.initiationstaff),' ',(select email from staff s where s.id=sta.initiationstaff)) as name,(select email from staff s where s.id=sta.initiationstaff) as email from staffaction sta where sta.action=20";
                // string query = $@"select a.accountiter as AccountTier,a.accountnumber as AccountNumber,sta.id as actionid,(select actioname from checkeraction where id=sta.action) as actioname,(select username from users s where s.id=sta.staffidtoaction) as username,(select firstname from users s where s.id=sta.staffidtoaction) as firstname,concat((select firstname from users s where s.id=sta.staffidtoaction),' ',(select lastname from users s where s.id=sta.initiationstaff)) as name,(select email from staff s where s.id=sta.initiationstaff) as email from staffaction sta join accountupgradedviachannel a on a.userid=sta.staffidtoaction,accountupgradedviachannel a join users u where sta.action=22 and sta.approvalstatus=false";
                string query = $@"SELECT 
                                            a.accountiter AS AccountTier,
                                            a.accountnumber AS AccountNumber,
                                            sta.id AS ActionID,
                                            ca.actioname AS ActionName,
                                            u.username AS UserName,
                                            u.firstname AS FirstName,
                                            CONCAT(u.firstname, ' ', u.lastname) AS name,
                                            st.email AS Email
                                        FROM 
                                            staffaction sta
                                        JOIN 
                                            accountupgradedviachannel a ON a.userid = sta.staffidtoaction
                                            AND upgradedstatus=false
                                        JOIN 
                                            users u ON u.id = sta.staffidtoaction
                                        JOIN 
                                            checkeraction ca ON ca.id = sta.action
                                        JOIN 
                                            staff st ON st.id = sta.initiationstaff
                                        WHERE 
                                            sta.action = 22 
                                            AND sta.approvalstatus = FALSE;
                                        ";
                var PendingStaff = (await con.QueryAsync<Staff2>(query)).ToList();
                return new PrimeAdminResponse() { Success = true, Response = EnumResponse.Successful, Data = PendingStaff };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> ApproveAccountUpgrade(string Username, UpgradeAccountNo upgradeAccountNo, int actionid, string staffNameAndRole, string approveordeny, string shortdescription)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                string name = staffNameAndRole.Contains("_") ? staffNameAndRole.Split('_')[0] : staffNameAndRole; // this will be staff that will approves
                _logger.LogInformation("name " + name);
                var initiationStaff = (await con.QueryAsync<string>("select (select email from staff s where s.id=sf.initiationstaff) as email from staffaction sf where sf.id=@id", new { id = actionid })).FirstOrDefault();

                if (initiationStaff.Replace("@trustbancgroup.com", "").Trim().Equals(name.Trim(), StringComparison.CurrentCultureIgnoreCase))
                {
                    return new GenericResponse() { Success = true, Response = EnumResponse.YouareNottheOne };
                }

                var validateActionId = (await con.QueryAsync<int>("select id from staffaction where id=@id", new { id = actionid })).FirstOrDefault();
                if (validateActionId == 0)
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.NoAccountExist };
                }
                var stafftoapprove = (await con.QueryAsync<int>("select id from staff where email=@email", new { email = name + "@trustbancgroup.com" })).FirstOrDefault();
                var PendingAction = (await con.QueryAsync<PendingStaffActionOnProfile>("select st.id,(select c.actioname from checkeraction c where c.id=st.action) as action,st.staffidtoaction as userid from staffaction st where st.approvalstatus=false and st.id=@id", new { id = actionid })).FirstOrDefault();
                if (PendingAction == null)
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.ActionLikelyApprove };
                }
                if (approveordeny.Equals("approve", StringComparison.CurrentCultureIgnoreCase))
                {
                    if (PendingAction.action.ToLower() == "Initiateupgradeaccount")
                    {
                        await con.ExecuteAsync("update staffaction set approvalstatus=true,approveordeny=@approve,approvalstaff=@approvalstaff where id=@id", new { approve = approveordeny, id = actionid, approvalstaff = stafftoapprove });
                        var customeridtoaction = (await con.QueryAsync<int>("select staffidtoaction from staffaction where id=@id", new { id = actionid })).FirstOrDefault();
                        GenericResponse genericResponse = await this.UpgradeAUserAccount(con, Username, upgradeAccountNo);
                        if (!genericResponse.Success)
                        {
                            return genericResponse;
                        }
                        Users usr = (await con.QueryAsync<Users>("select * from users where id=@id", new { id = customeridtoaction })).FirstOrDefault();
                        CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, customeridtoaction);
                        SendMailObject sendMailObject = new SendMailObject();
                        sendMailObject.Subject = "TrustBanc Mobile Account Upgrade";
                        string email = $@"
                                <p>Dear {usr.Username} {usr.LastName}</p>,
                                <p>We have approved your request for account upgrade for TrustBanc Mobile Banking.</p> 
                                <p>You can now log in to your mobile banking app using this username. If you encounter any issues or need further assistance, please do not hesitate to contact us.</p>
                                <p>
                                For additional security:
                                •	If you did not request this recovery, please contact TrustBanc Support immediately at {_settings.SupportPhoneNumber}.
                                •	If you need to reset your password or make any changes to your account, visit the TrustBanc website or mobile app for further instructions.
                                </p>
                                <p>Thank you for banking with TrustBanc!</p>
                                <p>
                                Best regards,
                                TrustBanc Mobile Banking Support Team
                                {_settings.SupportPhoneNumber}
                                {_settings.SupportEmail}
                               </p>
                                ";
                        sendMailObject.Email = customerDataNotFromBvn.Email;
                        sendMailObject.Html = email;
                        Task.Run(() =>
                        {
                            _genServ.SendMail(sendMailObject);
                        });
                        return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
                    }
                }
                else if (approveordeny.Equals("deny", StringComparison.CurrentCultureIgnoreCase))
                {
                    if (PendingAction.action.ToLower().Equals("Initiateupgradeaccount".ToLower(), StringComparison.CurrentCultureIgnoreCase))
                    {
                        await con.ExecuteAsync("update staffaction set approvalstatus=true,approveordeny=@approve,approvalstaff=@approvalstaff where id=@id", new { approve = approveordeny, id = actionid, approvalstaff = stafftoapprove });
                        var customeridtoaction = (await con.QueryAsync<int>("select staffidtoaction from staffaction where id=@id", new { id = actionid })).FirstOrDefault();
                        CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)PendingAction.userid);
                        var usr = await _genServ.GetUserbyUsername(customerDataNotFromBvn.username, con);
                        if (customeridtoaction != 0) // send to the customer that it has been approved.
                        {
                            await con.ExecuteAsync("update accountupgradedviachannel set upgradedstatus=true,inititiated=true where userid=@id and accountnumber=@accountnumber", new { id = customeridtoaction, accountnumber = upgradeAccountNo.AccountNo });
                            new Thread(() =>
                            {
                                SendMailObject sendMailObject = new SendMailObject();
                                sendMailObject.Subject = "TrustBanc Account Upgrade Denial";
                                sendMailObject.Email = customerDataNotFromBvn.Email;
                                sendMailObject.Html = $"Dear ${usr.Firstname.ToUpper()},the requested account upgrade you required has been rejected/denied .<br/>Thank you for Banking with us.";
                                _genServ.SendMail(sendMailObject); // notify the user  
                            }).Start();
                        }
                        return new GenericResponse() { Success = true, Response = EnumResponse.Successful, Message = "rejected" };
                    }
                }
                return new GenericResponse() { Response = EnumResponse.NotSuccessful };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public Task<bool> DeleteAdvertFilesAsync(List<string> imageNames)
        {
            throw new NotImplementedException();
        }

        public async Task<GenericResponse> InitiateDeactivateCustomer(string UserName, string status, string StaffNameAndRole)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var usr = await _genServ.GetUserbyUsername(UserName, con);
                _logger.LogInformation("staffNameAndRole " + StaffNameAndRole);
                string name = StaffNameAndRole.Contains("_") ? StaffNameAndRole.Split('_')[0] : StaffNameAndRole; // this will be staff that will approves
                _logger.LogInformation("name " + name);
                // await con.ExecuteAsync("update users set Status=@status where username=@username", new { status = status, username = UserName });
                var staffid = (await con.QueryAsync<string>("select id from staff where email=@email", new { email = name + "@trustbancgroup.com" })).FirstOrDefault();
                if (string.IsNullOrEmpty(staffid))
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.InvalidStaffid };
                }
                return await _staffUserService.InitiateTask((int)usr.Id, status, StaffNameAndRole);
            }
            // return new GenericResponse() { Response = EnumResponse.Successful, Success = true };  
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse2> GetTransactionsReported()
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                //$@"insert into reporttransaction(username,comment,transactionref,createdOn,amount,dateoftransaction)
                var reportedtransaction = (await con.QueryAsync<ReportedTransaction>("select r.CheckedStatus, r.username,r.comment,r.transactionref,r.createdOn,r.amount,r.dateoftransaction,(select Firstname from users u where u.username=r.username) as firstname,(select Lastname from users u where u.username=r.username) as lastname from reporttransaction r where r.status=false")).ToList();
                return new GenericResponse2() { Success = true, data = reportedtransaction, Response = EnumResponse.Successful };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
        }

        public Task<GenericResponse2> FixReportedTransactions(string userName, string transactionRef)
        {
            throw new NotImplementedException();
        }

        public async Task<GenericResponse2> UpdateReportedTransactionStatus(string userName, string transactionRef, int Status)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                //$@"insert into reporttransaction(username,comment,transactionref,createdOn,amount,dateoftransaction)
                // var reportedtransaction = (await con.QueryAsync<ReportedTransaction>("select r.CheckedStatus, r.username,r.comment,r.transactionref,r.createdOn,r.amount,r.dateoftransaction,(select Firstname from users u where u.username=r.username) as firstname,(select Lastname from users u where u.username=r.username) as lastname from reporttransaction r where r.status=false")).ToList();
                if (Status==1)
                {
                    await con.ExecuteAsync("update reporttransaction set CheckedStatus=@CheckedStatus where username=@username and transactionref=@transactionref", new { CheckedStatus ="UNDER_iNVESTIGATION", username = userName, transactionref = transactionRef });
                }
                else if (Status==2)
                {
                    await con.ExecuteAsync("update reporttransaction set CheckedStatus=@CheckedStatus,status=TRUE WHERE username=@username and transactionref=@transactionref", new { CheckedStatus = "RESOLVED", username=userName, transactionref = transactionRef});
                }
                else
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.WrongAction };
                }
                return new GenericResponse2() { Success = true, Response = EnumResponse.Successful };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
        }
    }

}






















































































































































