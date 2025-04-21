using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Retailbanking.Common.CustomObj;
using System.Data;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface IRegistration
    {
        Task<ValidateBvn> ValidateBvn(string Bvn, IDbConnection con);
        Task<ValidateBvn> CheckAssetCapitalInsuranceBvn(string Bvn, IDbConnection con);
        Task<ValidateBvn> ValidateAssetCapitalInsuranceBvn(string Bvn, IDbConnection con);
        Task<RegistrationResponse> StartRegistration(string ClientKey, RegistrationRequest Request);
        Task<string> MigratedExistinguser(string Session,string Otp,int ChannelId, MigratedCustomer migratedCustomer, IDbConnection con,string uniref);
        Task<RegistrationResponse> StartRegistrationNin(string ClientKey, RegistrationRequestNin Request);
        Task<RegistrationResponse> ResendOtpToPhoneNumber(string ClientKey, GenericRegRequest2 Request);
        Task<RegistrationResponse> ResendOtp(string ClientKey, GenericRegRequest Request);
        Task<ValidateOtpResponse> ValidateOtp(string ClientKey, ValidateOtp Request);
        Task<ValidateOtpResponse> CheckOtp(string ClientKey, ValidateOtp Request);
        Task<GenericResponse> CreateUsername(string ClientKey, SetRegristationCredential Request);
        Task<GenericResponse> ValidateUsername(string ClientKey, string Username);
        Task<GenericResponse> CreatePassword(string ClientKey, SavePasswordRequest Request);
        Task<GenericResponse> CreateTransPin(string ClientKey, SetRegristationCredential Request);
        Task<BvnSubDetails> ValidateDob(string ClientKey, SetRegristationCredential Request);
        Task<AccountOpeningResponse> OpenAccount(string ClientKey, GenericRegRequest Request);
        Task<GenericResponse> UploadProfilePicture(string ClientKey, ProfilePicture Request,IFormFile file);
        Task<GenericResponse> PhoneAndEmailAtFirstAttempt(string clientKey, PhoneAndEmail request2);
        Task<GenericResponse> ContactSupportForRegistration(string clientKey, ContactSupport request);
        Task<GenericResponse> CustomerReasonForNotReceivngOtp(string clientKey, CustomerReasonForNotReceivngOtp request);
        Task<RegistrationResponse> IsUserHasAccountNumber(string clientKey, CheckRegistrationRequest request);
        Task<ValidateOtpResponse> ValidateOtpForMigratedCustomer(string clientKey, ValidateOtpForMigratedCustomer request);
    }
}









