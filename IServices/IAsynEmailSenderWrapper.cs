using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface IAsynEmailSenderWrapper
    {
      Task  SendmailAsnc(IGeneric _genserv,SendMailObject sendMailObject, string htmlContent, AssetSimplexConfig _settings, AppSettings _appSettings, object user, object customerDataNotFromBvn);
    }
}
