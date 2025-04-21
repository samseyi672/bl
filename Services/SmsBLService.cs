using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using RestSharp;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.Services
{
    public class SmsBLService:ISmsBLService
    {
        private readonly ILogger<TransferServices> _logger;
        private readonly AppSettings _settings;
        private readonly IGeneric _genServ;
        private readonly DapperContext _context;

        public SmsBLService(ILogger<TransferServices> logger, IOptions<AppSettings> options, IGeneric genServ, DapperContext context)
        {
            _logger = logger;
            _settings = options.Value;
            _genServ = genServ;
            _context = context;
        }

        public async Task<GenericResponse> SendSmsToCustomer(string subject, string AccountNumber, string Amount, string phoneNumber, string narration, string alertType)
        {
            try
            {
                var CustomerDetails = await _genServ.GetCustomerbyAccountNo(AccountNumber);
                var balanaceenquiry = await _genServ.GetAccountbyCustomerId(CustomerDetails.result.customerID);
                _logger.LogInformation("processing sms balance " + "http://10.20.22.236:8081/omnichannel_transactions/" + "HEader.jpg");
                BalanceEnquiryDetails balanceEnquiryDetails = balanaceenquiry.balances.Any() ? balanaceenquiry.balances.ToList().Find(e => e.accountNumber == AccountNumber) : null;
                SmsRequest smsRequest = new SmsRequest();
                narration = narration + "/" + CustomerDetails.result.firstname.ToUpper()
                 + " " + CustomerDetails.result.middlename.ToUpper() + " " + CustomerDetails.result.lastname;
                string Balance = balanceEnquiryDetails.availableBalance.ToString("N2", CultureInfo.InvariantCulture);
                string AvailBalance = Convert.ToString(balanceEnquiryDetails.totalBalance);
                smsRequest.message = FormalizedSmSObj.formalized(AccountNumber, Amount, narration, Balance);
                smsRequest.subject = subject;
                smsRequest.alertType = alertType;
                smsRequest.contactList.Add(phoneNumber);
                Console.WriteLine("smsRequest " + JsonConvert.SerializeObject(smsRequest));
                _logger.LogInformation("smsRequest " + JsonConvert.SerializeObject(smsRequest));
                _logger.LogInformation("sending sms smsurl " + _settings.SmsUrl);
                string SmsResponse = await _genServ.CallServiceAsyncToString(Method.POST, _settings.SmsUrl, smsRequest, true);
                _logger.LogInformation("SmsResponse " + SmsResponse);
                return new GenericResponse() { Success = true, Response = EnumResponse.Successful, Message = SmsResponse };
            }
            catch (Exception ex)
            {
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> SendSmsToCustomer(string subject, string AccountNumber, string Amount, string phoneNumber, string narration, string alertType,string charge)
        {
            try
            {
                var CustomerDetails = await _genServ.GetCustomerbyAccountNo(AccountNumber);
                var balanaceenquiry = await _genServ.GetAccountbyCustomerId(CustomerDetails.result.customerID);
              //  _logger.LogInformation("processing debit sms balance " + "http://10.20.22.236:8081/omnichannel_transactions/" + "HEader.jpg");
                BalanceEnquiryDetails balanceEnquiryDetails = balanaceenquiry.balances.Any() ? balanaceenquiry.balances.ToList().Find(e => e.accountNumber == AccountNumber) : null;
                SmsRequest smsRequest = new SmsRequest();
                narration = narration + "/" + CustomerDetails.result.firstname.ToUpper()
                 + " " + " " + CustomerDetails.result.lastname;
                string Balance = balanceEnquiryDetails.availableBalance.ToString("N2", CultureInfo.InvariantCulture);
                string AvailBalance = Convert.ToString(balanceEnquiryDetails.totalBalance);
                _logger.LogInformation("formalizing debit sms message...");
                smsRequest.message = FormalizedSmSObj.formalized(AccountNumber, Amount, narration, Balance,charge);
                smsRequest.subject = subject;
                smsRequest.alertType = alertType;
                smsRequest.contactList.Add(phoneNumber);
                Console.WriteLine("smsRequest " + JsonConvert.SerializeObject(smsRequest));
                _logger.LogInformation("debit smsRequest " + JsonConvert.SerializeObject(smsRequest));
              //  _logger.LogInformation("sending sms smsurl " + _settings.SmsUrl);
                string SmsResponse = await _genServ.CallServiceAsyncToString(Method.POST, _settings.SmsUrl, smsRequest, true);
                _logger.LogInformation("debit SmsResponse " + SmsResponse);
                return new GenericResponse() { Success = true, Response = EnumResponse.Successful, Message = SmsResponse };
            }
            catch (Exception ex)
            {
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> SendSmsToCustomerForCredit(string subject, string AccountNumber, string Amount, string phoneNumber,string alertType, string narration)
        {
            try
            {
                var CustomerDetails = await _genServ.GetCustomerbyAccountNo(AccountNumber);
                var balanaceenquiry = await _genServ.GetAccountbyCustomerId(CustomerDetails.result.customerID);
               // _logger.LogInformation("processing sms balance " + "http://10.20.22.236:8081/omnichannel_transactions/" + "HEader.jpg");
                BalanceEnquiryDetails balanceEnquiryDetails = balanaceenquiry.balances.Any() ? balanaceenquiry.balances.ToList().Find(e => e.accountNumber == AccountNumber) : null;
                SmsRequest smsRequest = new SmsRequest();
                narration = narration + "/" + CustomerDetails.result.displayName.ToUpper();
                string Balance = balanceEnquiryDetails.availableBalance.ToString("N2", CultureInfo.InvariantCulture);
                string AvailBalance = Convert.ToString(balanceEnquiryDetails.totalBalance);
                smsRequest.message = FormalizedSmSObj.formalizedforCredit(AccountNumber, Amount, narration, Balance);
                smsRequest.subject = subject;
                smsRequest.alertType = alertType;
                smsRequest.contactList.Add(phoneNumber);
                _logger.LogInformation("smsRequest " + JsonConvert.SerializeObject(smsRequest));
                _logger.LogInformation("credit sending sms smsurl " + _settings.SmsUrl);
                string SmsResponse = await _genServ.CallServiceAsyncToString(Method.POST, _settings.SmsUrl, smsRequest, true);
                _logger.LogInformation("credit " + SmsResponse);
                return new GenericResponse() { Success = true, Response = EnumResponse.Successful, Message = SmsResponse };
            }
            catch (Exception ex)
            {
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> SendSmsNotificationToCustomer(string subject, string PhoneNumber, string narration, string alertType)
        {
            try
            {
                SmsRequest smsRequest = new SmsRequest();
                smsRequest.message = FormalizedSmSObj.formalized(narration);
                smsRequest.subject = subject;
                smsRequest.alertType = alertType;
                smsRequest.contactList.Add(PhoneNumber);
                string SmsResponse = await _genServ.CallServiceAsyncToString(Method.POST, _settings.SmsUrl, smsRequest, true);
                Console.WriteLine($"SmsResponse {SmsResponse}");
                return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
            }
            catch (Exception ex)
            {
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> SendSmsNotificationToCustomer(string subject, string PhoneNumber, string narration, string alertType,string smsurl)
        {
            try
            {
                SmsRequest smsRequest = new SmsRequest();
                smsRequest.message = FormalizedSmSObj.formalized(narration);
                smsRequest.subject = subject;
                smsRequest.alertType = alertType;
                smsRequest.contactList.Add(PhoneNumber);
                string SmsResponse = await _genServ.CallServiceAsyncToString(Method.POST,smsurl, smsRequest, true);
                Console.WriteLine($"SmsResponse {SmsResponse}");
                _logger.LogInformation($"SmsResponse {SmsResponse}");
                return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
            }
            catch (Exception ex)
            {
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }
    }
}






















