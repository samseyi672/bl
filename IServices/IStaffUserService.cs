using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface IStaffUserService
    {
        Task<GenericResponse> LoginStaff(string UserName,string password);
        PrimeAdminResponse GetAllUsers(int page=0,int size=10);
        PrimeAdminResponse SearchStaffUsers(string Search);
        Task<GenericResponse> ProfileStaff(ProfileStaff profileStaff,string token=null);
        Task<GenericResponse> GetRoles();
        Task<GenericResponse> GetPermissions(string roleName);
        Task<GenericResponse> GetRolesAndPermissions(string username);
        Task<GenericResponse> GetProfiledStaff(int page=0,int size=10);
        Task<GenericResponse> GetProfiledStaffWithAuthorities(int page, int size);
        Task<GenericResponse> GetStaffPermissions(int staffid);
        Task<GenericResponse> InitiateTask(int customerid, string action, string StaffNameAndRole);
        Task<GenericResponse> InitiateTask(int customerid, string action, string StaffNameAndRole,string AccountNumber);
        Task<GenericResponse> InitiationDeleteActionOnStaffProfile(int staffid,string action,string StaffNameAndRole);
        Task<GenericResponse> GetPendingDeleteActionOnStaffProfile();
        Task<GenericResponse> DeleteStaffProfile(int staffid, string staffNameAndRole,string approveordeny);
        Task<GenericResponse> GetPendingProfiledStaff();
        Task<GenericResponse> ApproveStaffProfile(int actionid,string StaffNameAndRole,string approveordeny);
        Task<GenericResponse> GetPendingProfiledStaffCount();
        Task<GenericResponse> GetPendingActionOnStaffProfileCount();
        Task<GenericResponse> GetOtherPendingTaskKyc(string path);
        Task<GenericResponse> ApproveTask(int actionid, string staffNameAndRole, string approveordeny,string shortdescription,string AccountNumber=null);
        Task<GenericResponse> GetcustomerIndemnityPendingTask();
        Task<GenericResponse> GetAccountIndemnityPendingTask();
        Task<GenericResponse> GetPendingCustomerActivationOrDeactivationTask();
        Task<GenericResponse2> GetIndemnityRequest(int page, int size, string indemnitytype);
        Task<GenericResponse2> GetApprovedIndemnityRequest(int page, int size, string indemnitytype);
        Task<GenericResponse> ApproveKycTask(int actionid, string staffNameAndRole, string approveordeny, string shortdescription, string typeofdocument);
        Task<GenericResponse2> CountOfPendingKycInitiationAndApproval();
        Task<GenericResponse2> ApproveTransactionCappedLimit(string approveordeny, TransactionCappedLimit setTransactionCappedLimit,int actionid,string StaffNameAndRole);
        Task<GenericResponse2> InitiateTransactionCappedLimit(string action,TransactionCappedLimit setTransactionCappedLimit,string StaffNameAndRole);
        Task<GenericResponse2> GetPendingTransactionCappedLimit();
        Task<GenericResponse2> GetCappedTransactionLimit();
    }
}
