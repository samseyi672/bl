using Dapper;
using MySql.Data.MySqlClient;
using Quartz;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using Retailbanking.Common.DbObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Retailbanking.BL.Services
{
    public class BirthdayGreetingJob : IJob
    {
        private readonly string _connectionString;
        private readonly ISmsBLService _smsService;
        private readonly IGeneric _genServ;

        public BirthdayGreetingJob(string connectionString, ISmsBLService smsService, IGeneric genServ)
        {
            _connectionString = connectionString;
            _smsService = smsService;
            _genServ = genServ;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            var today = DateTime.Today;

            using (IDbConnection db = new MySqlConnection(_connectionString))
            {
                string sql = $@"
                SELECT Firstname, Email, Lastname, PhoneNumber
                FROM Users
                WHERE MONTH(DateOfBirth) = @Month AND DAY(DateOfBirth) = @Day";
                // Stream results as they are read from the database
                var usersWithBirthdayToday = db.Query<Users>(sql, new { Month = today.Month, Day = today.Day }, buffered: false);
                foreach (var user in usersWithBirthdayToday)
                {
                    // send a mail and sms 
                    string textcontent = $@"Happy birthday Dear {user.Firstname.ToUpper()} {user.LastName.ToUpper()}.\n
                        May today be a new beginning in your life.We celebrate with you and wish you all the best of life.
                        \nHappy Birthday.\nThank you for Banking with us at TrustBanc J6 MFB.";
                   await _smsService.SendSmsNotificationToCustomer("Birthday Greetings"
                       ,user.PhoneNumber,textcontent,"Birthday");
                    SendMailObject sendMailObject = new SendMailObject();
                    sendMailObject.Html = textcontent;
                    sendMailObject.Email = user.Email;
                    sendMailObject.Subject = "TrustBanc J6 MFB- Birthday Greetings";
                    _genServ.SendMail(sendMailObject);
                }
            }
        }
    }
}





























































































