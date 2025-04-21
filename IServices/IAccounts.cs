using Microsoft.AspNetCore.Mvc;
using Retailbanking.Common.CustomObj;
using System.Net.Http;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface IAccounts
    {
        Task<GenericResponse> FetchTransactionsNotFromFinEdgeWithPagination(string ClientKey, TransactionHistoryRequest Request,int page,int size);
        Task<TransactionRequest> FetchTransactionsNotFromFinEdge(string ClientKey, TransactionHistoryRequest Request,int page, int size); 
        Task<FetchAccounts> FetchAccounts(string ClientKey, GenericRequest Request);
        Task<FetchTransactions2> FetchTransactions2(string ClientKey, TransactionHistoryRequest Request);
        Task<FetchTransactions> FetchTransactions(string ClientKey, TransactionHistoryRequest Request);
        Task<FileProxy> DownloadStatement(string ClientKey, TransactionHistoryRequest Request);
        Task<FileProxy> DownloadReceipt(string ClientKey, string Username, long TransId, int ChannelId);
        Task<TransactionRequest> DownloadReceipt(string ClientKey, string Username, string TransId, int ChannelId);
    }
}
