using Dapper;
using Microsoft.Extensions.Logging;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.Services
{
    public class GenericBLServiceHelper
    {
        public async void sendTransactionEmail(ILogger<TransferServices> _logger, IGeneric _genServ, TransactionListRequest transactionListRequest, string accountnumber)
        {
            _logger.LogInformation("entered sendTransactionEmail ..accountnumber " + accountnumber);
            var CustomerDetails = await _genServ.GetCustomerbyAccountNo(accountnumber);
            var balanaceenquiry = await _genServ.GetAccountbyCustomerId(CustomerDetails.result.customerID);
            _logger.LogInformation("image link " + "http://10.20.22.236:8081/omnichannel_transactions/" + "HEader.jpg");
            BalanceEnquiryDetails balanceEnquiryDetails = balanaceenquiry.balances.Any() ? balanaceenquiry.balances.ToList().Find(e => e.accountNumber == accountnumber) : null;
            string availableBalance = balanceEnquiryDetails != null ? balanceEnquiryDetails.availableBalance.ToString() : "";
            _logger.LogInformation("got here ...." + availableBalance);
            string htmlcontent = PdfCreator.ReceiptHtml("./wwwroot/HEader.jpg", transactionListRequest, CustomerDetails, availableBalance, _genServ);
            _logger.LogInformation("htmlcontent " + htmlcontent);
            SendMailObject sendMailObject = new SendMailObject();
            sendMailObject.Html = htmlcontent;
            //sendMailObject.BvnEmail = CustomerDetails.result.email;
            sendMailObject.Email = "opeyemi.adubiaro@trustbancgroup.com";
            sendMailObject.Subject = "TrustBanc J6 MFB- Transaction Notification";
            _logger.LogInformation("sending mail in thread");
            _logger.LogInformation($" enter in thread to send email ");
            _genServ.SendMail(sendMailObject);
            _logger.LogInformation("mail sent ....");
        }

        public void sendMail(IGeneric _genServ, string email, string subject, string htmlcontent)
        {
            SendMailObject sendMailObject = new SendMailObject();
            sendMailObject.Html = htmlcontent;
            sendMailObject.Email = email;
            sendMailObject.Subject = subject;
            _genServ.SendMail(sendMailObject);
            Console.WriteLine("sendMail");
        }

        public async Task<CustomerDataNotFromBvn> getCustomerData(IDbConnection con, string query)
        {
            CustomerDataNotFromBvn customerDataNotFromBvn = (await con.QueryAsync<CustomerDataNotFromBvn>(query)).FirstOrDefault();
            return customerDataNotFromBvn;
        }

        public async void sendTransactionEmail(ILogger<TransferServices> _logger, IGeneric _genServ, TransactionListRequest transactionListRequest, string accountnumber, string Email, string htmlcontent, string Subject)
        {
            _logger.LogInformation("entered sendTransactionEmail ..accountnumber " + accountnumber);
            var CustomerDetails = await _genServ.GetCustomerbyAccountNo(accountnumber);
            var balanaceenquiry = await _genServ.GetAccountbyCustomerId(CustomerDetails.result.customerID);
            _logger.LogInformation("image link " + "http://10.20.22.236:8081/omnichannel_transactions/" + "HEader.jpg");
            BalanceEnquiryDetails balanceEnquiryDetails = balanaceenquiry.balances.Any() ? balanaceenquiry.balances.ToList().Find(e => e.accountNumber == accountnumber) : null;
            string availableBalance = balanceEnquiryDetails != null ? balanceEnquiryDetails.availableBalance.ToString() : "";
            _logger.LogInformation("got here ...." + availableBalance);
            _logger.LogInformation("htmlcontent " + htmlcontent);
            SendMailObject sendMailObject = new SendMailObject();
            sendMailObject.Html = htmlcontent;
            sendMailObject.Email = Email;
            sendMailObject.Subject = Subject;
            _logger.LogInformation($" enter in thread to send email ");
            _genServ.SendMail(sendMailObject);
            _logger.LogInformation("mail sent ....");
        }
        public string GenerateRequestID(int length)
        {
            string characters = "0123456789";
            StringBuilder randomString = new StringBuilder(length);
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                byte[] randomByte = new byte[1];
                for (int i = 0; i < length; i++)
                {
                    rng.GetBytes(randomByte);
                    int randomIndex = randomByte[0] % characters.Length;
                    randomString.Append(characters[randomIndex]);
                }
            }
            return randomString.ToString();
        }
        static DateTime AddBusinessDays(DateTime startDate, int businessDays)
        {
            DateTime currentDate = startDate;
            int daysAdded = 0;

            while (daysAdded < businessDays)
            {
                currentDate = currentDate.AddDays(1);

                // Check if the current day is a weekday (Monday to Friday)
                if (currentDate.DayOfWeek != DayOfWeek.Saturday && currentDate.DayOfWeek != DayOfWeek.Sunday)
                {
                    daysAdded++;
                }
            }
            return currentDate;
        }
        public static bool HasTwoDecimalPlacesAndIsNotZero(string numberStr)
        {
            // Check if the string contains a decimal point
            int decimalIndex = numberStr.IndexOf('.');
            // If there's no decimal point, return false
            if (decimalIndex == -1)
            {
                return false;
            }
            // Get the number of digits after the decimal point
            int numDecimalPlaces = numberStr.Length - decimalIndex - 1;

            // Check if there are exactly two decimal places
            if (numDecimalPlaces == 2)
            {
                // Check if those two decimal places are "00"
                string decimalPart = numberStr.Substring(decimalIndex + 1);
                return decimalPart != "00";
            }
            return false;
        }
        public static string maskedAccountNumber(string originalNumber)
        {
            int maskLength = originalNumber.Length - 5; // Calculate the length to mask, excluding the first 2 and last 3 characters

            string maskedNumber = originalNumber.Substring(0, 2) // Take the first 2 characters
                                   + new string('*', maskLength) // Create a string of asterisks of the desired length
                                   + originalNumber.Substring(originalNumber.Length - 3); // Take the last 3 characters

            Console.WriteLine("maskedNumber " + maskedNumber);
            return maskedNumber;
        }
    }
}
