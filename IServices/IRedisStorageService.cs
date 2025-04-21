using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface IRedisStorageService
    {
        Task SetCacheDataAsync(string key, object value);
        Task<T> GetCacheDataAsync<T>(string key);
        Task<string> GetCustomerAsync(string key);
        Task SetTokenAndRefreshTokenCacheDataAsync(string key, object value, int time);
        Task RemoveCustomerAsync(string key);
    }
}
