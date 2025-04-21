using Retailbanking.Common.CustomObj;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface IAirtimeBills
    {
        Task<AirtimeCodes> GetAirtimeCodes();
        Task<AirtimeBillsLimit> GetAirtimeBillsLimit(string PhoneNumber, int ChannelId);
        Task<TransResponse> MakeAirtime(AirtimeRequest Request);
        Task<TransResponse> MakeBillPayment(BillsRequest Request);
        Task<FetchDetailsResponse> ValidateReference(FetchDetails Request);
        Task<List<GetList>> GetCategories();
        Task<List<GetList>> GetBillers(long CategoryId);
        Task<List<GetProducts>> GetBillerProducts(long BillerId);

    }
}
