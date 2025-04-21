using Microsoft.Extensions.Caching.Memory;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;

namespace Retailbanking.BL.IServices
{
    public interface IUserCacheService
    {
        void StoreUserData(MigratedCustomer userData,int time, IMemoryCache _cache);
        MigratedCustomer GetUserData(string phoneNumber, IMemoryCache _cache);
        void ClearUserData(string phoneNumber, IMemoryCache _cache);
    }

}
