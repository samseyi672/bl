using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface INotification
    {
        Task<GenericResponse> SendNotificationAsync(string token, string title, string body);
    }
}
