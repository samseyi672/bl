using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Retailbanking.Common.CustomObj;
using System.Data;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface IAuthentication
    {
        Task<LoginResponse> LoginUser(string ClientKey, LoginRequest Request, bool LoginWithFingerPrint = false);
        // Task<LoginResponse> LoginUserWithFingerPrint(string ClientKey, LoginRequestFinger Request);
      //  Task<RegistrationResponse> UnlockDevice(string ClientKey, UnlockDevice Request);
       // Task<RegistrationResponse> UnlockProfile(string ClientKey, ResetObj Request);
      //  Task<RegistrationResponse> ForgotPassword(string ClientKey, ResetObj Request);
        Task<GenericResponse> ResetPassword(string ClientKey, ResetPassword Request);
        Task<GenericResponse> ValidateOtp(string ClientKey, ValidateOtpRetrival Request);
       // Task<GenericResponse2> MigrateCustomerBeneficiaresToPrime2(long UserId, string Username);
        Task<GenericResponse2> MigrateCustomerBeneficiaresToPrime2(long UserId,bool migrateduser,bool isbeneficiarymigrated,string Username);

        int UpdateDeviceLoginStatus(string ClientKey, string Username);
        Task<GenericResponse> ValidateOtherDeviceForOnBoarding(string ClientKey,PhoneAndAccount Request);

        Task<GenericResponse> ValidateOtpToOnBoardOtherDevices(string ClientKey,DeviceOtpValidator deviceOtpValidator);

        Task<GenericResponse> CheckOtp(string ClientKey, ValidateOtpRetrival Request);
        Task<RetrivalResponse> StartRetrival(string ClientKey, ResetObj2 Request);
      Task<GenericResponse2> UploadProfilePicture(string clientKey, Picture request, IFormFile file);
        Task<GenericResponse> ValidatePIn(string clientKey, int ChannelId,string username, string userPin, string session);
        Task<GenericResponse> SetEmploymentInfo(string clientKey,int channelId, string username,EmploymentInfo employmentInfo);
        Task<GenericResponse> SetNextOfKinInfo(string clientKey, int channelId,NextOfKinInfo nextOfKinInfo);
        Task<GenericResponse> AddCustomerIdCard(string clientKey,CustomerIdCard customerIdCard,IFormFile IdCard);
        Task<GenericResponse> SetCustomerDocument(string clientKey, CustomerDocuments customerDocuments,IFormFile passport,IFormFile signature, IFormFile utilityBill);
        Task<GenericResponse> Kyc(string clientKey, string session, string username, int ChannelId);
        Task<GenericResponse> CompareNinAndBvnForValidation(string clientKey, string session, string username, int channelId, string nin,string inputbvn=null);
        Task<GenericResponse> SetAdvertImageOnMobile(string clientKey, AdvertImageOnMobile request);
        Task<GenericResponse> SetUtilityBill(string clientKey, CustomerDocuments customerDocuments, IFormFile utilitybill);
        Task<GenericResponse> SetSignatureAndPassport(string clientKey, CustomerDocuments customerDocuments, IFormFile passport, IFormFile signature);
        Task<GenericResponse> AddPassport(string clientKey, CustomerDocuments customerDocuments, IFormFile passport);
        Task<GenericResponse> AddSignature(string clientKey, CustomerDocuments customerDocuments, IFormFile signature);
        Task<GenericResponse> KycPassportAcceptance(string clientKey, KycPassport kycStatus);
        Task<GenericResponse> KycUtlityBillAcceptance(string clientKey, KycUtlityBill kycStatus);
        Task<GenericResponse> KycSignatureAcceptance(string clientKey, KycSignature kycStatus);
        Task<GenericResponse> KycIdCardAcceptance(string clientKey, KycidCard kycStatus);
        Task<GenericResponse> KycStatus(string username);
        Task<GenericResponse2> GetCustomerAccountLimit(string clientKey, string username,string Session,int ChannelId);
        Task<GenericResponse2> BackOfficeIndemnityFormUploadForCustomer(string clientKey, BackofficeIndemnityForm customerDocuments, IFormFile indemnityform);
        Task<GenericResponse2> UploadIndemnityForm(string clientKey, IndemnityForm customerDocuments,IFormFile indemnityform);
  //      Task<GenericResponse2> CheckCustomerDailyandSingleTransactionLimit(string clientKey, string username, string session, int channelId);
        //Task<GenericResponse2> CustomerTransactionLimit(string clientKey, string username, string session, int channelId);
        Task<GenericResponse2> CustomerTransactionLimit(string clientKey, CustomerAccountTransactionLimit customerAccountLimit);
        Task<GenericResponse> KycAcceptance(Kyc kyc, string type,string StaffNameAndRole);
        Task<GenericResponse> InitiateTask(int customerid, string action, string StaffNameAndRole);
        Task<ValidateOtpResponse> SendConfirmationOtp(OtpType registration,string PhoneNumber,string Username, string type);
        Task<ValidateOtpResponse> SendTypeOtp(OtpType registration, string username, string typeOtp);
        Task<GenericResponse> ValidateOtp2(string clientKey, ValidateOtpRetrival request);
        Task<GenericResponse2> UploadIndemnityFormWithoutForm(IndemnityForm customerDocuments);
        Task<GenericResponse2> MailAfterLoginAndSuccessfulAccountFetch(string username, string Session, int ChannelId,string DeviceName);
    }
}
    