using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using Org.BouncyCastle.Asn1.Ocsp;
using Quartz.Impl.Triggers;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using Retailbanking.Common.DbObj;
using Serilog;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;

namespace Retailbanking.BL.Services
{
    public class PlatformSuspenderService:IPlatformSuspenderService
    {
        private readonly ILogger<AuthenticationServices> _logger;
        private readonly IGeneric _genServ;
        private readonly DapperContext _context;
        private readonly AppSettings _settings;
        private readonly INotification _notification;
        public PlatformSuspenderService(INotification notification,IOptions<AppSettings> options,ILogger<AuthenticationServices> logger, IGeneric genServ, DapperContext context)
        {
            _logger = logger;
            _settings = options.Value;
            _genServ = genServ;
            _context = context;
            _notification = notification;
        }

        public async Task<GenericResponse2> getPlatformSuspensionStatus()
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {                    
                  var platformChecker = (await con.QueryAsync<PlatformChecker>("select * from platformlocker", new {})).FirstOrDefault();                
                  return new GenericResponse2() { Response = EnumResponse.Successful,Success=true,data= platformChecker };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Success = false, Response = EnumResponse.SystemError };
            }
        }
            public async Task<GenericResponse2> SetPlatformSuspensionForLogin(bool login, bool transaction, bool bills)
            {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    _logger.LogInformation("login " + login);
                    if (login)
                    {
                        await con.ExecuteAsync("update platformlocker set login=@login", new { login = login });
                    }
                    if(!login){
                        await con.ExecuteAsync("update platformlocker set login=@login", new { login = login });
                    }
                    //send push notification to customers 
                    string type = "System Notification";
                    string upmessage = _settings.UpSystemMaintenance;
                    string downmessage = _settings.DownSystemMaintenance;
                    new Thread(() => {
                       if(login) {
                           SendNotifcationToAllCustomers(con,type,upmessage);
                       }
                        if (!login)
                        {
                            SendNotifcationToAllCustomers(con, type, downmessage);
                        }
                    }).Start();
                    return new GenericResponse2() { Response = EnumResponse.Successful };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Success = false, Response = EnumResponse.SystemError };
            }
        }

        private void SendNotifcationToAllCustomers(IDbConnection con,string type,string message)
        {
                int pageSize = 1000;
                int currentPage = 0;
                IEnumerable<string> customers;
                do
                {
                    //var sql = "SELECT (select DeviceToken from mobiledevice m where mUserId=s.id) as token FROM users s ORDER BY s.CustomerId OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
                 var sql = @"
                            SELECT 
                                (SELECT m.DeviceToken 
                                 FROM mobiledevice m 
                                 WHERE m.mUserId = s.id) AS token 
                            FROM 
                                users s 
                            ORDER BY 
                                s.CustomerId 
                            LIMIT @Offset, @PageSize";
                customers = con.Query<string>(sql, new { Offset = currentPage * pageSize, PageSize = pageSize }, buffered: false);
                    // Process data in a separate thread
                    Task.Run(async () =>
                    {     
                        foreach (var token in customers)
                        {
                         _logger.LogInformation($"sending token {token}");
                        await _notification.SendNotificationAsync(token,type,message);                          
                        }
                    });
                    currentPage++;
                } while (customers.Any());
            }

        public Task<GenericResponse2> SetPlatformSuspensionStatus(PlatformChecker platformSetter)
        {
            throw new NotImplementedException();
        }

        public async Task<GenericResponse2> SetPlatformSuspensionForTransactionStatus(bool v1, bool transaction, bool v2)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    _logger.LogInformation("transaction " + transaction);
                    if (transaction)
                    {
                        await con.ExecuteAsync("update platformlocker set transaction=@transaction", new { transaction = transaction });
                    }
                    if (!transaction)
                    {
                        await con.ExecuteAsync("update platformlocker set transaction=@transaction", new { transaction = transaction });
                    }
                    //send push notification to customers 
                    string type = "System Notification";
                    string upmessage = _settings.UpSystemMaintenance;
                    string downmessage = _settings.DownSystemMaintenance;
                    new Thread(() => {
                        if (transaction)
                        {
                            SendNotifcationToAllCustomers(con, type, upmessage);
                        }
                        if (!transaction)
                        {
                            SendNotifcationToAllCustomers(con, type, downmessage);
                        }
                    }).Start();
                    return new GenericResponse2() { Response = EnumResponse.Successful };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Success = false, Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse2> SetPlatformSuspensionForBills(bool v1, bool v2, bool bills)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    _logger.LogInformation("bills " + bills);
                    if (bills)
                    {
                        await con.ExecuteAsync("update platformlocker set bills=@bills", new { bills = bills });
                    }
                    if (!bills)
                    {
                        await con.ExecuteAsync("update platformlocker set bills=@bills", new { bills = bills });
                    }
                    //send push notification to customers 
                    string type = "System Notification";
                    string upmessage = _settings.UpSystemMaintenance;
                    string downmessage = _settings.DownSystemMaintenance;
                    new Thread(() => {
                        if (bills)
                        {
                            SendNotifcationToAllCustomers(con, type, upmessage);
                        }
                        if (!bills)
                        {
                            SendNotifcationToAllCustomers(con, type, downmessage);
                        }
                    }).Start();
                    return new GenericResponse2() { Response = EnumResponse.Successful };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Success = false, Response = EnumResponse.SystemError };
            }
        }
    }
}



























































































