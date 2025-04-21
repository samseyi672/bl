using Retailbanking.Common.CustomObj;
using System.Data;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface IBeneficiary
    {
        Task<GenericBeneficiary> GetBeneficiary2(string ClientKey, GenericRequest Request, BeneficiaryType beneficiaryType, bool TopBeneficiary = false);
        Task<GenericBeneficiary> GetBeneficiary(string ClientKey, GenericRequest Request, BeneficiaryType beneficiaryType, bool TopBeneficiary = false);
        Task<GenericResponse> SaveBeneficiary(long UserId, BeneficiaryModel Request, IDbConnection con, BeneficiaryType beneficiaryType);
        Task<GenericResponse> UpdateBeneficiary(string ClientKey, GenericIdRequest Request);
    }
}