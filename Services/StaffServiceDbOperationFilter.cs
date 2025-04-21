using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Retailbanking.BL.Services
{
    public class StaffServiceDbOperationFilter : IStaffServiceDbOperationFilter
    {

        private readonly FolderPaths _folderPaths;
        private readonly ILogger<TransferServices> _logger;
        private readonly AppSettings _settings;
        private readonly IGeneric _genServ;
        private readonly DapperContext _context;
        private readonly IStaffServiceDbOperationFilter _staffService;

        public StaffServiceDbOperationFilter(IOptions<FolderPaths> folderPaths, ILogger<TransferServices> logger, IOptions<AppSettings> options, IGeneric genServ, DapperContext context)
        {
            _logger = logger;
            _settings = options.Value;
            _genServ = genServ;
            _context = context;
            _folderPaths = folderPaths.Value;
        }

        public async Task<List<string>> GetAuthorizerEmailsAsync(CustomerDataAtInitiationAndApproval customerDataAtInitiationAndApproval)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                if (customerDataAtInitiationAndApproval.role.Equals("Authorizer", StringComparison.CurrentCultureIgnoreCase))
                {
                    var emails = await con.QueryAsync<string>(
                         @"SELECT (SELECT email FROM staff s WHERE s.id = str.staffid) AS email 
                  FROM omnichannel.staffrole str 
                  WHERE str.staffrole = 2"
                       );
                    _logger.LogInformation("email " + emails.Any());
                    SendMailObject sendMailObject = new SendMailObject();
                    sendMailObject.Subject = "PrimeApp Office Notification";
                    sendMailObject.Html = $@"
                                            <p>Dear Corresponding Admin Staffs</p>,
                                            <p>Kindly know that an Initiation of {customerDataAtInitiationAndApproval.ApprovalName} has been approved for a customer {customerDataAtInitiationAndApproval.CustomerName} today on {DateTime.Now}</p>
                                            <p>Let all be notified</p>.
                                            <p>Best Regards.</p>
                                            ";
                    new Thread(() =>
                    {
                        foreach (var item in emails)
                        {
                            sendMailObject.Email = item;
                            _genServ.SendMail(sendMailObject);
                            _logger.LogInformation("mail sent to authorizer");
                        }
                    }).Start();
                    return emails.ToList();
                } else if (customerDataAtInitiationAndApproval.role.Equals("admin", StringComparison.CurrentCultureIgnoreCase)) {
                    var emails = await con.QueryAsync<string>(
                        @"SELECT (SELECT email FROM staff s WHERE s.id = str.staffid) AS email 
                          FROM omnichannel.staffrole str 
                          WHERE str.staffrole = 4"
                      );
                   _logger.LogInformation("admin email " + emails.Any());
                    SendMailObject sendMailObject = new SendMailObject();
                    sendMailObject.Subject = "PrimeApp Office Notification";
                    sendMailObject.Html = $@"
                                            Dear Corresponding Admin Staffs,
                                            Kindly know an Initiation {customerDataAtInitiationAndApproval.ApprovalName} has been approved for a customer {customerDataAtInitiationAndApproval.CustomerName} today {DateTime.Now}
                                            Let all be notified .
                                            Regards.
                                            ";
                    new Thread(() =>
                    {
                        foreach (var item in emails)
                        {
                            sendMailObject.Email = item;
                            _genServ.SendMail(sendMailObject);
                            _logger.LogInformation("mail sent to admin");
                        }
                    }).Start();
                 return emails.ToList();
                }
                else
                {
                    _logger.LogInformation("no role was found");   
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
            }
            return null;
        }

        public Task<List<string>> GetAuthorizerEmailsAsync(List<string> ListOfEmail)
        {
            try {
                return null;

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
            }
            return null;
        }

    }
}
