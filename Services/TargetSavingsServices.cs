using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using Dapper;
using System.Linq;
using System.Data;
using System;
using System.Threading.Tasks;
using RestSharp;
using MySql.Data.MySqlClient;
using Retailbanking.Common.DbObj;
using System.Collections.Generic;

namespace Retailbanking.BL.Services
{
    public class TargetSavingsServices : ITargetSaving
    {

        private readonly ILogger<TargetSavingsServices> _logger;
        private readonly AppSettings _settings;
        private readonly IGeneric _genServ;
        private readonly IMemoryCache _cache;

        public TargetSavingsServices(ILogger<TargetSavingsServices> logger, IOptions<AppSettings> options, IGeneric genServ, IMemoryCache memoryCache)
        {
            _logger = logger;
            _settings = options.Value;
            _genServ = genServ;
            _cache = memoryCache;
        }

        public async Task<CalculateRegularDebitResponse> CalculateRegularDebit(CalculateRegularDebitRequest Request) => await _genServ.CallServiceAsync<CalculateRegularDebitResponse>(Method.POST, $"{_settings.FinedgeUrl}api/TargetSaving/CalculateRegularDebit", Request, true);

        public async Task<List<GetTargetSavingsCategory>> GetCategories()
        {
            try
            {
                List<GetTargetSavingsCategory> myData = new List<GetTargetSavingsCategory>();
                if (!_cache.TryGetValue(CacheKeys.TsCategory, out myData))
                {
                    // Key not in cache, so get data.
                    var myData1 = await _genServ.CallServiceAsync<GetTsCategory>(Method.GET, $"{_settings.FinedgeUrl}api/TargetSaving/GetCategory", null, true);
                    var myData2 = new List<GetTargetSavingsCategory>();
                    if (myData1 != null && myData1.Success)
                        foreach (var n in myData1.TsCategories)
                            myData2.Add(new GetTargetSavingsCategory()
                            {
                                CategoryId = n.Id,
                                CategoryName = n.Name,
                                MinMonth = n.MinMonth,
                                Rate = n.Rate
                            });

                    myData = myData2;
                    // Set cache options.
                    var cacheEntryOptions = new MemoryCacheEntryOptions()
                        // Keep in cache for this time, reset time if accessed.
                        .SetSlidingExpiration(TimeSpan.FromHours(1));

                    // Save data in cache.
                    _cache.Set(CacheKeys.TsCategory, myData2, cacheEntryOptions);

                }
                return myData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new List<GetTargetSavingsCategory>();
            }
        }

        public async Task<GenericResponse> MakeTargetSavings(MakeTargetSavings Request, string DbConn, int ChannelId)
        {
            try
            {
                using (IDbConnection con = new MySqlConnection(DbConn))
                {
                    var validateSession = await _genServ.ValidateSession(Request.Username, Request.Session, ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                }

                var req = new CreateTs()
                {
                    ClientKey = _settings.FinedgeKey,
                    CustomName = Request.CustomName,
                    Deduction = Request.Deduction,
                    EndDate = Request.EndDate,
                    Frequency = Request.Frequency,
                    SourceAccount = Request.SourceAccount,
                    StartDate = Request.StartDate,
                    TargetAmount = Request.TargetAmount,
                    CategoryId = Request.CategoryId
                };
                return await _genServ.CallServiceAsync<GenericResponse>(Method.POST, $"{_settings.FinedgeUrl}api/TargetSaving/CreateTargetSaving", req, true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> StopTargetSavings(GenericIdRequest Request, string DbConn, int ChannelId)
        {
            try
            {
                using (IDbConnection con = new MySqlConnection(DbConn))
                {
                    var validateSession = await _genServ.ValidateSession(Request.Username, Request.Session, ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                }

                var req = new StopTargetSavings()
                {
                    ClientKey = _settings.FinedgeKey,
                    TargetSavingsId = Request.Id
                };
                return await _genServ.CallServiceAsync<GenericResponse>(Method.POST, $"{_settings.FinedgeUrl}api/TargetSaving/StopTargetSaving", req, true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> TopUpTargetSavings(TopUpTargetSavings Request, string DbConn, int ChannelId)
        {
            try
            {
                using (IDbConnection con = new MySqlConnection(DbConn))
                {
                    var validateSession = await _genServ.ValidateSession(Request.Username, Request.Session, ChannelId, con);
                    if (!validateSession)
                        return new GenericResponse() { Response = EnumResponse.InvalidSession };
                }

                var req = new TopUpTargetSavingMifos()
                {
                    ClientKey = _settings.FinedgeKey,
                    TargetSavingsId = Request.TargetSavingsId,
                    SourceAccount = Request.SourceAccount,
                    Amount = Request.Amount
                };
                return await _genServ.CallServiceAsync<GenericResponse>(Method.POST, $"{_settings.FinedgeUrl}api/TargetSaving/TopUpTargetSaving", req, true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<ViewTargetSavings> ViewTargetSavings(GenericRequest Request, string DbConn, int ChannelId)
        {
            try
            {
                using (IDbConnection con = new MySqlConnection(DbConn))
                {
                    var validateSession = await _genServ.ValidateSession(Request.Username, Request.Session, ChannelId, con);
                    if (!validateSession)
                        return new ViewTargetSavings() { Response = EnumResponse.InvalidSession };

                    var getUser = await _genServ.GetUserbyUsername(Request.Username, con);

                    var req = new ViewTargetSavingsRequest()
                    {
                        ClientKey = _settings.FinedgeKey,
                    };
                    return await _genServ.CallServiceAsync<ViewTargetSavings>(Method.POST, $"{_settings.FinedgeUrl}api/TargetSaving/ViewTargetSaving", req, true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new ViewTargetSavings() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }
    }
}
