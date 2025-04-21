using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface ISupportService
    {
        Task<GenericResponse> ProvideSupportService(Support contactSupport,string Username);
    }
}





















































