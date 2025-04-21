using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface ISmsBLService
    {
        Task<GenericResponse> SendSmsToCustomer(string subject, string AccountNumber, string Amount, string phoneNumber, string narration, string alertType);
        Task<GenericResponse> SendSmsNotificationToCustomer(string subject, string PhoneNumber, string narration, string alertType);
        Task<GenericResponse> SendSmsNotificationToCustomer(string subject, string PhoneNumber, string narration, string alertType, string smsurl);
        Task<GenericResponse> SendSmsToCustomer(string subject, string AccountNumber, string Amount, string phoneNumber, string narration, string alertType, string charge);
        Task<GenericResponse> SendSmsToCustomerForCredit(string subject, string AccountNumber, string Amount, string phoneNumber, string alertType, string narration);
      }
}
