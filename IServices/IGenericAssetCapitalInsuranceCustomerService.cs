
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Retailbanking.Common.CustomObj;
using Retailbanking.Common.DbObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface IGenericAssetCapitalInsuranceCustomerService
    {
        Task<AssetCapitalInsuranceRegistrationOtpSession> ValidateRegistrationSessionOtp(OtpType otpType, string Session, string userType, IDbConnection con);
        Task<GenericResponse> CreatePassword(string clientKey, SavePasswordRequest request, string userType);
        Task<GenericResponse> CreateTransPin(string clientKey, SetRegristationCredential request, string userType);
        Task<GenericResponse> CreateUsername(string clientKey, SetRegristationCredential request, string userType);
        Task<string> GetToken();
        Task<LoginResponse> LoginUser(string clientKey, LoginRequest request,string UserType, bool LoginWithFingerPrint = false);
        Task<string> SetAPIToken();

       // Task<OtpSession> ValidateSessionOtp(OtpType otpType, string Session, string userType, IDbConnection con);
        Task<GenericResponse> ValidateUsername(string clientKey, string Username, string UserType);
        Task<GenericResponse2> CreateSimplexKycResponse([FromForm] IFormFile file, [FromForm] SimplexKycForm simplexKycForm,string UserType,string Session,int ChannelId);
        Task<GenericResponse2> GetClientPicture(ClientPictureRequest clientPictureRequest,string UserType,string Session, int ChannelId);
        Task<GenericResponse2> GetClientTitles(string session, string UserType);
        Task<GenericResponse2> GetEmployers(string session,string UserType);
        Task<GenericResponse2> GetFullDetails(int accountCode, string session,string UserType);
        GenericResponse ValidateUserType(string UserType);
        Task<RegistrationResponse> StartRegistration(AssetCapitalInsuranceRegistrationRequest request);
        Task<GenericResponse2> OpenAccount(string userType, GenericRegRequest request,AssetCapitalInsuranceRegistration assetCapitalInsuranceRegistration);
        Task<GenericResponse2> GetRegistrationDataBeforeOpenAccount(string bvn,string UserType,string Session);
        Task<GenericResponse2> GetDataAtRegistrationWithReference(int RegId,string userType, string session, string requestReference,int ChannelId);
        Task<GenericResponse> ValidateDob(string v, SetRegristationCredential request, string userType);
        Task<ValidateOtpResponse> ValidateOtp(string v, AssetCapitalInsuranceValidateOtp request, string userType);
        Task<GenericResponse> ContactSupportForRegistration(string v, ContactSupport request, string userType);
        Task<GenericResponse> CustomerReasonForNotReceivngOtp(string v, CustomerReasonForNotReceivngOtp request, string userType);
        Task<GenericResponse> ResendOtpToPhoneNumber(string v, GenericRegRequest2 request, string userType);
        Task<GenericResponse> ClearRegistrationByBvn(string bvn, string userType);
        Task<GenericResponse2> MailAfterLoginAndSuccessfulAccountFetch(string username, string session, int channelId, string deviceName,string UserType);
        Task<AssetCapitalInsuranceUsers> GetAssetCapitalInsuranceCustomerbyPhoneNumber(string phoneNumberOrEmail, string UserType, IDbConnection con);
        Task<GenericResponse> StartRetrival(string v, AssetCapitalInsuranceResetObj request, string userType);
        Task<GenericResponse> ResetPassword(string v, AssetCapitalInsuranceResetPassword request, string userType);
        Task<GenericResponse> ValidateOtherDeviceForOnBoarding(string v, AssetCapitalInsurancePhoneAndAccount request, string userType);
        Task<GenericResponse> ValidateOtpToOnBoardOtherDevices(string v, DeviceOtpValidator request, string userType);
        Task<ValidateOtpResponse> SendTypeOtp(OtpType registration, string username, string userType, string typeOtp);
        Task<GenericResponse2> GetFullDetailsByClientReference(string userName, string session, string userType);
        Task<GenericResponse2> GetRelationship(string session, string userType);
        Task<GenericResponse2> ClientCountries(string userType, string session);
        Task<GenericResponse> ValidateOtpForOtherPurposes(string v, AssetCapitalInsuranceValidateOtpRetrival request, string userType);
        Task<GenericResponse2> GetInhouseBanks(string userName, string session, string userType);
        Task<GenericResponse2> GetOccupations(string userName, string session, string userType);
        Task<GenericResponse2> GetReligious(string userName, string session, string userType);
        Task<GenericResponse2> GetWalletBalance(string userName,string Session, string userType, string currency, int clientid);
        Task<GenericResponse2> ClientStates(string session, string userType);
        Task<GenericResponse2> AddBankOrUpdateForClient(SimplexClientBankDetailsDto simplexClientBankDetails);
        Task<ValidateAccountResponse> BankAccountNameEnquiry(string UserName, string session, string userType, string accountNumber,string BankCode);
        Task<GenericResponse2> GetAllBanks(string userName, string session, string userType);
        Task<BankList> GetPossibleBanks(string AccountNumber, string session, string userType);
        Task<GenericResponse2> ClientLga(string state, string session, string userType);
        Task<GenericResponse2> AddOrUpdateClientMinor(SimplexClientMinorDto simplexClientMinor);
        Task<GenericResponse2> AddOrUpdateClientNextOfKin(SimplexClientNextOfKinDto simplexClientNextOfKin);
        Task<GenericResponse2> CompareNinAndBvnForValidation(string clientKey, string session, string username, int channelId, string nin,string UserType,string inputbvn=null);
        Task<GenericResponse2> GetSourcesOffund(string session, string userType);
        Task<GenericResponse> ValidatePin(PinValidator pinValidator, string userType);
        Task<GenericResponse2> RemoveBankOrUpdateForClient(SimplexClientBankDetailsRemovalDto simplexClientBankDetailsRemovalDto,string UserType);
    }
}














