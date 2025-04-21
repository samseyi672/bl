using FirebaseAdmin.Messaging;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.Services
{
    public class NotificationService : INotification
    {
        private readonly ILogger<TransferServices> _logger;
        private readonly AppSettings _settings;
        private readonly IGeneric _genServ;
        private readonly DapperContext _context;

        public NotificationService(ILogger<TransferServices> logger, IGeneric genServ, DapperContext context, IOptions<AppSettings> options)
        {
            _logger = logger;
            _settings = options.Value;
            _genServ = genServ;
            _context = context;
        }

        public async Task<GenericResponse> SendNotificationAsync(string token, string title, string body)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var message = new Message()
                    {
                        Token = token,
                        Notification = new Notification
                        {
                            Title = title,
                            Body = body
                        }
                    };
                    string response = await FirebaseMessaging.DefaultInstance.SendAsync(message);
                    Console.WriteLine("notification response ......" + response);
                    _logger.LogInformation("notification response ......"+response);
                    return new BillPaymentResponse() { Success = true, Response = EnumResponse.Successful, Data = !string.IsNullOrEmpty(response) ? "message sent successfully " + response : "message not sent" };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }
    }
}





























