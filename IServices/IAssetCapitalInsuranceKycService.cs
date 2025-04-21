using Microsoft.AspNetCore.Http;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface IAssetCapitalInsuranceKycService
    {
        Task<GenericResponse2> AddUtilityBillOrIdCardOrSignature(string session, string userType, string userName, string documentType, IFormFile utilitybill);
        Task<GenericResponse2> GetCustomerDetailAfterRegistration(string userName, string session, string userType);
        Task<GenericResponse2> UpdateCustomerDetail(string userName, string userType,int ClientId, ExtendedSimplexCustomerUpdate extendedSimplexCustomerRegistration);
        Task<GenericResponse2> ValidateSessinAndUserTypeForKyc(string session, string username, string userType);
    }
}
