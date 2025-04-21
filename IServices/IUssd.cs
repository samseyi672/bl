using Retailbanking.Common.CustomObj;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface IUssd
    {
        Task<GenericLoginResponse> StartUssd(StartUssdRequest Request);
        Task<FetchAccounts> GetBalance(GenericUssdTransRequest Request);
        Task<GenericResponse> Transfer(UssdTransferRequest Request);
        Task<GenericNameEnquiryReponse> NameEnquiry(NameEnquiry Request);
    }
}
