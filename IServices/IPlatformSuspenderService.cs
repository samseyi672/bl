using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface IPlatformSuspenderService
    {
        Task<GenericResponse2> SetPlatformSuspensionForLogin(bool login= false,bool transaction=false,bool bills=false);
        Task<GenericResponse2> getPlatformSuspensionStatus();
        Task<GenericResponse2> SetPlatformSuspensionStatus(PlatformChecker platformSetter);
        Task<GenericResponse2> SetPlatformSuspensionForTransactionStatus(bool v1, bool transaction, bool v2);
        Task<GenericResponse2> SetPlatformSuspensionForBills(bool v1, bool v2, bool bills);
    }
}
