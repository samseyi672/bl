using Google.Api.Gax;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;

namespace Retailbanking.BL.Services
{
    public class UserCacheService : IUserCacheService
    {
        //private readonly IMemoryCache _cache;

        public UserCacheService()
        {
           // _cache = cache;
        }

        public void ClearUserData(string phoneNumber, IMemoryCache _cache)
        {
            string cacheKey = $"user_{phoneNumber}";
            _cache.Remove(cacheKey); // Remove the specific user data
        }

        public MigratedCustomer GetUserData(string phoneNumber, IMemoryCache _cache)
        {
            string cacheKey = $"user_{phoneNumber}";
            _cache.TryGetValue(cacheKey, out string userData);
            Console.WriteLine(userData != null ? $"Retrieved data for key: {cacheKey}" : $"No data found for key: {cacheKey}");
            if (userData==null)
            {
                return null;
            }
            return (MigratedCustomer)JsonConvert.DeserializeObject(userData);
        }

        public void StoreUserData(MigratedCustomer userData, int time, IMemoryCache _cache)
        {
           // string cacheKey = $"user_{userData.PhoneNumber}";
           // var cacheEntryOptions = new MemoryCacheEntryOptions().SetSlidingExpiration(time);
           // _cache.Set(cacheKey, userData, TimeSpan.FromSeconds(time)); // Set expiration for the cached data    
            string cacheKey = $"user_{userData.PhoneNumber}";
            _cache.Set(cacheKey,JsonConvert.SerializeObject(userData), TimeSpan.FromSeconds(time)); // Set expiration for the cached data    
            Console.WriteLine($"Stored in cache: {cacheKey} - {userData.PhoneNumber}");
        }
    }

}
