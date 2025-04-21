using RestSharp;
using Retailbanking.Common.CustomObj;
using Retailbanking.Common.DbObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;


namespace Retailbanking.BL.IServices
{
    public interface IGeneric
    {

        Task<string> GetNinToken();
        void LogRequestResponse(string methodname, string Request, string Response);
        Task<object> CallServiceAsync<T>(AppSettings appSetting, Method method, string url, object requestobject, bool log = false, List<HeaderApi> header = null) where T : class;
        Task<T> CallServiceAsync<T>(Method method, string url, object requestobject, bool log = false, IDictionary<string, string> header = null) where T : class;
        Task<string> CallServiceAsyncToString(Method method, string url, object requestobject, bool log = false, IDictionary<string, string> header = null);
        Task<string> CallServiceAsyncForFileUploadToString(RestRequest request, Method method, string url, object requestobject, string filepath, bool log = false, IDictionary<string, string> header = null);
        string EncryptString(string str);
        string ConvertStringtoMD5(string strword);
        string RemoveSpecialCharacters(string str);
        string StringNumbersOnly(string str);
        DateTime ConvertDatetime(string dtstring);
        bool CheckPasswordCondition(string newpassword);
        GenericResponse SendMail(SendMailObject Request, System.IO.MemoryStream pdfStream=null);
        string GetSession();
        string GenerateOtp();
        string GeneratePin();
        string GenerateOtp2();
        string CreateRandomPassword(int length = 8);
        Task<bool> ValidateSessionForAssetCapitalInsurance(long ucid, string Session, int ChannelId, IDbConnection con, string UserType);
        Task<bool> ValidateSessionForAssetCapitalInsurance(string Session, string UserType, IDbConnection con);
        Task<bool> ValidateSession(long UserId, string Session, int ChannelId, IDbConnection con);
        Task<bool> ValidateSession(string Username, string Session, int ChannelId, IDbConnection con);
        Task<bool> CheckIfUserIsLoggedIn(string PhoneNumber, int ChannelId, IDbConnection con);
        Task SetUserSession(long UserId, string Session, int ChannelId, IDbConnection con);
        Task SetAssetCapitalInsuranceUserSession(long UserId, string UserType, string Session, int ChannelId, IDbConnection con);
        Task<Users> GetUserbyPhone(string PhoneNumber, IDbConnection con);
        Task<Users> GetUserbyUsername(string Username, IDbConnection con);
        Task<AssetCapitalInsuranceUsers> GetAssetCapitalInsuraceUserbyUsername(string Username, string UserType, IDbConnection con);
        Task<AssetCapitalInsuranceUsers> GetAssetCapitalInsuranceUserbyBvn(string bvn, string UserType, IDbConnection con);
        Task<Users> GetUserbyCustomerId(string CustomerId, IDbConnection con);
        Task<GenericResponse> ValidateNin(string username,string nin,IDbConnection con,string inputbvn=null);
        Task<GenericResponse> ValidateNin(string nin, IDbConnection con, string inputbvn);
        Task InsertOtp(OtpType otpType, long ObjId, string session, string otp, IDbConnection con);
        Task<SendSmsResponse> SendSMS(SendSmsRequest Request);
        Task<UserSession> ValidateSessionOtp2(OtpType otpType, string Session, IDbConnection con);
        string MaskEmail(string Email);
        string MaskPhone(string Phone);
        Task SendOtp(OtpType otpType, string Otp, string number, string email);
        Task SendOtp(OtpType otpType, string Otp, string number);
        Task SendOtp2(OtpType otpType, string Otp, string number, ISmsBLService smsBLService);
        Task SendOtp3(OtpType otpType, string Otp, string number, ISmsBLService smsBLService,string type,string email =null);
        public Task SendOtp4(OtpType otpType, string Otp, string number, ISmsBLService smsBLService, string type, string email = null);
        Task<string> DecryptStringAES(string cipherText, int ChannelId, string connectionstring);
        Task<string> EncryptStringAES(string plainText, int ChannelId, string connectionstring);
        Task InsertLogs(long UserId, string Session, string Device, string Gps, string Action, IDbConnection con);
        Task<string> GetUserCredential(CredentialType credentialType, long UserId, IDbConnection con);
        Task<string> GetAssetCapitalInsuranceUserCredential(CredentialType credentialType, long UserId, string UserType, IDbConnection con);
        Task<UsrCredential> GetUserCredentialForTrans(CredentialType credentialType, long UserId, IDbConnection con);
        Task SetUserCredential(CredentialType credentialType, long UserId, string Credential, IDbConnection con, bool PlainText);
        Task SetAssetCapitalInsuranceUserCredential(CredentialType credentialType, long UserId, string Credential, IDbConnection con, bool PlainText, string UserType);
        Task<MobileDevice> GetActiveMobileDevice(long UserId, IDbConnection con);
        Task<List<AssetCapitalInsuranceMobileDevice>> GetAssetCapitalInsuranceListOfActiveMobileDevice(long UserId, string UserType, IDbConnection con);
        Task<List<MobileDevice>> GetListOfActiveMobileDevice(long UserId, IDbConnection con);

