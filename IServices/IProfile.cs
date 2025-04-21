using Retailbanking.Common.CustomObj;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface IProfile
    {
        Task<GenericResponse> GetProfileStatus(string ClientKey, GenericRequest Request);
        Task<List<GenericIdStatus>> ViewProfileStatus(string ClientKey, GenericRequest Request);
        Task<List<DocumentType>> GetDocumentTypes();
        Task<GenericResponse> UploadDocument(string ClientKey, UploadDocument Request);
        Task<List<GenericValue>> GetOtherCredentials(string ClientKey, GenericRequest Request);
        Task<GenericResponse> UploadOtherCredentials(string ClientKey, GenericIdFileUpload Request);
        Task<GenericResponse> AddEmploymentInformation(string ClientKey, AddEmploymentInfo Request);
        Task<GenericResponse> AddNextOfKin(string ClientKey, AddNextKin Request);
    }
}
