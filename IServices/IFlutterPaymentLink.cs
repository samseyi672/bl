using Microsoft.AspNetCore.Http;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface IFlutterPaymentLink
    {
        Task<GenericResponse2> GetPaymentLink(string AppKey,string AppType, PaymentLinkRequestDto paymentLinkRequestDto,string Session);
        Task<GenericResponse2> GetPaymentResponseAfterTransaction(string appKey, GetPaymentResponseAfterTransaction getPaymentResponseAfterTransaction);
        Task<GenericResponse2> VerifyTransaction(string AppKey, string AppType,string TransactionId);
        Task<GenericResponse2> SendExternalPaymentNotification(string session, string userName, string userType,decimal amount,string PayentReference,string BankName,string AccountNumber);
        Task<GenericResponse2> FundWalletAfterTransaction(string appKey, FundWalletAfterTransaction fundWalletAfterTransaction);
    }
}
