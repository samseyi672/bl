using Microsoft.AspNetCore.Http;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface ITransactionReportService
    {
        Task<GenericResponse> getTransactionWithIssues(int page , int size);
        Task<GenericResponse> SearchTransactionWithIssues(string startdate,string enddate);
        Task<GenericResponse> ReportTransactionAsFixed(string username,bool status, string transactionref);
        Task<GenericResponse> SearchTransactionWithReference(string transactionref);
        Task<GenericResponse> TotalTransactionByTransfer();
        Task<GenericResponse> TotalTransactionByBill();
        Task<GenericResponse> TotalCustomer();
        Task<GenericResponse> TotalActiveOrInActiveCustomer(string status);
        Task<GenericResponse> TotalTransactionByTransferAndBill();
        Task<GenericResponse> TotalActiveAndInActiveCustomer();
    }
}
