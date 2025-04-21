using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface IPinManagementService
    {
        Task<GenericResponse2> GetListOfInitiatedPinApproval(int page, int size);
        Task<GenericResponse2> InitiatePinApproval(string action,string username,string StStaffNameAndRole);
        Task<GenericResponse2> PinApproval(PinApproval pinApproval);
    }
}
