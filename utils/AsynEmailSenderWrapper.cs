using Microsoft.Extensions.Logging;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using Retailbanking.Common.DbObj;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.utils
{
    public class AsynEmailSenderWrapper: IAsynEmailSenderWrapper
    {
        private readonly ILogger<IAsynEmailSenderWrapper> _logger;

        public AsynEmailSenderWrapper(ILogger<IAsynEmailSenderWrapper> logger)
        {
            _logger = logger;
        }

        public async Task SendmailAsnc(IGeneric _genserv,SendMailObject sendMailObject,string htmlContent,AssetSimplexConfig _settings,AppSettings _appSettings, object user,object customerDataNotFromBvn)
        {
            Task.Run(async () =>
            {
                if (customerDataNotFromBvn is AssetCapitalInsuranceCustomerDataNotFromBvn typedData)
                {
                    if(user is AssetCapitalInsuranceUsers usr)
                    // AssetCapitalInsuranceCustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getAssetCapitalInsuranceCustomerRelationService(con, (int)usr.id, UserType);
                    sendMailObject.Email = typedData != null ? typedData.email : usr.email;
                    CultureInfo nigerianCulture = new CultureInfo("en-NG");
                    sendMailObject.Html = htmlContent;
                    _genserv.SendMail(sendMailObject);
                    _logger.LogInformation("mail sent");
                }
            });
        }
    }
}
