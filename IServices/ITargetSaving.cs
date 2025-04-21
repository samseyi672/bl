using Retailbanking.Common.CustomObj;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface ITargetSaving
    {
        Task<List<GetTargetSavingsCategory>> GetCategories();
        Task<CalculateRegularDebitResponse> CalculateRegularDebit(CalculateRegularDebitRequest Request);
        Task<GenericResponse> MakeTargetSavings(MakeTargetSavings Request, string DbConn, int ChannelId);
        Task<ViewTargetSavings> ViewTargetSavings(GenericRequest Request, string DbConn, int ChannelId);
        Task<GenericResponse> TopUpTargetSavings(TopUpTargetSavings Request, string DbConn, int ChannelId);
        Task<GenericResponse> StopTargetSavings(GenericIdRequest Request, string DbConn, int ChannelId);
    }
}