        Task SetMobileDevice(string Username, string DeviceId, string DeviceName, int newStatus, IDbConnection con);
        Task SetMobileDevice(long UserId, string DeviceId, string DeviceName, int newStatus, IDbConnection con);
        Task SetAssetCapitalInsuranceMobileDevice(long UserId, string DeviceId, string DeviceName, int newStatus, IDbConnection con, string UserType);
        Task<List<GenericValue>> GetEntityType(EntityType entityType);
        Task<List<ClientCredentials>> GetClientCredentials();
        Task<List<AccessBankList>> GetBanks(bool log = false);
        Task<BalanceEnquiryResponse> GetAccountbyCustomerId(string CustomerId);
        Task<BalanceEnquiryResponse> GetBalanceEnquirybyAccountNumber(string AccountNumber);
        Task<GenericResponse> GetCustomerAllAccountBalance(string customerId);
        Task<GenericResponse> UpgradeAccountNo(UpgradeAccountNo upgradeAccountNo);
        Task<BalanceEnquiryResponse> GetAccountDetailsbyAccountNumber(string AccountNumber);
        Task<OtpSession> ValidateSessionOtp(OtpType otpType, string Session, IDbConnection con);
        Task InsertOtpForAssetCapitalInsurance(OtpType otpType, string UserType, long ucid, string session, string otp, IDbConnection con,long UserId);
        Task InsertOtpForAssetCapitalInsuranceOnRegistration(OtpType otpType, string UserType, string bvn, string session, string otp, IDbConnection con);
        Task InsertOtpForAssetCapitalInsuranceRegistration(OtpType otpType, string UserType, int registrationid, string session, string otp, IDbConnection con);
        Task<decimal> GetDailyLimit(long UserId, IDbConnection con, BeneficiaryType transType);
        Task<decimal> GetTotalSpent(long UserId, IDbConnection con, BeneficiaryType transType);
        Task<List<decimal>> SuggestedAmount(long UserId, IDbConnection con, BeneficiaryType transType);
        Task<UserLimit> GetUserLimits(long UserId, IDbConnection con, BeneficiaryType transType);
        Task<AirtimeBillsLimit> GetAirtimeBillsLimit(long UserId, IDbConnection con);
        Task<GetCustomerResponse> GetCustomer(string CustomerId);
        Task<GetCustomerInfobyCustomerId> GetCustomer2(string CustomerId);
        Task<FinedgeSearchBvn> GetCustomerbyAccountNo(string AccountNo);
        Task<ValidateAccountResponse> ValidateNumberOnly(string DestinationAccount, string DestinationBankCode);
        string GenerateHmac(string message, string secretKey, bool toBase64, HmacType hmacType = HmacType.Sha512);
        Task<FinedgeAccountProductName> GetAccountWithProductName(string source_Account);
        Task<string> CheckIfUserIdExistInTransfer(string username,IDbConnection connection);
        Task<string> GenerateUnitID(int id);
    }
    public enum HmacType
    {
        Md5,
        Sha1,
        Sha256,
        Sha384,
        Sha512
    }
}
