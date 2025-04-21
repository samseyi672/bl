using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using Retailbanking.Common.DbObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Retailbanking.BL.Services
{
    public class GenericServices : IGeneric
    {
        private readonly ILogger<GenericServices> _logger;
        private readonly IMemoryCache _cache;
        private readonly AppSettings appSetting;
        private readonly SmtpDetails smtpDetails;
        private readonly OtpMessage otpMessage;
        private readonly DapperContext _context;

        public GenericServices(ILogger<GenericServices> logger, IMemoryCache memoryCache, IOptions<AppSettings> options, IOptions<SmtpDetails> option_smtp, IOptions<OtpMessage> option_otp, DapperContext context)
        {
            _logger = logger;
            _cache = memoryCache;
            appSetting = options.Value;
            smtpDetails = option_smtp.Value;
            otpMessage = option_otp.Value;
            _context = context;
        }

        public void LogRequestResponse(string methodname, string Request, string Response)
        {
            try
            {
                _logger.LogInformation($"methodname {methodname} Request: -  {Request}");
                _logger.LogInformation($"methodname {methodname} Response: - {Response}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
            }
        }

        public async Task<string> CallServiceAsyncToString(Method method, string url, object requestobject, bool log = false, IDictionary<string, string> header = null)
        {
            {

                try
                {
                    _logger.LogInformation("Enters CallServiceAsync: " + url);
                    _logger.LogInformation("API Request: " + (requestobject!=null?JsonConvert.SerializeObject(requestobject):requestobject));
                    var client = new RestClient(url);
                    var request = new RestRequest(method);

                    if (method == Method.POST)
                    {
                        if(requestobject!=null)
                        {
                            request.AddParameter("application/json", JsonConvert.SerializeObject(requestobject), ParameterType.RequestBody);
                            request.AddHeader("Content-Type", "application/json");
                        }
                        else
                        {
                            //request.AddParameter("application/json", JsonConvert.SerializeObject(requestobject), ParameterType.RequestBody);
                            //request.AddHeader("Content-Type", "application/json");
                        }
                    }

                    if (header != null)
                    {
                        foreach (var item in header)
                        {
                            request.AddHeader(item.Key, item.Value);
                        }
                        _logger.LogInformation("Header added.");
                    }
                    ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
                    IRestResponse response = await client.ExecuteAsync(request);
                    // Console.WriteLine("API Response: " + response.Content);
                    if (log)
                    {
                        _logger.LogInformation("API Response: " + response.Content);
                    }
                    return response.Content;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error calling service.");
                    return null;
                }
            }
        }

        public async Task<string> CallServiceAsyncForFileUploadToString(RestRequest request, Method method, string url, object requestobject,string filepath, bool log = false, IDictionary<string, string> header = null)
        {
            {

                try
                {
                    _logger.LogInformation("Enters CallServiceAsync: " + url);
                    var client = new RestClient(url);
                   // var request = new RestRequest(method);
                     request.Method = method;
                    request.AddFile("file",filepath);

                    // Optionally, you can add other form parameters
                   // request.AddParameter("param1", "value1");
                   // request.AddParameter("param2", "value2");

                  //  if (method == Method.POST)
                   // {
                       // request.AddParameter("application/json", JsonConvert.SerializeObject(requestobject), ParameterType.RequestBody);
                       // request.AddHeader("Content-Type", "application/json");
                    //}

                    if (header != null)
                    {
                        foreach (var item in header)
                        {
                            request.AddHeader(item.Key, item.Value);
                        }
                        _logger.LogInformation("Header added.");
                    }
                    ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
                    IRestResponse response = await client.ExecuteAsync(request);
                    // Console.WriteLine("API Response: " + response.Content);
                    if (log)
                    {
                        _logger.LogInformation("API Response: " + response.Content);
                    }
                    if (response.IsSuccessful)
                    {
                        _logger.LogInformation("File uploaded successfully.");

                        // Delete the file after successful upload
                        try
                        {
                            File.Delete(filepath);
                            _logger.LogInformation("File deleted successfully.");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogInformation($"Error deleting file: {ex.Message}");
                        }
                    }
                    else
                    {
                        _logger.LogInformation($"Error uploading file: {response.ErrorMessage}");
                    }
                    return response.Content;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error calling service.");
                    return null;
                }
            }
        }

        public async Task<T> CallServiceAsync<T>(Method method, string url, object requestobject, bool log = false, IDictionary<string, string> header = null) where T : class
        {
            try
            {
                Console.WriteLine("enters CallServiceAsync " + url);
                // Console.WriteLine($"API Request - url {url}");
                var client = new RestClient(url);
                var request = new RestRequest(method);
                if (method == Method.POST)
                {
                    request.AddParameter("application/json", JsonConvert.SerializeObject(requestobject), ParameterType.RequestBody);
                    request.AddHeader("Content-Type", "application/json");
                }

                if (header != null)
                    foreach (var item in header)
                        request.AddHeader(item.Key, item.Value);
                _logger.LogInformation("header added");

                ServicePointManager
                .ServerCertificateValidationCallback +=
                (sender, cert, chain, sslPolicyErrors) => true;

                _logger.LogInformation("API Call - " + url);
                // Console.WriteLine("API Call - " + url);
                if (requestobject != null)
                {
                    _logger.LogInformation("API Request - " + JsonConvert.SerializeObject(requestobject));
                }
                IRestResponse response = await client.ExecuteAsync(request);
                if (log)
                {
                    _logger.LogInformation("API Response - " + JsonConvert.SerializeObject(response.Content));
                }
                object resObj = null;
                // JsonStringProcessor.processError(JsonConvert.SerializeObject(response.Content));
                var token = JToken.Parse(response.Content);
                if (token is JObject)
                {
                    var json = (JObject)token;
                    // _logger.LogInformation("json " + json + " status " + json.ContainsKey("status"));

                    if (json.ContainsKey("trustBancRef") && json.ContainsKey("resp"))
                    {
                        _logger.LogInformation(" trustBancRef " + json.ContainsKey("trustBancRef"));
                        resObj = new
                        {
                            processingID = json["resp"] != null && json["resp"].Type != JTokenType.Null ? json["resp"]["requestID"].ToString() : "",
                            success = json["success"] != null && json["success"].Type != JTokenType.Null ? bool.Parse(json["success"].ToString()) : false,
                            message = json["resp"] != null && json["resp"].Type != JTokenType.Null ? json["resp"]["retmsg"].ToString() : ""
                        };
                        var model2 = JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(resObj));
                        return model2;
                    }
                    if (json.ContainsKey("transactioResponse"))
                    {
                        _logger.LogInformation("sending model out ....");

                        resObj = new
                        {
                            processingID = json["transactioResponse"] != null && json["transactioResponse"].Type != JTokenType.Null ? json["transactioResponse"]["requestID"].ToString() : "",
                            success = json["status"] != null && json["status"].Type != JTokenType.Null ? bool.Parse(json["status"].ToString()) : false,
                            message = json["transactioResponse"] != null && json["transactioResponse"].Type != JTokenType.Null ? json["transactioResponse"]["retMsg"].ToString() : "",
                            tranAmt = json["transactioResponse"] != null && json["transactioResponse"].Type != JTokenType.Null ? json["transactioResponse"]["tranAmt"].ToString() : "",
                            postDate = json["transactioResponse"] != null && json["transactioResponse"].Type != JTokenType.Null ? json["transactioResponse"]["postdate"].ToString() : "",
                        };
                        // _logger.LogInformation("sending model out ....");
                        var model2 = JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(resObj));
                        //Console.WriteLine("model2 .. " + JsonConvert.SerializeObject(model2));
                        _logger.LogInformation("transactionResponse model2 .. " + JsonConvert.SerializeObject(model2));
                        return model2;
                    }
                }
                else if (token is JArray)
                {
                    JArray jsonArray = (JArray)token;
                    var jsonObject = jsonArray.Children<JObject>().First();
                    if (jsonObject.ContainsKey("transactioResponse"))
                    {
                        resObj = new
                        {
                            processingID = jsonObject["transactioResponse"] != null && jsonObject["transactioResponse"].Type != JTokenType.Null ? jsonObject["transactioResponse"]["requestID"].ToString() : "",
                            success = jsonObject["status"] != null && jsonObject["status"].Type != JTokenType.Null ? bool.Parse(jsonObject["status"].ToString()) : false,
                            message = jsonObject["transactioResponse"] != null && jsonObject["transactioResponse"].Type != JTokenType.Null ? jsonObject["transactioResponse"]["retMsg"].ToString() : "",
                            TranAmt = jsonObject["transactioResponse"] != null && jsonObject["transactioResponse"].Type != JTokenType.Null ? jsonObject["transactioResponse"]["tranAmt"].ToString() : ""
                        };
                        var model2 = JsonConvert.DeserializeObject<T>(System.Text.Json.JsonSerializer.Serialize(resObj, new JsonSerializerOptions { WriteIndented = true }));
                        // _logger.LogInformation("json array model2 .. " + JsonConvert.SerializeObject(model2));
                        model2 = JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(model2));
                        return model2;
                    }

                }
                var model = JsonConvert.DeserializeObject<T>(response.Content);
                return model;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return default;
            }
        }

        public string EncryptString(string str)
        {
            try
            {
                if (string.IsNullOrEmpty(str))
                    return string.Empty;
                var hashAlgorithm = new Org.BouncyCastle.Crypto.Digests.Sha3Digest(512);

                // Choose correct encoding based on your usecase
                byte[] input = Encoding.ASCII.GetBytes(str);

                hashAlgorithm.BlockUpdate(input, 0, input.Length);

                byte[] result = new byte[64]; // 512 / 8 = 64
                hashAlgorithm.DoFinal(result, 0);

                string hashString = BitConverter.ToString(result);
                hashString = hashString.Replace("-", "").ToLowerInvariant();
                return hashString;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return null;
            }
        }

        public bool CheckPasswordCondition(string newpassword)
        {
            try
            {
                var chars = new List<char>();
                chars.AddRange(newpassword);

                //if contains Uppercase
                if (!chars.Any(x => x >= 'A' && x <= 'Z'))
                    return false;

                //if contains Lower
                if (!chars.Any(x => x >= 'a' && x <= 'z'))
                    return false;

                //if contains Numbers
                if (!chars.Any(x => x >= '0' && x <= '9'))
                    return false;

                string[] specialschar = new string[] { "~", "!", "@", "#", "$", "%", "^", "^&", "*", "(", ")", "-", "_", "=", "+" };
                if (!chars.Any(x => specialschar.Any(p => p == x.ToString())))
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return false;
            }
        }

        public string ConvertStringtoMD5(string strword)
        {
            MD5 md5 = MD5.Create();
            byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(strword);
            byte[] hash = md5.ComputeHash(inputBytes);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("x2"));
            }
            return sb.ToString();
        }

        public string RemoveSpecialCharacters(string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;
            StringBuilder sb = new StringBuilder();
            foreach (char c in str)
            {
                if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '.' || c == '_' || c == ' ' || c == '&' || c == '-' || c == '@')
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        public string StringNumbersOnly(string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;
            StringBuilder sb = new StringBuilder();
            foreach (char c in str)
                if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                    sb.Append(c);

            return sb.ToString();
        }

        public DateTime ConvertDatetime(string dtstring)
        {
            try
            {
                if (string.IsNullOrEmpty(dtstring))
                    return DateTime.Now;

                string[] format = new string[] { "yyyy-MM-dd", "yyyy-MM-dd HH-mm-ss", "yyyy-MM-dd hh-mm-ss tt", "MM/dd/yyyy", "M/d/yyyy", "M/dd/yyyy", "MM/d/yyyy", "dd-MMM-yyyy", "dd/MMM/yyyy", "dd/MM/yyyy", "ddMMMyyyy", "dd-MM-yyyy", "yyyy-MM-ddThh:mm:ss.fff", "dd/MM/yyyy hh:mm:ss tt" };
                foreach (string s in format)
                    try
                    {
                        DateTime dt = DateTime.ParseExact(dtstring.Trim().ToUpper(), s, CultureInfo.InvariantCulture);
                        return dt;
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                return DateTime.Now;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return DateTime.Now;
            }
        }

        public string GetSession()
        {
            try
            {
                string sess = EncryptString(DateTime.Now.ToString("ddMMyyyyHHmmss") + "_" + new Random().Next(1111, 9999));
                StringBuilder sb = new StringBuilder();
                foreach (char c in sess)
                    if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '.' || c == '_')
                        sb.Append(c);
                Console.WriteLine($"returning {sb.ToString()}");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return null;
            }
        }

        public string GenerateOtp() => appSetting.DemoOtp == "y" ? "123456" : new Random().Next(111111, 999999).ToString();

        public string GenerateOtp2() => appSetting.DemoOtp == "y" ? "123456" : new Random().Next(111111, 999999).ToString();

        public async Task SendOtp(OtpType otpType, string Otp, string number, string email)
        {
            try
            {
                if (appSetting.DemoOtp == "y") return;
                string msg = otpMessage.Registration;

                if (otpType == OtpType.PasswordReset)
                    msg = otpMessage.PasswordReset;

                if (otpType == OtpType.UnlockDevice)
                    msg = otpMessage.UnlockDevice;

                if (otpType == OtpType.UnlockProfile)
                    msg = otpMessage.UnlockProfile;

                msg = msg.Replace("$otp", Otp);

                string msgref = DateTime.Now.ToString("ddMMyyyyHHmmss") + "_" + new Random().Next(11111, 99999);
                var request = new SendSmsRequest()
                {
                    ClientKey = appSetting.FinedgeKey,
                    Message = msg,
                    PhoneNumber = number,
                    SmsReference = msgref
                };
                await SendSMS(request);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
            }
        }

        public async Task SendOtp2(OtpType otpType, string Otp, string number, ISmsBLService smsBLService)
        {
            try
            {
                if (appSetting.DemoOtp == "y") return;
                string msg = otpMessage.Registration;

                if (otpType == OtpType.PasswordReset)
                    msg = otpMessage.PasswordReset;

                if (otpType == OtpType.UnlockDevice)
                    msg = otpMessage.UnlockDevice;

                if (otpType == OtpType.UnlockProfile)
                    msg = otpMessage.UnlockProfile;

                msg = msg.Replace("$otp", Otp);

                string msgref = DateTime.Now.ToString("ddMMyyyyHHmmss") + "_" + new Random().Next(11111, 99999);
                var request = new SendSmsRequest()
                {
                    ClientKey = appSetting.FinedgeKey,
                    Message = msg,
                    PhoneNumber = number,
                    SmsReference = msgref
                };
                _logger.LogInformation("SmsUrl " + appSetting.SmsUrl);
                GenericResponse response = await smsBLService.SendSmsNotificationToCustomer("Otp", number, $@"your otp is {msg}"
                      , "Registration", appSetting.SmsUrl);
                _logger.LogInformation(response.Message);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
            }
        }

        public async Task<SendSmsResponse> SendSMS(SendSmsRequest Request)
        {
            try
            {
                if (appSetting.DemoOtp == "y") return new SendSmsResponse();
                var resp = await CallServiceAsync<SendSmsResponse>(Method.POST, $"{appSetting.FinedgeUrl}api/posting/SendSms", Request, true);
                return resp;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new SendSmsResponse() { errorMessage = ex.Message };
            }
        }

        public async Task<SendSmsResponse> SendSMS2(SendSmsRequest Request, ISmsBLService smsBLService)
        {
            try
            {
                // if (appSetting.DemoOtp == "y") return new SendSmsResponse();
                var resp = await CallServiceAsync<SendSmsResponse>(Method.POST, $"{appSetting.FinedgeUrl}api/posting/SendSms", Request, true);
                return resp;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new SendSmsResponse() { errorMessage = ex.Message };
            }
        }

        public GenericResponse SendMail(SendMailObject Request, MemoryStream pdfStream)
        {
            try
            {
                _logger.LogInformation($"SendMailObject {JsonConvert.SerializeObject(Request)} smtpDetails.Host {smtpDetails.Host} -- smtpDetails.Port {smtpDetails.Port}");
                SmtpClient MyServer = new SmtpClient()
                {
                    Host = smtpDetails.Host,// "smtp.office365.com",
                    Port = smtpDetails.Port,// 587,
                    EnableSsl = true,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    //Credentials = new NetworkCredential("eyiwunmi.dawodu@trustbancgroup.com", "Jugger007%")
                    Credentials = new NetworkCredential(smtpDetails.Username, smtpDetails.Password)
                };

                MailAddress from = new MailAddress(smtpDetails.FrmMail, smtpDetails.FrmName);
                MailAddress receiver = new MailAddress(Request.Email, Request.Firstname);
                MailMessage Mymessage = new MailMessage(from, receiver);
                Attachment attachment = null;
                if (pdfStream != null)
                {
                    attachment = new Attachment(pdfStream, $"{Request.Firstname}.pdf", "application/pdf");
                    Mymessage.Attachments.Add(attachment);
                }
                if (!string.IsNullOrEmpty(smtpDetails.BccMail))
                {
                    MailAddress bcc = new MailAddress(smtpDetails.BccMail);
                    Mymessage.Bcc.Add(bcc);
                }

                Mymessage.Subject = Request.Subject;
                Mymessage.Body = Request.Html;
                //sends the email
                Mymessage.IsBodyHtml = true;
                try
                {
                    MyServer.Send(Mymessage);
                    Console.WriteLine("mail sent in mailer .......");
                    _logger.LogInformation($"Mail sent to {Request.Email} -- {Request.Subject}");
                    return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.Message + " " + ex.StackTrace);
                    return new GenericResponse() { Message = ex.Message, Response = EnumResponse.SystemError };
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Message = ex.Message, Response = EnumResponse.SystemError };
            }
        }

        public string CreateRandomPassword(int length = 8)
        {
            // Create a string of characters, numbers, special characters that allowed in the password  
            string validChars = "ABCDEFGHJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789@#$%";
            Random random = new Random();

            // Select one random character at a time from the string  
            // and create an array of chars  
            char[] chars = new char[length];
            for (int i = 0; i < length; i++)
                chars[i] = validChars[random.Next(0, validChars.Length)];

            return new string(chars);
        }

        public async Task SetUserSession(long UserId, string Session, int ChannelId, IDbConnection con)
        {
            try
            {
                //var usrSess = await con.QueryAsync<long>($"select RequestReference from user_session where userid={UserId}");
                var usrSess = await con.QueryAsync<long>($"select * from user_session where userid={UserId}");
                if (usrSess.Any())
                {
                    await con.ExecuteAsync($"update user_session set session = @sess, status = 1,channelid={ChannelId}, createdon= sysdate(), last_activity = sysdate() where id = {usrSess.FirstOrDefault()}", new { sess = Session });
                    return;
                }

                string sql = $@"insert into user_session (userid, channelid, session, status, createdon, last_activity)
                    values ({UserId},{ChannelId},@ses,1,sysdate(),sysdate())";
                await con.ExecuteAsync(sql, new { ses = Session });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
            }
        }

        public async Task SetAssetCapitalInsuranceUserSession(long UserId,string UserType, string Session, int ChannelId, IDbConnection con)
        {
            try
            {
                //var usrSess = await con.QueryAsync<long>($"select RequestReference from user_session where userid={UserId}");
                var usrSess = await con.QueryAsync<long>($"select id from asset_capital_insurance_user_session where user_id={UserId} and user_type='{UserType}'");
                if (usrSess.Any())
                {
                    await con.ExecuteAsync($"update asset_capital_insurance_user_session set session = @sess, status = 1,channelid={ChannelId}, created_on= UTC_TIMESTAMP(), last_activity = UTC_TIMESTAMP() where id = {usrSess.FirstOrDefault()} and user_type=@UserType", new { sess = Session, UserType= UserType });
                    return;
                }
                string sql = $@"insert into asset_capital_insurance_user_session (user_id, channelid, session, status, created_on, last_activity,user_type)
                    values ({UserId},{ChannelId},@ses,1,UTC_TIMESTAMP(),UTC_TIMESTAMP(),'{UserType}')";
                await con.ExecuteAsync(sql, new { ses = Session });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
            }
        }

        public async Task<bool> ValidateSession(long UserId, string Session, int ChannelId, IDbConnection con)
        {
            try
            {
                if (appSetting.ValidateSession == "n")
                    return true;
                string Username = (await con.QueryAsync<string>($"select username from users where id={UserId}")).FirstOrDefault();
                var presentlogindevice = await con.QueryAsync<string>($"select device from customer_devices where loginstatus=1 and username ='{Username}' and trackdevice='present'");
                Console.WriteLine("presentlogindevice " + presentlogindevice);
                _logger.LogInformation($"presentlogindevice {presentlogindevice}");
                // var genericResponse = await DeactivateDeviceSession(Username, con, presentlogindevice.FirstOrDefault());
                // if (genericResponse != null)
                // {
                //     return false;
                // }
                var usrSess = await con.QueryAsync<UserSession>($"select * from user_session where userid={UserId}");
                if (!usrSess.Any() || usrSess.FirstOrDefault().Session != Session || DateTime.Now.Subtract(usrSess.FirstOrDefault().Last_Activity).TotalHours > appSetting.SessionLenght || ChannelId != usrSess.FirstOrDefault().ChannelId)
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return false;
            }
        }

        public async Task<bool> ValidateSessionForAssetCapitalInsurance(long ucid, string Session, int ChannelId, IDbConnection con,string UserType)
        {
            try
            {
                var usrSess = await con.QueryAsync<AssetCapitalInsuranceUserSession>($"select * from asset_capital_insurance_user_session where ucid={ucid} and user_type='{UserType}'");
                if (!usrSess.Any() || usrSess.FirstOrDefault().session != Session || DateTime.Now.Subtract(usrSess.FirstOrDefault().last_activity).TotalHours > appSetting.SessionLenght || ChannelId != usrSess.FirstOrDefault().channelId)
                    return false;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return false;
            }
        }

        public async Task<bool> ValidateSessionForAssetCapitalInsurance(string Session,string UserType, IDbConnection con)
        {
            try
            {
                var usrSess = await con.QueryAsync<AssetCapitalInsuranceUserSession>($"select * from asset_capital_insurance_user_session where session='{Session}' and user_type='{UserType}'");
                if (!usrSess.Any() || usrSess.FirstOrDefault().session != Session || DateTime.UtcNow.Subtract(usrSess.FirstOrDefault().last_activity).TotalHours > appSetting.SessionLenght)
                    return false;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return false;
            }
        }

        private async Task<GenericResponse> DeactivateDeviceSession(string username, IDbConnection con, string presentlogindevice)
        {
            //check devices and deactivate sessions
            GenericResponse genericResponse = null;
            var CustomerDevices = (await con.QueryAsync<CustomerDevices>("select * from customer_devices where username = @username and device=@device", new { username = username, device = presentlogindevice }));
            /*
            if (CustomerDevices.Any())
            {
                CustomerDevices = CustomerDevices.ToList();
                bool checkdevice = false;
                foreach (var item in CustomerDevices)
                {
                    if (item.Device.ToLower().Trim() != presentlogindevice.ToLower().Trim() && item.TrackDevice.ToLower().Trim()=="recent") // which means it is not the current device
                    {
                        checkdevice = true;
                        //deactivate Request.Device.ToLower().Trim()
                    }
                }
                if (checkdevice)
                {
                    var CustomerDevice2 = (await con.QueryAsync<CustomerDevices>("select * from customer_devices where presentlogindevice = @presentlogindevice and loginstatus=1 and @username=username", new { device = presentlogindevice, username = username }));// get the current login devices 
                    if (CustomerDevice2.Any())
                    {
                        string session = CustomerDevice2.FirstOrDefault().Session;
                        if (!string.IsNullOrEmpty(session))
                        {
                            genericResponse = new GenericResponse() { message = "You have logged in on other devices", Response = EnumResponse.AlreadyLoggedInOnDifferentChannel };
                        }
                        await con.ExecuteAsync(@"update customer_devices set session=@session where presentlogindevice=@presentlogindevice and username=@username", new { session=(object)null, device = presentlogindevice, username = username });
                        genericResponse = new GenericResponse() { message = "You have logged in on other devices", Response = EnumResponse.AlreadyLoggedInOnDifferentChannel };
                    }
                    return genericResponse;
                }
               // return genericResponse;
            }
            else {
              //genericResponse =   new GenericResponse() { message = "You have logged in on other devices", Response = EnumResponse.AlreadyLoggedInOnDifferentChannel };
              //  return genericResponse;
              }
            */
            return null;
        }
        public async Task<bool> ValidateSession(string Username, string Session, int ChannelId, IDbConnection con)
        {
            try
            {
                if (appSetting.ValidateSession == "y")
                    return true;

                if (string.IsNullOrEmpty(Username))
                    return false;
                // var presentlogindevice =  await con.QueryAsync<string>($"select * from customer_devices where loginstatus=0 and username = {Username}");
                // Console.WriteLine("presentlogindevice "+ presentlogindevice);
                // _logger.LogInformation($"presentlogindevice {presentlogindevice}"); 
                // var genericResponse = await DeactivateDeviceSession(Username,con,presentlogindevice.FirstOrDefault());
                // if (genericResponse!=null) {
                //   return false;
                // }
                var usrSess = await con.QueryAsync<UserSession>($"select * from user_session where userid=(select id from users where lower(username) = @urs)", new { urs = Username.ToLower().Trim() });
                _logger.LogInformation("Time "+(DateTime.Now.Subtract(usrSess.FirstOrDefault().Last_Activity).TotalHours > appSetting.SessionLenght));
                if (!usrSess.Any() || usrSess.FirstOrDefault().Session != Session || DateTime.Now.Subtract(usrSess.FirstOrDefault().Last_Activity).TotalHours > appSetting.SessionLenght || ChannelId != usrSess.FirstOrDefault().ChannelId)
                    return false;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return false;
            }
        }

        public async Task<Users> GetUserbyPhone(string PhoneNumber, IDbConnection con)
        {
            try
            {
                var resp = await con.QueryAsync<Users>("select * from users where lower(PhoneNumber) = @urs", new { urs = PhoneNumber.ToLower().Trim() });
                return resp.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new Users();
            }
        }

        public async Task<Users> GetUserbyUsername(string Username, IDbConnection con)
        {
            try
            {
                if (string.IsNullOrEmpty(Username))
                    return null;
                var resp = await con.QueryAsync<Users>("select * from users where lower(Username) = @urs", new { urs = Username.ToLower().Trim() });
                _logger.LogInformation("user resp details "+JsonConvert.SerializeObject(resp));
                return resp.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return null;
            }
        }

        public async Task<AssetCapitalInsuranceUsers> GetAssetCapitalInsuraceUserbyUsername(string Username,string UserType, IDbConnection con)
        {
            try
            {
                if (string.IsNullOrEmpty(Username))
                    return null;
                var resp = await con.QueryAsync<AssetCapitalInsuranceUsers>("select * from asset_capital_insurance_user where lower(username) = @urs and user_type=@UserType", new { urs = Username.ToLower().Trim(),UserType=UserType });
                _logger.LogInformation("user resp details " + JsonConvert.SerializeObject(resp));
                return resp.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return null;
            }
        }

        public async Task<Users> GetUserbyCustomerId(string Username, IDbConnection con)
        {
            try
            {
                //var resp = await con.QueryAsync<Users>("select * from users where Username = @custId", new { custId = Username.ToLower().Trim() });
                var resp = await con.QueryAsync<Users>("select * from users where customerid = @custId", new { custId = Username.ToLower().Trim() });
                return resp.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new Users();
            }
        }

        public async Task<AssetCapitalInsuranceUsers> GetAssetCapitalInsuranceUserbyBvn(string bvn,string UserType, IDbConnection con)
        {
            try
            {
                //var resp = await con.QueryAsync<Users>("select * from users where Username = @custId", new { custId = Username.ToLower().Trim() });
                var resp = await con.QueryAsync<AssetCapitalInsuranceUsers>("select * from asset_capital_insurance_user where bvn = @custId and user_type=@UserType", new { custId = bvn,UserType=UserType});
                return resp.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new AssetCapitalInsuranceUsers();
            }
        }
        /* this is the modified one but backed up since the customerid is returning null 
         * which has not been fixed at of the time yet.
        public async Task<Users> GetUserbyCustomerId(string CustomerId, IDbConnection con)
        {
            try
            {
                var resp = await con.QueryAsync<Users>("select * from users where CustomerId = @custId", new { custId = CustomerId.ToLower().Trim() });
                return resp.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.message + " " + ex.StackTrace);
                return new Users();
            }
        }
        */
        public async Task<bool> CheckIfUserIsLoggedIn(string PhoneNumber, int ChannelId, IDbConnection con)
        {
            try
            {
                if (appSetting.ValidateSession == "n")
                    return true;

                if (string.IsNullOrEmpty(PhoneNumber))
                    return false;

                var usrSess = await con.QueryAsync<UserSession>($"select * from user_session where userid=(select id from users where lower(PhoneNumber) = @urs)", new { urs = PhoneNumber.ToLower().Trim() });
                if (!usrSess.Any() || DateTime.Now.Subtract(usrSess.FirstOrDefault().Last_Activity).TotalHours > appSetting.SessionLenght || ChannelId != usrSess.FirstOrDefault().ChannelId)
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return false;
            }
        }

        public string EncryptString(string str, string Unique_salt = null)
        {
            try
            {
                if (string.IsNullOrEmpty(str))
                    return string.Empty;

                string salt = "76767676hghghytytyvvvv!!!!##hghghghghghghghghghgkgkgkgkgkg..gfgfgghfyutytt";
                if (!string.IsNullOrEmpty(Unique_salt))
                    salt = Unique_salt;
                byte[] saltedValue = Encoding.UTF8.GetBytes(str + salt);
                var data = new SHA512Managed().ComputeHash(saltedValue);
                return Convert.ToBase64String(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return null;
            }
        }

        public async Task<List<GenericValue>> GetEntityType(EntityType entityType)
        {
            try
            {
                var myData = new List<GenericValue>();
                if (!_cache.TryGetValue(entityType.ToString(), out myData))
                {
                    using (IDbConnection con = _context.CreateConnection())
                    {
                        // Key not in cache, so get data.
                        var myData1 = await con.QueryAsync<GenericValue>($"select enum_id id, enum_value value from enum_values where entity_id = {(int)entityType}");
                        myData = myData1.ToList();
                        // Set cache options.
                        var cacheEntryOptions = new MemoryCacheEntryOptions()
                            // Keep in cache for this time, reset time if accessed.
                            .SetSlidingExpiration(TimeSpan.FromDays(1));

                        // Save data in cache.
                        _cache.Set(entityType.ToString(), myData, cacheEntryOptions);
                    }
                }
                return myData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new List<GenericValue>();
            }
        }

        public async Task<List<ClientCredentials>> GetClientCredentials()
        {
            try
            {
                var myData = new List<ClientCredentials>();
                if (!_cache.TryGetValue(CacheKeys.Clients, out myData))
                {
                    using (IDbConnection con = _context.CreateConnection())
                    {
                        // Key not in cache, so get data.
                        var myData1 = await con.QueryAsync<ClientCredentials>("select id ClientId, ChannelKey,ChannelSalt,MaxAmount from channel where status = 1");
                        myData = myData1.ToList();
                        // Set cache options.
                        var cacheEntryOptions = new MemoryCacheEntryOptions()
                            // Keep in cache for this time, reset time if accessed.
                            .SetSlidingExpiration(TimeSpan.FromDays(1));

                        // Save data in cache.
                        _cache.Set(CacheKeys.Clients, myData, cacheEntryOptions);
                    }
                }
                return myData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new List<ClientCredentials>();
            }
        }

        public async Task<string> DecryptStringAES(string cipherText, int ChannelId, string connectionstring)
        {
            try
            {
                var getClients = await GetClientCredentials();

                var client = getClients.FirstOrDefault(x => x.ClientId == ChannelId);
                if (client == null)
                    return string.Empty;

                var secretkey = Encoding.UTF8.GetBytes(client.ChannelKey);
                var ivKey = Encoding.UTF8.GetBytes(client.ChannelSalt);

                var encrypted = Convert.FromBase64String(cipherText);
                var decriptedFromJavascript = DecryptStringFromBytes(encrypted, secretkey, ivKey);
                // _logger.LogWarning($"successfully decriptedFromJavascript");
                return decriptedFromJavascript;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return string.Empty;
            }
        }

        public async Task<string> EncryptStringAES(string plainText, int ChannelId, string connectionstring)
        {
            try
            {
                var getClients = await GetClientCredentials();
                var client = getClients.FirstOrDefault(x => x.ClientId == ChannelId);
                if (client == null)
                    return string.Empty;

                var secretkey = Encoding.UTF8.GetBytes(client.ChannelKey);
                var ivKey = Encoding.UTF8.GetBytes(client.ChannelSalt);

                var plainBytes = Encoding.UTF8.GetBytes(plainText);
                // var encrypted = Convert.ToBase64String(plainBytes);
                var encryptedFromJavascript = EncryptStringToBytes(plainText, secretkey, ivKey);
                // _logger.LogWarning($" decriptedFromJavascript: {encryptedFromJavascript}");
                return encryptedFromJavascript;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return string.Empty;
            }
        }

        private static string EncryptStringToBytes(string plainText, byte[] key, byte[] iv)
        {
            // Check arguments.  
            //if (plainText == null || plainText.Length <= 0)
            //{
            //    throw new ArgumentNullException("plainText");
            //}
            //if (key == null || key.Length <= 0)
            //{
            //    throw new ArgumentNullException("key");
            //}
            //if (iv == null || iv.Length <= 0)
            //{
            //    throw new ArgumentNullException("key");
            //}
            byte[] encrypted;
            // Create a RijndaelManaged object  
            // with the specified key and IV.  
            using (var rijAlg = new RijndaelManaged())
            {
                rijAlg.Mode = CipherMode.CBC;
                rijAlg.Padding = PaddingMode.PKCS7;
                rijAlg.FeedbackSize = 128;
                rijAlg.Key = key;
                rijAlg.IV = iv;
                // Create a decrytor to perform the stream transform.  
                var encryptor = rijAlg.CreateEncryptor(rijAlg.Key, rijAlg.IV);
                // Create the streams used for encryption.  
                using (var msEncrypt = new MemoryStream())
                {
                    using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        using (var swEncrypt = new StreamWriter(csEncrypt))
                        {
                            //Write all data to the stream.  
                            swEncrypt.Write(plainText);
                        }
                        encrypted = msEncrypt.ToArray();
                    }
                }
            }
            //Convert.ToBase64String(encrypted);
            // Return the encrypted bytes from the memory stream.  
            return Convert.ToBase64String(encrypted);
        }

        private static string DecryptStringFromBytes(byte[] cipherText, byte[] key, byte[] iv)
        {
            // Check arguments.  
            if (cipherText == null || cipherText.Length <= 0)
            {
                throw new ArgumentNullException("cipherText");
            }
            if (key == null || key.Length <= 0)
            {
                throw new ArgumentNullException("key");
            }
            if (iv == null || iv.Length <= 0)
            {
                throw new ArgumentNullException("key");
            }
            // Declare the string used to hold  
            // the decrypted text.  
            string plaintext = null;
            // Create an RijndaelManaged object  
            // with the specified key and IV.  
            using (var rijAlg = new RijndaelManaged())
            {
                //Settings  
                rijAlg.Mode = CipherMode.CBC;
                rijAlg.Padding = PaddingMode.PKCS7;
                // rijAlg.FeedbackSize = 128;
                rijAlg.Key = key;
                rijAlg.IV = iv;
                // Create a decrytor to perform the stream transform.  
                var decryptor = rijAlg.CreateDecryptor(rijAlg.Key, rijAlg.IV);

                try
                {
                    // Create the streams used for decryption.  
                    using (var msDecrypt = new MemoryStream(cipherText))
                    {
                        using (var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                        {
                            using (var srDecrypt = new StreamReader(csDecrypt))
                            {
                                // Read the decrypted bytes from the decrypting stream  
                                // and place them in a string.  
                                plaintext = srDecrypt.ReadToEnd();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    plaintext = "keyError";
                }
            }
            return plaintext;
        }

        /*
        public async Task InsertOtp(OtpType otpType, long ObjId, string session, string otp, IDbConnection con)
        {
            Console.WriteLine("ObjId " + ObjId);
            var regId = await con.QueryAsync<OtpSession>($"select * from otp_session where ObjId ={ObjId}");
           // Console.WriteLine("regId " + JsonConvert.SerializeObject(regId));
            if (regId.Any()) {
                await con.ExecuteAsync($"update otp_session set status = 1, Session='{session}' where ObjId ={ObjId} and otp_type = {(int)otpType}");
              //  await con.ExecuteAsync($"insert into otp_session(otp_type, session, otp, status, createdon,ObjId) values ({(int)otpType},'{session}','{otp}',1,sysdate(),{ObjId})");
               // Console.WriteLine($"inserted into otp_session {session} ObjId {ObjId}");
            }
            else {
                await con.ExecuteAsync($"insert into otp_session(otp_type, session, otp, status, createdon,ObjId) values ({(int)otpType},'{session}','{otp}',1,sysdate(),{ObjId})");
                Console.WriteLine($"inserted into otp_session {session} ObjId {ObjId}");
            }
           // await con.ExecuteAsync($"update otp_session set status = 0 where ObjId ={ObjId} and otp_type = {(int)otpType}");
            //await con.ExecuteAsync($"insert into otp_session(otp_type, session, otp, status, createdon,ObjId) values ({(int)otpType},'{session}','{otp}',1,sysdate(),{ObjId})");
            //Console.WriteLine($"inserted into otp_session {session} ObjId {ObjId}");
        }
        */
        //updated method
        public async Task InsertOtp(OtpType otpType, long ObjId, string session, string otp, IDbConnection con)
        {
            // Log ObjId for debugging
            _logger.LogInformation("session " + session + "ObjId: " + ObjId + " otptype " + (int)otpType);
            // Using parameterized query to avoid SQL injection
            var regId = await con.QueryAsync<OtpSession>(
                "SELECT * FROM otp_session WHERE ObjId = @ObjId",
                new { ObjId });
            _logger.LogInformation("Query Result: " + JsonConvert.SerializeObject(regId));
            if (regId != null && regId.Any())
            {
                // Log that we are updating the record
                _logger.LogInformation($"Updating otp_session for ObjId {ObjId} and otp_type {(int)otpType}, otp {otp}");
                // Update existing record using parameterized query
                /*
                await con.ExecuteAsync(
                    "UPDATE otp_session SET status = 1,otp= @Otp, session = @Session WHERE ObjId = @ObjId AND otp_type = @OtpType",
                    new { Session = session, ObjId, OtpType = (int)otpType,Otp=otp });
                */
                await con.ExecuteAsync(
          "UPDATE otp_session SET status = 1,otp= @Otp, session = @Session,otp_type = @OtpType WHERE ObjId = @ObjId",
          new { Session = session, ObjId, OtpType = (int)otpType, Otp = otp });
                _logger.LogInformation("Update completed");
            }
            else
            {
                // Log that we are inserting a new record
                _logger.LogInformation($"Inserting new otp_session for ObjId {ObjId}");
                // Insert new record using parameterized query
                await con.ExecuteAsync(
                    "INSERT INTO otp_session (otp_type, session, otp, status, createdon, ObjId) " +
                    "VALUES (@OtpType, @Session, @Otp, 1, sysdate(), @ObjId)",
                    new { OtpType = (int)otpType, Session = session, otp, ObjId });
                _logger.LogInformation("insert completed ....");
                //Console.WriteLine("Insert completed");
            }
        }


        public async Task InsertOtpForAssetCapitalInsurance(OtpType otpType,string UserType, long ucid, string session, string otp, IDbConnection con,long UserId)
        {
            // Log ObjId for debugging
            _logger.LogInformation("session " + session + "ObjId: " + ucid + " otptype " + (int)otpType);
            // Using parameterized query to avoid SQL injection
            var regId = await con.QueryAsync<AssetCapitalInsuranceUserSession>(
                "SELECT * FROM asset_capital_insurance_otp_session WHERE (ucid = @ucid or user_id=@UserId) and user_type=@UserType",
                new { ucid, UserType, UserId });
            _logger.LogInformation("Query Result: " + JsonConvert.SerializeObject(regId));
            if (regId != null && regId.Any())
            {
                // Log that we are updating the record
                _logger.LogInformation($"Updating asset_capital_insurance_otp_session for ObjId {ucid} and otp_type {(int)otpType}, otp {otp}");

                await con.ExecuteAsync(
          "UPDATE asset_capital_insurance_otp_session SET status = 1,otp= @Otp, session = @Session,otp_type = @OtpType WHERE ucid = @ucid and user_type=@UserType",
          new { Session = session, ucid, OtpType = (int)otpType, Otp = otp, UserType= UserType });
                _logger.LogInformation("Update completed");
            }
            else
            {
                // Log that we are inserting a new record
                _logger.LogInformation($"Inserting new otp_session for ObjId {ucid}");
                // Insert new record using parameterized query
                await con.ExecuteAsync(
                    "INSERT INTO asset_capital_insurance_otp_session (otp_type, session, otp, status, createdon, ucid,user_type,user_id) " +
                    "VALUES (@OtpType, @Session, @Otp, 1, sysdate(), @ucid,@UserType,@UserId)",
                    new { OtpType = (int)otpType, Session = session, otp, ucid, UserType, UserId=UserId});
                _logger.LogInformation("insert completed ....");
            }
        }

        public async Task InsertOtpForAssetCapitalInsuranceOnRegistration(OtpType otpType, string UserType, string bvn, string session, string otp, IDbConnection con)
        {
            // Log ObjId for debugging
            _logger.LogInformation("session " + session + "ObjId: " + bvn + " otptype " + (int)otpType);
            // Using parameterized query to avoid SQL injection
            var regId = await con.QueryAsync<AssetCapitalInsuranceRegistrationOtpSession>(
                "SELECT * FROM asset_capital_insurance_registration_otp_session WHERE bvn = @bvn and user_type=@UserType",
                new { bvn, UserType });
            _logger.LogInformation("Query Result: " + JsonConvert.SerializeObject(regId));
            if (regId != null && regId.Any())
            {
                // Log that we are updating the record
                _logger.LogInformation($"Updating asset_capital_insurance_registration_otp_session for ObjId {bvn} and otp_type {(int)otpType}, otp {otp}");

                await con.ExecuteAsync(
          "UPDATE asset_capital_insurance_registration_otp_session SET createdon=sysdate(), status = 1,otp= @Otp, session = @Session,otp_type = @OtpType WHERE bvn = @bvn and user_type=@UserType",
          new { Session = session, bvn, OtpType = (int)otpType, Otp = otp, UserType = UserType });
                _logger.LogInformation("Update completed");
            }
            else
            {
                // Log that we are inserting a new record
                _logger.LogInformation($"Inserting new otp_session for ObjId {bvn}");
                // Insert new record using parameterized query
                await con.ExecuteAsync(
                    "INSERT INTO asset_capital_insurance_registration_otp_session (otp_type, session, otp, status, createdon,bvn,user_type) " +
                    "VALUES (@OtpType, @Session, @Otp, 1, sysdate(), @bvn,@UserType)",
                    new { OtpType = (int)otpType, Session = session, otp,bvn, UserType });
                _logger.LogInformation("insert completed ....");
            }
        }


        //not implemented
        public async Task<OtpSession> ValidateSessionOtp(OtpType otpType, string Session, IDbConnection con)
        {
            try
            {
                string sql = $@"select * from otp_session where otp_type= {(int)otpType} and status = 1 and session = @sess";
                var resp = await con.QueryAsync<OtpSession>(sql, new { sess = Session });
                _logger.LogInformation($"resp opt session " + Session);
                return resp.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return null;
            }
        }

        //this queries user_session table
        public async Task<UserSession> ValidateSessionOtp2(OtpType otpType, string Session, IDbConnection con)
        {
            try
            {
                string sql = $@"select * from user_session where otp_type= {(int)otpType} and status = 1 and session = @sess";
                var resp = await con.QueryAsync<UserSession>(sql, new { sess = Session });
                _logger.LogInformation($"resp opt session " + Session);
                return resp.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return null;
            }
        }

        public string MaskEmail(string Email)
        {
            try
            {
                if (string.IsNullOrEmpty(Email))
                    return Email;

                var emil = Email.Split('@');
                if (emil.Count() == 1)
                    return Email;

                return emil[0].Substring(0, 3) + "****@" + emil[1];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return Email;
            }
        }

        public string MaskPhone(string Phone)
        {
            try
            {
                if (string.IsNullOrEmpty(Phone))
                    return Phone;

                return "*****" + Phone.Substring(Phone.Length - 3);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return Phone;
            }
        }

        public async Task InsertLogs(long UserId, string Session, string Device, string Gps, string Action, IDbConnection con) => await con.ExecuteAsync($"insert into user_logs (userid, session, device, gps, action,createdon) values ({UserId},'{Session}',@dev,@gps,@act,sysdate())", new { dev = Device, gps = Gps, act = Action });

        public async Task<string> GetUserCredential(CredentialType credentialType, long UserId, IDbConnection con)
        {
            try
            {
                var cred = await con.QueryAsync<string>($"select credential from user_credentials where CredentialType = {(int)credentialType} and UserId = {UserId} and Status = 1");
                return cred.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return "";
            }
        }

        public async Task<string> GetAssetCapitalInsuranceUserCredential(CredentialType credentialType, long UserId,string UserType, IDbConnection con)
        {
            try
            {
                var cred = await con.QueryAsync<string>($"select credential from asset_capital_insurance_user_credentials where credential_type = {(int)credentialType} and user_id = {UserId} and Status = 1 and user_type='{UserType}'");
                return cred.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return "";
            }
        }

        public async Task<UsrCredential> GetUserCredentialForTrans(CredentialType credentialType, long UserId, IDbConnection con)
        {
            try
            {
                var cred = await con.QueryAsync<UsrCredential>($"select credential,temporarypin from user_credentials where CredentialType = {(int)credentialType} and UserId = {UserId} and Status = 1");
                return cred.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return null;
            }
        }

        public async Task SetUserCredential(CredentialType credentialType, long UserId, string Credential, IDbConnection con, bool PlainText)
        {
            try
            {
                _logger.LogInformation($"credentialType ${credentialType} Credential {Credential} PlainText {PlainText}");
                // await con.ExecuteAsync($"update user_credentials set status =0 where userid = {UserId} and CredentialType={(int)credentialType}");
                _logger.LogInformation("proceeding to insert pin ...");
                string sql = $@"insert into user_credentials (userid, credentialtype,status, createdon,credential)
                    values ({UserId},{(int)credentialType},1,sysdate(),@cred)";
                string pincred = null;
                if (PlainText)
                {
                    _logger.LogInformation(" proceeding to encript credentials " + Credential);
                    pincred = EncryptString(Credential);
                }
                else
                {
                    pincred = Credential;
                }
                // var pincred = PlainText ? EncryptString(Credential) : Credential;
                // _logger.LogInformation("pincred " + pincred);
                // _logger.LogInformation($"PlainText ? EncryptString(Credential) : Credential {(PlainText ? EncryptString(Credential) : Credential)}");
                var usrid = (await con.QueryAsync<long>("select distinct UserId from user_credentials where Userid=@userid and CredentialType=@CredentialType and Status=1", new { userid = UserId, CredentialType = (int)credentialType })).FirstOrDefault();
                if (usrid != 0)
                {
                    await con.ExecuteAsync("update user_credentials set credential=@cred where Userid=@userid and CredentialType=@CredentialType and Status=1", new { cred = pincred, userid = UserId, CredentialType = (int)credentialType });
                }
                else
                    await con.ExecuteAsync(sql, new { cred = pincred });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
            }
        }

        public async Task SetAssetCapitalInsuranceUserCredential(CredentialType credentialType, long UserId, string Credential, IDbConnection con, bool PlainText,string UserType)
        {
            try
            {
                _logger.LogInformation($"credentialType ${credentialType} Credential {Credential} PlainText {PlainText}");
                _logger.LogInformation("proceeding to insert pin ...");
                string sql = $@"insert into asset_capital_insurance_user_credentials(user_id, credential_type,status, createdon,credential,user_type)
                    values ({UserId},{(int)credentialType},1,sysdate(),@cred,'{UserType}')";
                string pincred = null;
                if (PlainText)
                {
                    _logger.LogInformation(" proceeding to encript credentials " + Credential);
                    pincred = EncryptString(Credential);
                }
                else
                {
                    pincred = Credential;
                }
                var usrid = (await con.QueryAsync<long>("select distinct user_id as UserId from asset_capital_insurance_user_credentials where user_id=@userid and credential_type=@CredentialType and Status=1 and user_type=@UserType", new { userid = UserId, CredentialType = (int)credentialType, UserType=UserType })).FirstOrDefault();
                if (usrid != 0)
                {
                    await con.ExecuteAsync("update asset_capital_insurance_user_credentials set credential=@cred where user_id=@userid and credential_type=@CredentialType and Status=1 and user_type=@UserType", new { cred = pincred, userid = UserId, CredentialType = (int)credentialType, UserType=UserType });
                }
                else
                    await con.ExecuteAsync(sql, new { cred = pincred });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
            }
        }

        public async Task<MobileDevice> GetActiveMobileDevice(long UserId, IDbConnection con)
        {
            try
            {
                var resp = await con.QueryAsync<MobileDevice>($"select * from mobiledevice where userid= {UserId} and status = 1");
                Console.WriteLine("resp ... " + JsonConvert.SerializeObject(resp.ToList()));
                return resp.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return null;
            }
        }
        public async Task<List<MobileDevice>> GetListOfActiveMobileDevice(long UserId, IDbConnection con)
        {
            try
            {
                var resp = await con.QueryAsync<MobileDevice>($"select * from mobiledevice where userid= {UserId} and status = 1");
                Console.WriteLine("resp ... " + JsonConvert.SerializeObject(resp.ToList()));
                // return resp.FirstOrDefault();
                return resp.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return null;
            }
        }

        public async Task<List<AssetCapitalInsuranceMobileDevice>> GetAssetCapitalInsuranceListOfActiveMobileDevice(long UserId,string UserType, IDbConnection con)
        {
            try
            {
                var resp = await con.QueryAsync<AssetCapitalInsuranceMobileDevice>($"select * from asset_capital_insurance_mobile_device where user_id= {UserId} and status = 1 and user_type='{UserType}'");
                Console.WriteLine("resp ... " + JsonConvert.SerializeObject(resp.ToList()));
                // return resp.FirstOrDefault();
                return resp.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return null;
            }
        }

        public async Task SetMobileDevice(string Username, string DeviceId, string DeviceName, int newStatus, IDbConnection con)
        {
            try
            {
                await con.ExecuteAsync($"update mobiledevice set status = 2 where username={Username}");
                string sql = $@"insert into mobiledevice (userid,presentlogindevice,status,devicename,createdon,username)
                    values ({Username},@dv,{newStatus},@dvname,sysdate(),username)";
                await con.ExecuteAsync(sql, new { dv = DeviceId.ToLower().Trim(), dvname = DeviceName, Username });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
            }
        }



        //original method
        public async Task SetMobileDevice(long UserId, string DeviceId, string DeviceName, int newStatus, IDbConnection con)
        {
            try
            {
                string devicesql = "SELECT device FROM mobiledevice WHERE device = @Device AND devicename = @DeviceName and UserId=@UserId";
                var parameters = new { Device = DeviceId, DeviceName = DeviceName, UserId = UserId };
                _logger.LogInformation("parameters " + parameters);

                var result = await con.QueryFirstOrDefaultAsync<string>(devicesql, parameters);
                _logger.LogInformation("parameters " + parameters);
                _logger.LogInformation("inserting  successfully ...." + result);
                if (!string.IsNullOrEmpty(result))  // In case the device already exists
                    return;

                string sql = @"INSERT INTO mobiledevice (userid, device, status, devicename, createdon)
                       VALUES (@UserId, @Device, @Status, @DeviceName, @CreatedOn)";
                _logger.LogInformation("device inserted and registered");
                _logger.LogInformation("inserting successfully ....");
                await con.ExecuteAsync(sql, new { UserId, Device = DeviceId.ToLower().Trim(), Status = newStatus, DeviceName, CreatedOn = DateTime.Now });
                _logger.LogInformation("inserted successfully ....");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
            }
        }

        public async Task SetAssetCapitalInsuranceMobileDevice(long UserId, string DeviceId, string DeviceName, int newStatus, IDbConnection con,string UserType)
        {
            try
            {
                string devicesql = "SELECT device FROM asset_capital_insurance_mobile_device WHERE device = @Device AND device_name = @DeviceName and user_id=@UserId and user_type=@UserType";
                var parameters = new { Device = DeviceId, DeviceName = DeviceName, UserId = UserId,UserType=@UserType};
                _logger.LogInformation("parameters " + parameters);

                var result = await con.QueryFirstOrDefaultAsync<string>(devicesql, parameters);
                _logger.LogInformation("parameters " + parameters);
                _logger.LogInformation("inserting  successfully ...." + result);
                if (!string.IsNullOrEmpty(result))  // In case the device already exists
                    return;

                string sql = @"INSERT INTO asset_capital_insurance_mobile_device (user_id, device, status, device_name, createdon,user_type)
                       VALUES (@UserId, @Device, @Status, @DeviceName, @CreatedOn,@UserType)";
                _logger.LogInformation("device inserted and registered");
                _logger.LogInformation("inserting successfully ....");
                await con.ExecuteAsync(sql, new { UserId, Device = DeviceId.ToLower().Trim(), Status = newStatus, DeviceName, CreatedOn = DateTime.Now,UserType=UserType });
                _logger.LogInformation("inserted successfully ....");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
            }
        }

        /*
          formal method
         public async Task SetMobileDevice(long UserId, string DeviceId, string DeviceName, int newStatus, IDbConnection con)
         {
             try
             {
                 string devicesql = "SELECT device FROM mobiledevice WHERE device = @Device AND devicename = @DeviceName and UserId=@userid";
                 var parameters = new { Device = DeviceId, DeviceName = DeviceName,userid=UserId };
                 _logger.LogInformation("parameters " + parameters);
                 var result = (await con.QueryFirstOrDefaultAsync<string>(devicesql, parameters)).ToList();
                 _logger.LogInformation("parameters " + parameters);
                 _logger.LogInformation("result " + (result.Count));
                 if (result.Any())  // incase the device already exists
                     return;
                // await con.ExecuteAsync($"update mobiledevice set status = 2 where userid={UserId}");
                 string sql = $@"insert into mobiledevice (userid,device,status,devicename,createdon)
                     values ({UserId},@dv,{newStatus},@dvname,sysdate())";
                 _logger.LogInformation("device inserted and registered ");
                 await con.ExecuteAsync(sql, new { dv = DeviceId.ToLower().Trim(), dvname = DeviceName });
             }
             catch (Exception ex)
             {
                 _logger.LogError(ex.Message + " " + ex.StackTrace);
             }
         }
         */

        public async Task<List<AccessBankList>> GetBanks(bool log = false)
        {
            try
            {
                var myData = new List<AccessBankList>();
                if (!_cache.TryGetValue(CacheKeys.Banks, out myData))
                {
                    // Key not in cache, so get data.
                    myData = await CallServiceAsync<List<AccessBankList>>(Method.GET, $"{appSetting.AccessUrl}AccessOutward/GetBankList", null, log);

                    // Set cache options.
                    var cacheEntryOptions = new MemoryCacheEntryOptions()
                        // Keep in cache for this time, reset time if accessed.
                        .SetSlidingExpiration(TimeSpan.FromHours(1));

                    // Save data in cache.
                    _cache.Set(CacheKeys.Banks, myData, cacheEntryOptions);
                }
               // _logger.LogInformation($"banklist mydata {myData}");
                _logger.LogInformation($"banks {JsonConvert.SerializeObject(myData)}");
                return myData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new List<AccessBankList>();
            }
        }

        public async Task<BalanceEnquiryResponse> GetAccountbyCustomerId(string CustomerId) => await CallServiceAsync<BalanceEnquiryResponse>(Method.GET, $"{appSetting.FinedgeUrl}api/Enquiry/GetAccountBalanceByCustomerId/{CustomerId}", null, true, new Dictionary<string, string>
                {
                    { "ClientKey", appSetting.FinedgeKey }
                });

        public async Task<BalanceEnquiryResponse> GetAccountDetailsbyAccountNumber(string AccountNumber)
        {
            try
            {
                var req = new EnquiryObj() { ClientKey = appSetting.FinedgeKey, Value = AccountNumber };
                Console.WriteLine("api call " + $"{appSetting.FinedgeUrl}api/enquiry/GetAccountBalanceByNuban");
                var resp = await CallServiceAsync<BalanceEnquiryResponse>(Method.POST, $"{appSetting.FinedgeUrl}api/enquiry/GetAccountBalanceByNuban", req, true);
                return resp;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new BalanceEnquiryResponse() { message = ex.Message };
            }
        }

        public async Task<BalanceEnquiryResponse> GetBalanceEnquirybyAccountNumber(string AccountNumber)
        {
            try
            {
                var req = new EnquiryObj() { ClientKey = appSetting.FinedgeKey, Value = AccountNumber };
                Console.WriteLine("api call " + $"{appSetting.FinedgeUrl}api/Enquiry/GetBalanceEnquirybyAccountNo");
                var resp = await CallServiceAsync<BalanceEnquiryResponse>(Method.POST, $"{appSetting.FinedgeUrl}api/Enquiry/GetBalanceEnquirybyAccountNo", req, true);
                return resp;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new BalanceEnquiryResponse() { message = ex.Message };
            }
        }

        public async Task<decimal> GetDailyLimit(long UserId, IDbConnection con, BeneficiaryType transType)
        {
            try
            {
                var myData = new List<TransLimit>();
                if (!_cache.TryGetValue(CacheKeys.Limits, out myData))
                {
                    // Key not in cache, so get data.
                    var myData1 = await con.QueryAsync<TransLimit>("SELECT Trans_Type TransType,limits FROM daily_limit");
                    myData = myData1.ToList();
                    // Set cache options.
                    var cacheEntryOptions = new MemoryCacheEntryOptions()
                        // Keep in cache for this time, reset time if accessed.
                        .SetSlidingExpiration(TimeSpan.FromHours(1));

                    // Save data in cache.
                    _cache.Set(CacheKeys.Limits, myData, cacheEntryOptions);
                }
                return myData.FirstOrDefault(x => x.TransType == (int)transType).Limits;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return 0;
            }
        }

        public async Task<decimal> GetTotalSpent(long UserId, IDbConnection con, BeneficiaryType transType)
        {
            try
            {
                string sql = $"select sum(amount) from {transType.ToString().ToLower()} where User_Id = {UserId} and Success = 1 and CreatedOn >'{DateTime.Now.ToString("yyyy-MM-dd")}'";
                //  _logger.LogInformation("Total " + sql);
                var resp = await con.QueryAsync<string>(sql);
                decimal result = 0;
                decimal.TryParse(resp.FirstOrDefault(), out result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return 0;
            }
        }

        public async Task<List<decimal>> SuggestedAmount(long UserId, IDbConnection con, BeneficiaryType transType)
        {
            var result = new List<decimal>
            {
                1000
            };
            return result;
        }

        public async Task<UserLimit> GetUserLimits(long UserId, IDbConnection con, BeneficiaryType transType) => new UserLimit()
        {
            DailyLimit = await GetDailyLimit(UserId, con, transType),
            DailyTotalSpent = await GetTotalSpent(UserId, con, transType),
            SuggestedAmount = await SuggestedAmount(UserId, con, transType)
        };

        public async Task<AirtimeBillsLimit> GetAirtimeBillsLimit(long UserId, IDbConnection con) => new AirtimeBillsLimit()
        {
            AirtimeLimit = await GetUserLimits(UserId, con, BeneficiaryType.Airtime),
            BillsLimit = await GetUserLimits(UserId, con, BeneficiaryType.Bills)
        };

        public async Task<GetCustomerResponse> GetCustomer(string CustomerId) => await CallServiceAsync<GetCustomerResponse>(Method.POST, $"{appSetting.FinedgeUrl}api/customer/GetCustomerbyCustomerId", new GetCustomerRequest() { clientKey = appSetting.FinedgeKey, customerId = CustomerId }, true);

        public async Task<GetCustomerInfobyCustomerId> GetCustomer2(string CustomerId) => await CallServiceAsync<GetCustomerInfobyCustomerId>(Method.POST, $"{appSetting.FinedgeUrl}api/enquiry/GetCustomerbyCustId", new GetCustomerRequest() { clientKey = appSetting.FinedgeKey, customerId = CustomerId }, true);

        public async Task<FinedgeSearchBvn> GetCustomerbyAccountNo(string AccountNo)
        {
            try
            {
                FinedgeSearchBvn myData;

                if (!_cache.TryGetValue("Accounts_" + AccountNo, out myData))
                {
                    // Key not in cache, so get data.
                    myData = await CallServiceAsync<FinedgeSearchBvn>(Method.GET, $"{appSetting.FinedgeUrl}api/Enquiry/SearchCustomerbyAccountNumber/{AccountNo}", null, true, new Dictionary<string, string>
                {
                    { "ClientKey", appSetting.FinedgeKey }
                });

                    if (myData == null || !myData.success)
                        return new FinedgeSearchBvn();
                    // Set cache options.
                    var cacheEntryOptions = new MemoryCacheEntryOptions()
                        // Keep in cache for this time, reset time if accessed.
                        .SetSlidingExpiration(TimeSpan.FromMinutes(1));

                    // Save data in cache.
                    _cache.Set("Accounts_" + AccountNo, myData, cacheEntryOptions);

                }
                return myData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new FinedgeSearchBvn() { message = ex.Message };
            }
        }

        public async Task<ValidateAccountResponse> ValidateNumberOnly(string DestinationAccount, string DestinationBankCode)
        {
            try
            {
                _logger.LogInformation("Validating accountnumber " + DestinationAccount + " code " + DestinationBankCode);
                var result = new ValidateAccountResponse();
                if (DestinationBankCode == appSetting.TrustBancBankCode)
                {
                    var getAcct = await GetCustomerbyAccountNo(DestinationAccount);
                    if (!getAcct.success)
                    {
                        result.Response = EnumResponse.InvalidAccount;
                        return result;
                    }
                    result.Success = true;
                    result.Response = EnumResponse.Successful;
                    result.AccountName = getAcct.result.displayName;
                    return result;
                }

                var req = new NameEnquiryRequest() { accountNumber = DestinationAccount, bankCode = DestinationBankCode };
                //$"{appSetting.AccessUrl}AccessOutward/NameEnquiry"
                var nameEnq = await CallServiceAsync<NameEnquiryResponse>(Method.POST, $"{appSetting.AccessLiveNameEnquiry}AccessOutward/NameEnquiry", req, true, new Dictionary<string, string>
                {
                    { "ClientKey", appSetting.AccessKey }
                });
                if (nameEnq == null || !nameEnq.success)
                {
                    result.Response = EnumResponse.InvalidAccount;
                    return result;
                }
                Console.WriteLine("name Enq " + JsonConvert.SerializeObject(nameEnq));
                result.Success = true;
                result.Response = EnumResponse.Successful;
                result.AccountName = nameEnq.accountName;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new ValidateAccountResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<string> GetNinToken()
        {
            try
            {
                string myData = string.Empty;
                if (!_cache.TryGetValue(CacheKeys.NinToken, out myData))
                {
                    Console.WriteLine($"token endpoint {appSetting.NintokenEndpointUrl}");
                    var client = new RestClient($"{appSetting.NintokenEndpointUrl}");
                    client.Timeout = -1;
                    var request = new RestRequest(Method.POST);
                    request.AddHeader("Content-Type", "application/x-www-form-urlencoded");
                    request.AddParameter("username", appSetting.NinUserNameEndPoint);
                    request.AddParameter("password", appSetting.NinPasswordEndPoint);
                    ServicePointManager
            .ServerCertificateValidationCallback +=
            (sender, cert, chain, sslPolicyErrors) => true;
                    IRestResponse response = await client.ExecuteAsync(request);
                    Console.WriteLine($"response {response.Content},{response.StatusCode}");
                    if (response.StatusCode != HttpStatusCode.OK)
                        return string.Empty;
                    var model = JsonConvert.DeserializeObject<NinTokenResponse>(response.Content);
                    Console.WriteLine($"model {model.message} access_token {model.accessToken}");
                    if (string.IsNullOrEmpty(model.accessToken))
                        return string.Empty;
                    myData = model.accessToken;
                    Console.WriteLine($"myData {myData}");
                    var cacheEntryOptions = new MemoryCacheEntryOptions()
                          // Keep in cache for this time, reset time if accessed.
                          .SetSlidingExpiration(TimeSpan.FromMinutes(1));

                    // Save data in cache.
                    _cache.Set(CacheKeys.NinToken, myData, cacheEntryOptions);
                }
                return myData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return string.Empty;
            }
        }

        public string GenerateHmac(string message, string secretKey, bool toBase64, HmacType hmacType = HmacType.Sha512)
        {
            using var hasher = new HmacAlgorithm(hmacType);
            var hash = hasher.ComputeHash(message, secretKey);

            if (toBase64)
                return Convert.ToBase64String(hash);

            return BitConverter.ToString(hash).Replace("-", string.Empty);
        }

        public async Task SendOtp(OtpType otpType, string Otp, string number)
        {
            try
            {
                if (appSetting.DemoOtp == "y") return;
                string msg = otpMessage.UnlockDevice;

                if (otpType == OtpType.PasswordReset)
                    msg = otpMessage.PasswordReset;

                if (otpType == OtpType.UnlockDevice)
                    msg = otpMessage.UnlockDevice;

                if (otpType == OtpType.UnlockProfile)
                    msg = otpMessage.UnlockProfile;

                msg = msg.Replace("$otp", Otp);

                string msgref = DateTime.Now.ToString("ddMMyyyyHHmmss") + "_" + new Random().Next(11111, 99999);
                var request = new SendSmsRequest()
                {
                    ClientKey = appSetting.FinedgeKey,
                    Message = msg,
                    PhoneNumber = number,
                    SmsReference = msgref
                };
                await SendSMS(request);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
            }
        }

        public async Task<object> CallServiceAsync<T>(AppSettings appSetting, Method method, string url, object requestobject, bool log = false, List<HeaderApi> header = null) where T : class
        {
            try
            {
                var client = new RestClient(url);
                var request = new RestRequest(method);
                if (method == Method.POST)
                {
                    request.AddParameter("application/json", JsonConvert.SerializeObject(requestobject), ParameterType.RequestBody);
                    request.AddHeader("Content-Type", "application/json");
                }

                if (header != null)
                    foreach (var item in header)
                        request.AddHeader(item.Header, item.Value);
                client.Timeout = -1;
                ServicePointManager
                .ServerCertificateValidationCallback +=
                (sender, cert, chain, sslPolicyErrors) => true;

                _logger.LogInformation("API Call - " + url);
                IRestResponse response = await client.ExecuteAsync(request);
                Console.WriteLine("API Response - " + JsonConvert.SerializeObject(response.Content));
                if (log)
                {
                    // _logger.LogInformation("API Call - " + url);
                    if (requestobject != null)
                        _logger.LogInformation("API Request - " + JsonConvert.SerializeObject(requestobject));
                    _logger.LogInformation("API Response - " + JsonConvert.SerializeObject(response.Content));
                }
                var model = JsonConvert.DeserializeObject<T>(response.Content);
                return model;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new object();
            }
        }

        public async Task<FinedgeAccountProductName> GetAccountWithProductName(string AccountNo)
        {
            try
            {
                FinedgeAccountProductName myData;

                if (!_cache.TryGetValue("Accounts_" + AccountNo, out myData))
                {
                    // Key not in cache, so get data.
                    myData = await CallServiceAsync<FinedgeAccountProductName>(Method.GET, $"{appSetting.FinedgeUrl}api/Enquiry/GetAccountBalanceAndAccountType/{AccountNo}", null, true, new Dictionary<string, string>
                {
                    { "ClientKey", appSetting.FinedgeKey }
                });
                    if (myData == null)
                        return new FinedgeAccountProductName();
                    // Set cache options.
                    var cacheEntryOptions = new MemoryCacheEntryOptions()
                        // Keep in cache for this time, reset time if accessed.
                        .SetSlidingExpiration(TimeSpan.FromMinutes(1));
                    // Save data in cache.
                    _cache.Set("Accounts_" + AccountNo, myData, cacheEntryOptions);
                }
                return myData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new FinedgeAccountProductName() { message = ex.Message };
            }
        }

        public async Task<GenericResponse> ValidateNin(string username, string nin, IDbConnection con, string inputbvn = null)
        {
            var requestobject = new
            {
                number_nin = nin
            };
            //get token
            var loginobj = new
            {
                username = "itservices@trustbancgroup.com",
                password = "trust@banc"
            };
            _logger.LogInformation("about to  login for nin " + JsonConvert.SerializeObject(loginobj));
            string Loginresponse = await CallServiceAsyncToString(Method.POST, "http://localhost:8080/MFB_USSD/api/v1/user/login", loginobj, true);
            JObject loginjson = (JObject)JToken.Parse(Loginresponse);
            string accessToken = loginjson.ContainsKey("response") ? loginjson["response"]["accessToken"].ToString() : "";
            if (string.IsNullOrEmpty(accessToken))
            {
                return new GenericResponse() { Response = EnumResponse.NotSuccessful };
            }
            Dictionary<string, string> header = new Dictionary<string, string>
                        {
                            { "Authorization", "Bearer " + accessToken }
                        };
            //  Console.WriteLine("CustNinUrlFromUssd ");
            _logger.LogInformation("calling nin endpoint .....");
            string response = await CallServiceAsyncToString(Method.POST, "http://localhost:8080/MFB_USSD/api/v1/verification/nin", requestobject, true, header);
            var settings = new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Ignore
            };
            NinValidationResponse ninValidation = JsonConvert.DeserializeObject<NinValidationResponse>(response, settings);
            var usr = await GetUserbyUsername(username, con);
            if (!(ninValidation.response_code == "00" && ninValidation.ninData.residence_address.ToLower().Contains("suspended")))
            {
                string ninName = ninValidation.ninData.firstname + " " + ninValidation.ninData.surname + " " + ninValidation.ninData.middlename;
                string ninDateOfBirth = ninValidation.ninData.birthdate;
                string CustFirstName = usr.Firstname;
                string CustLastName = usr.LastName;
                string custbvn = inputbvn == null ? usr.Bvn : inputbvn;
                var result = Task.Run(async () =>
                {
                    string bvnurl = "http://localhost:8080/MFB_USSD/api/v1/verification/bvn";
                    var bvnobj = new
                    {
                        bvn = custbvn
                    };
                    string bvnresponse = await CallServiceAsyncToString(Method.POST, bvnurl, bvnobj, true, header);
                    CustomerBvn customerBvn = JsonConvert.DeserializeObject<CustomerBvn>(bvnresponse, settings);
                    return customerBvn;
                });
                CustomerBvn customerBvn = await result;
                _logger.LogInformation("customerBvn " + JsonConvert.SerializeObject(customerBvn));
                string CustBirthDate = customerBvn.data.dateOfBirth;
                bool NameCheck = (ninName.ToLower().Contains(CustFirstName.ToLower()) || ninName.ToLower().Contains(CustLastName.ToLower()));
                string dateString1 = CustBirthDate;
                string dateString2 = ninDateOfBirth;
                string[] formats = { "dd-MMM-yyyy", "dd-MM-yyyy" };
                if (NameCheck)
                {
                    if (TryParseDate(dateString1, formats, out DateTime date1) && TryParseDate(dateString2, formats, out DateTime date2))
                    {
                        if (date1.Date == date2.Date)
                        {
                            // _logger.LogInformation("The dates are equal.");
                            return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
                        }
                        else
                        {
                            // Console.WriteLine("The dates are not equal.");
                            return new GenericResponse() { Success = false, Response = EnumResponse.DateMismatch };
                        }
                    }
                }
                else
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.NameMisMatch };
                }
                // return new GenericResponse() { Success=true, Response = EnumResponse.ValidNin };
                return new GenericResponse() { Success = false, Response = EnumResponse.InValidNin };
            }
            return null;
        }
        static bool TryParseDate(string dateString, string[] formats, out DateTime date)
        {
            return DateTime.TryParseExact(dateString, formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out date);
        }



        public async Task<GenericResponse> ValidateNin(string nin, IDbConnection con, string inputbvn)
        {
            var requestobject = new
            {
                number_nin = nin
            };
            //get token
            var loginobj = new
            {
                username = "itservices@trustbancgroup.com",
                password = "trust@banc"
            };
            _logger.LogInformation("about to  login for nin " + JsonConvert.SerializeObject(loginobj));
            string Loginresponse = await CallServiceAsyncToString(Method.POST, "http://localhost:8080/MFB_USSD/api/v1/user/login", loginobj, true);
            JObject loginjson = (JObject)JToken.Parse(Loginresponse);
            string accessToken = loginjson.ContainsKey("response") ? loginjson["response"]["accessToken"].ToString() : "";
            _logger.LogInformation("accessToken " + accessToken);
            if (string.IsNullOrEmpty(accessToken))
            {
                return new GenericResponse() { Response = EnumResponse.NotSuccessful };
            }
            Dictionary<string, string> header = new Dictionary<string, string>
                        {
                            { "Authorization", "Bearer " + accessToken }
                        };
            //  Console.WriteLine("CustNinUrlFromUssd ");
            _logger.LogInformation("calling nin endpoint .....");
            string response = await CallServiceAsyncToString(Method.POST, "http://localhost:8080/MFB_USSD/api/v1/verification/nin", requestobject, true, header);
            _logger.LogInformation("response " + response);
            var settings = new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Ignore
            };
            NinValidationResponse ninValidation = JsonConvert.DeserializeObject<NinValidationResponse>(response, settings);
            //var usr = await GetUserbyUsername(username, con);
            if (!(ninValidation.response_code == "00" && ninValidation.ninData.residence_address.ToLower().Contains("suspended")))
            {
                string ninName = ninValidation.ninData.firstname + " " + ninValidation.ninData.surname + " " + ninValidation.ninData.middlename;
                string ninDateOfBirth = ninValidation.ninData.birthdate;
                string custbvn = inputbvn;
                var result = Task.Run(async () =>
                {
                    string bvnurl = "http://localhost:8080/MFB_USSD/api/v1/verification/bvn";
                    var bvnobj = new
                    {
                        bvn = custbvn
                    };
                    string bvnresponse = await CallServiceAsyncToString(Method.POST, bvnurl, bvnobj, true, header);
                    CustomerBvn customerBvn = JsonConvert.DeserializeObject<CustomerBvn>(bvnresponse, settings);
                    return customerBvn;
                });
                _logger.LogInformation("awaiting task");
                CustomerBvn customerBvn = await result;
                string CustFirstName = customerBvn.data.firstName;
                string CustLastName = customerBvn.data.lastName;
                _logger.LogInformation("customerBvn " + JsonConvert.SerializeObject(customerBvn));
                string CustBirthDate = customerBvn.data.dateOfBirth;
                string bvnfirstName = customerBvn.data.firstName;
                string bvnlastName = customerBvn.data.lastName;
                string bvnmiddlename = customerBvn.data.middleName;
                bool AtLeastTwoNamesPresent(string ninName, string firstname, string lastname, string middlename)
                {
                    // Logic to check if at least two names are present
                    List<string> foundNames = new List<string>();
                    if (ninName.Contains(firstname, StringComparison.OrdinalIgnoreCase))
                    {
                        foundNames.Add(firstname.ToLower());
                    }

                    if (ninName.Contains(lastname, StringComparison.OrdinalIgnoreCase))
                    {
                        foundNames.Add(lastname.ToLower());
                    }

                    if (ninName.Contains(middlename, StringComparison.OrdinalIgnoreCase))
                    {
                        foundNames.Add(middlename.ToLower());
                    }

                    // Check if at least two unique names are present
                    int uniqueCount = new HashSet<string>(foundNames).Count;
                    /*
                    if (uniqueCount >= 2)
                    {
                        Console.WriteLine("At least two of the names are present in ninName.");
                    }
                    else
                    {
                        Console.WriteLine("Less than two of the names are present in ninName.");
                    }
                    */
                    return uniqueCount >= 2;
                }
                // bool NameCheck = (ninName.ToLower().Contains(CustFirstName.ToLower()) || ninName.ToLower().Contains(CustLastName.ToLower()));
                bool NameCheck = AtLeastTwoNamesPresent(ninName, bvnfirstName, bvnlastName, bvnmiddlename);
                string dateString1 = CustBirthDate;
                string dateString2 = ninDateOfBirth;
                string[] formats = { "dd-MMM-yyyy", "dd-MM-yyyy" };
                if (NameCheck)
                {
                    if (TryParseDate(dateString1, formats, out DateTime date1) && TryParseDate(dateString2, formats, out DateTime date2))
                    {
                        if (date1.Date == date2.Date)
                        {
                            // _logger.LogInformation("The dates are equal.");
                            return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
                        }
                        else
                        {
                            // Console.WriteLine("The dates are not equal.");
                            return new GenericResponse() { Success = false, Response = EnumResponse.DateMismatch };
                        }
                    }
                }
                else
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.NameMisMatch };
                }
                return new GenericResponse() { Success = false, Response = EnumResponse.InValidNin };
            }
            return new GenericResponse() { Success = false, Response = EnumResponse.InValidNin };
        }

        public async Task<GenericResponse> GetCustomerAllAccountBalance(string customerId)
        {
            try
            {
                string balanceResponse = await CallServiceAsyncToString(Method.GET, appSetting.FinedgeUrl + "api/Enquiry/GetCustomerAllAccountBalance/" + customerId, null, true);
                GenericResponse2 genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(balanceResponse);
                Console.WriteLine("genericResponse2.data "
                    + JsonConvert.SerializeObject(genericResponse2.data));
                if (balanceResponse == null || balanceResponse == "")
                {
                    return new GenericResponse()
                    {
                        Success = false,
                        Response = EnumResponse.NoRecordFound
                    };
                }
                AccountBalanceResponses accountBalanceResponses =
                    JsonConvert.DeserializeObject<AccountBalanceResponses>
                    (JsonConvert.SerializeObject(genericResponse2.data));
                if (accountBalanceResponses.Status.Equals("Successful", StringComparison.OrdinalIgnoreCase))
                {
                    return new PrimeAdminResponse()
                    {
                        Success = true,
                        Response = EnumResponse.Successful,
                        Data = accountBalanceResponses
                    };
                }
                return new GenericResponse() { Success = false, Response = EnumResponse.NoRecordFound };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task<GenericResponse> UpgradeAccountNo(UpgradeAccountNo upgradeAccountNo)
        {
            try
            {
                string upgradeResponse = await CallServiceAsyncToString(Method.POST, appSetting.FinedgeUrl + "api/Customer/UpgradeAccountTier", upgradeAccountNo, true);
                _logger.LogInformation("upgradeResponse " + upgradeResponse);
                AccountUpgradeResponseRoot accountUpgradeResponseRoot = JsonConvert.DeserializeObject<AccountUpgradeResponseRoot>(upgradeResponse);
               // GenericResponse2 genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(upgradeResponse);
                /*
                _logger.LogInformation("genericResponse2.data "
                    + JsonConvert.SerializeObject(genericResponse2.data));
                */
                _logger.LogInformation("accountUpgradeResponseRoot "
                    + JsonConvert.SerializeObject(accountUpgradeResponseRoot));
                if (upgradeResponse == null || upgradeResponse == "")
                {
                    return new GenericResponse()
                    {
                        Success = false,
                        Response = EnumResponse.NoRecordFound
                    };
                }
                /*
                UpgradeAccountNoResponse upgradeAccountNoResponse =
                    JsonConvert.DeserializeObject<UpgradeAccountNoResponse>
                    (JsonConvert.SerializeObject(genericResponse2.data));
                */
                if (accountUpgradeResponseRoot.status.Equals("Successful", StringComparison.OrdinalIgnoreCase))
                {
                    return new PrimeAdminResponse()
                    {
                        Success = true,
                        Response = EnumResponse.Successful,
                        Data = accountUpgradeResponseRoot
                    };
                }
                return new GenericResponse() { Success = false, Response = EnumResponse.NoRecordFound };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse() { Response = EnumResponse.SystemError, Message = ex.Message };
            }
        }

        public async Task SendOtp4(OtpType otpType, string Otp, string number, ISmsBLService smsBLService, string type, string email = null)
        {
            try
            {
                //if (appSetting.DemoOtp == "y") return;
                _logger.LogInformation("sending otp " + Otp);
                string msg = null;
                string customerName = null; ;
                if (number.Contains("_"))
                {
                    number = number.Split('_')[0];
                    customerName = number.Split('_')[1];
                }
                _logger.LogInformation("number " + number + " customerName " + customerName);
                _logger.LogInformation("sending otp number ....." + number);
                number = CustomerServiceNotFromBvnService.ReplaceFirstDigit(number, "234");
                _logger.LogInformation("number " + number);
                if (otpType == OtpType.PasswordReset)
                    msg = otpMessage.PasswordReset;

                if (otpType == OtpType.UnlockDevice)
                    msg = otpMessage.UnlockDevice;

                if (otpType == OtpType.UnlockProfile)
                    msg = otpMessage.UnlockProfile;

                if (otpType == OtpType.Registration)
                    msg = otpMessage.Registration;

                if (otpType == OtpType.Confirmation)
                    msg = otpMessage.Confirmation;

                if (otpType == OtpType.PinResetOrChange)
                    msg = otpMessage.PinResetOrChange;

                if (otpType == OtpType.RetrieveUsername)
                    msg = otpMessage.RetrieveUsername;

                msg = msg.Replace("$otp", Otp);

                string msgref = DateTime.Now.ToString("ddMMyyyyHHmmss") + "_" + new Random().Next(11111, 99999);
                var request = new SendSmsRequest()
                {
                    ClientKey = appSetting.FinedgeKey,
                    Message = msg,
                    PhoneNumber = number,
                    SmsReference = msgref
                };
                _logger.LogInformation("SmsUrl " + appSetting.SmsUrl + " ");
                new Thread(async () =>
                {
                    GenericResponse response = await smsBLService.SendSmsNotificationToCustomer("Pin", number, $@"{msg}"
                    , type, appSetting.SmsUrl);
                    _logger.LogInformation("response.Message " + response.Message);
                    SendMailObject sendMailObject = new SendMailObject();
                    number = number.Substring(3);
                    _logger.LogInformation("number in thread " + number);
                    if (email != null)
                    {
                        //var Email = (await con.QueryAsync<string>("SELECT email FROM customerdatanotfrombvn where PhoneNumber=@PhoneNumber", new { PhoneNumber = "0" + number })).FirstOrDefault();
                        _logger.LogInformation("sending otp email " + email);
                        sendMailObject.Email = email;
                        sendMailObject.Html = $@"
                                                <p>Dear Customer {(!string.IsNullOrEmpty(customerName)?customerName:"")}</p>,
                                                <p>Thank you for choosing TrustBanc! To ensure the security of your account, please find your Pin below.<br/>This code is required to complete your transaction or account verification.</p>
                                                <p>Your Temporary pin is: {Otp}</p>
                                                <p>Please do not share this code with anyone for your safety.</p>
                                                <p>If you did not request this Pin, kindly contact us immediately at [TrustBanc customer support email/phone number].</p>
                                                <p>Thank you for trusting TrustBanc.</p>
                                                <p>The TrustBanc Team</p>
                                                 <p>Call us at:07004446147</p>
                                                <p><a href='https://trustbancmfb.com/'>Click to vist website</a></p>
                                                ";
                        sendMailObject.Subject = "TrustBanc Mobile Banking Pin Reset Request Approval";
                        _logger.LogInformation("sending otp email ....." + email);
                        this.SendMail(sendMailObject, null);
                    }
                }).Start();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
            }
        }

        public async Task SendOtp3(OtpType otpType, string Otp, string number, ISmsBLService smsBLService, string type, string email = null)
        {
            try
            {
                //if (appSetting.DemoOtp == "y") return;
                _logger.LogInformation("sending otp " + Otp);
                string msg = null;
                _logger.LogInformation("sending otp number ....." + number);
                number = CustomerServiceNotFromBvnService.ReplaceFirstDigit(number, "234");
                _logger.LogInformation("number " + number);
                if (otpType == OtpType.PasswordReset)
                    msg = otpMessage.PasswordReset;

                if (otpType == OtpType.UnlockDevice)
                    msg = otpMessage.UnlockDevice;

                if (otpType == OtpType.UnlockProfile)
                    msg = otpMessage.UnlockProfile;

                if (otpType == OtpType.Registration)
                    msg = otpMessage.Registration;

                if (otpType == OtpType.Confirmation)
                    msg = otpMessage.Confirmation;

                if (otpType == OtpType.PinResetOrChange)
                    msg = otpMessage.PinResetOrChange;

                if (otpType == OtpType.RetrieveUsername)
                    msg = otpMessage.RetrieveUsername;

                msg = msg.Replace("$otp", Otp);

                string msgref = DateTime.Now.ToString("ddMMyyyyHHmmss") + "_" + new Random().Next(11111, 99999);
                var request = new SendSmsRequest()
                {
                    ClientKey = appSetting.FinedgeKey,
                    Message = msg,
                    PhoneNumber = number,
                    SmsReference = msgref
                };
                _logger.LogInformation("SmsUrl " + appSetting.SmsUrl + " ");
                new Thread(async () =>
                {
                    GenericResponse response = await smsBLService.SendSmsNotificationToCustomer("Otp", number, $@"{msg}"
                    , type, appSetting.SmsUrl);
                    _logger.LogInformation("response.Message " + response.Message);
                    SendMailObject sendMailObject = new SendMailObject();
                    number = number.Substring(3);
                    _logger.LogInformation("number in thread " + number);
                    if (email != null)
                    {
                        //var Email = (await con.QueryAsync<string>("SELECT email FROM customerdatanotfrombvn where PhoneNumber=@PhoneNumber", new { PhoneNumber = "0" + number })).FirstOrDefault();
                        _logger.LogInformation("sending otp email " + email);
                        sendMailObject.Email = email;
                        sendMailObject.Html = $@"
                                                <p>Dear Customer -{email}</p>,
                                                
                                                <p>Thank you for choosing TrustBanc! To ensure the security of your account, please find your One-Time Password (OTP) below. This code is required to complete your transaction or account verification.
                                                 </p>
                                                <p>Your OTP is: {Otp}</p>
                                                <p>This OTP is valid for the next 3 minutes. Please do not share this code with anyone for your safety.
                                                If you did not request this OTP, kindly contact us immediately at [TrustBanc customer support email/phone number].
                                                </p>
                                                <p>
                                                Thank you for trusting TrustBanc.
                                                </p>
                                                <p>
                                                The TrustBanc Team
                                                </p>
                                                <a href='https://trustbancmfb.com/'>Click to vist website</a>
                                                ";
                       // sendMailObject.Html = $"Dear Customer,<br/>kindly find your otp {Otp} for this {type}.<br/>Please if you are not the one.Thank you for banking with us.";
                        sendMailObject.Subject = "TrustBanc OTP/Pin Code";
                        _logger.LogInformation("sending otp email ....." + email);
                        this.SendMail(sendMailObject, null);
                    }
                }).Start();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
            }
        }

        public string GeneratePin() => appSetting.DemoOtp == "y" ? "123456" : new Random().Next(1111, 9999).ToString();
        public async Task<string> CheckIfUserIdExistInTransfer(string username, IDbConnection connection)
        {
            // Fetch user details
            var usr = await this.GetUserbyUsername(username, connection);
            if (usr == null)
            {
                return null; // Return null if user does not exist
            }
            // Query for User_Id in the transfer table
            var userId = (await connection.QueryAsync<int?>(
                "SELECT User_Id FROM transfer WHERE User_Id = @userid",
                new { userid = usr.Id }
            )).FirstOrDefault();
            // Check and return the result
            return userId.HasValue ? userId.Value.ToString() : usr.Id.ToString();
        }

        public async Task<string> GenerateUnitID(int length)
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

        public Task InsertOtpForAssetCapitalInsuranceRegistration(OtpType otpType, string UserType, int registrationid, string session, string otp, IDbConnection con)
        {
            throw new NotImplementedException();
        }
    }

    public class HmacAlgorithm : IDisposable
    {
        private readonly HMAC _hasher;

        public HmacAlgorithm(HmacType hmacType) => _hasher = Create(hmacType);

        public byte[] ComputeHash(string message, string key)
        {
            _hasher.Key = Encoding.UTF8.GetBytes(key);

            var messageBytes = Encoding.UTF8.GetBytes(message);
            return _hasher.ComputeHash(messageBytes);
        }

        private static HMAC Create(HmacType hmacType)
        {
            return hmacType switch
            {
                HmacType.Md5 => new HMACMD5(),
                HmacType.Sha1 => new HMACSHA1(),
                HmacType.Sha256 => new HMACSHA256(),
                HmacType.Sha384 => new HMACSHA384(),
                HmacType.Sha512 => new HMACSHA512(),
                _ => throw new ArgumentException(nameof(hmacType)),
            };
        }
        public void Dispose() => _hasher.Dispose();
    }
}



