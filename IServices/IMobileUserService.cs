using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Retailbanking.Common.CustomObj;
using Retailbanking.Common.DbObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface IMobileUserService
    {
        Task<GenericResponse> GetPrimeUsers(int page,int size,string host);
        Task<GenericResponse> SendMessageToUser(string clientKey);
        Task<GenericResponse> GetDetailsOfAUser(int userId);
        Task<GenericResponse> DeactivateAUser(int userId);
        Task<GenericResponse> ActivateAUser(int userId);
        Task<GenericResponse> GetAUserTransactionsOnMobile(int userId,string date1,string date2);
        Task<GenericResponse> CheckAUserKyc(string UserName);
        Task<GenericResponse> InitiateActivateACustomer(string UserName,string status,string StaffNameAndRole);
        Task<GenericResponse> UpgradeAUserAccount(IDbConnection connection,string UserName,UpgradeAccountNo upgradeAccountNo);
        Task<GenericResponse> ApproveAccountUpgrade(string Username,UpgradeAccountNo upgradeAccountNo, int actionid, string staffNameAndRole, string approveordeny, string shortdescription);
        Task<GenericResponse> GetCustomerAllAccountBalance(string Username);
        Task<GenericResponse> GetUserKycEmploymentInfo(string userName);
        Task<GenericResponse> GetKycNextOfKinInfo(string userName);
        Task<GenericResponse> GetUserKycDocument(string userName,string path);
        Task<GenericResponse> GetUserAccountDetailsWithKycLevel(string userName);
        Task<GenericResponse> GetAUserMobileTransactionHistory(int page, int size, string username, string StartDate, string EndDate);
        Task<GenericResponse> SearchUserByName(int page, int size, string host, string searchTerm);
        Task<GenericResponse> SearchUserByBvn(int page , int size ,string host, string bvnSearch);
        Task<GenericResponse> AddAdvertImageOrPictures(bool active, string userName, IFormFile image);
        Task<GenericResponse> GetAdvertImagesorPromoImage(string baseurl,int page,int size);
        Task<GenericResponse> EditAdvertImagesorPromoImage(List<ImageUpdate> ids);
        Task<GenericResponse2> GetCustomerIntrabankTransactionusStatus(string transRef, string transId);
      //  Task<GenericResponse2> GetCustomerIntrabankTransactionusStatus(string transRef, string v);
        Task<GenericResponse2> GetCustomerInterBankTransactionusStatus(string transRef, string v);
        Task<GenericResponse2> GetCustomerInterBankTransactionusStatusForOutward(string transRef, string transId);
        Task<GenericResponse2> FetchUser(string userName,string host);
        Task<GenericResponse> DeleteAdvertImagesorPromoImage(string host,List<int> ids);
        Task<bool> DeleteAdvertFilesAsync(List<string> imageNames);
        Task<GenericResponse2> GetActiveAndInActiveCustomer();
        Task<GenericResponse2> GetMobileActiveAndInActiveCustomer();
        Task<GenericResponse2> GetPendingKycCount();
        Task<GenericResponse2> GetPendingKyc();
        Task<GenericResponse2> SetPendingKycAsTreated(string username);
        Task<GenericResponse2> GetPendingAccountLimitUpdate();
        Task<GenericResponse2> GetPendingCustomerAccountLimitUpdate();
        Task<GenericResponse2> SetPendingCustomerAccountLimitUpdateAsTreated(string username);
        Task<GenericResponse> InitiateCustomerIndmnityLimitAcceptance(string username, string status,string StaffNameAndRole);
        Task<GenericResponse2> GetCustomerAccountLimitUpdate(string username,string baseurl);
        Task<GenericResponse> GetCountOfTransactionFortheMonth();
        Task<GenericResponse> InitiateAccountIndemnityLimitAcceptance(string username, string status, string staffNameAndRole, string accountNumber);
        Task<GenericResponse2> UpdatePhoneNumberAndEmail(string userName, string phoneNumber, string email);
        Task<GenericResponse> Initiateupgradeaccount(string userName,string AccountTier, string AccountNumber, string actionName, string staffNameAndRole);
        Task<GenericResponse> GetPendingListOfAccountTobeUpgraded();
        Task<GenericResponse> InitiateDeactivateCustomer(string userName, string action, string staffNameAndRole);
        Task<GenericResponse2> GetTransactionsReported();
        Task<GenericResponse2> FixReportedTransactions(string userName, string transactionRef);
        Task<GenericResponse2> UpdateReportedTransactionStatus(string userName, string transactionRef,int Status);
    }
}












































































































