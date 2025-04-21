using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface IOfficeTransactionLoader
    {
        Task<GenericResponse> FetchTransactions(string TransactionType,int page,int size);
        Task<GenericResponse> FetchTransactionByDate(string transtype,string StartDate,string EndDate,int page , int size);
        Task<GenericResponse> SearchTransactionsBySourceAccount(string SourceAccount);
        Task<GenericResponse2> SearchTransactionsByReference(string reference, string type,string UserName,string SourceAccount);
        GenericResponse SendCustomDataToFilter();
    }
}
