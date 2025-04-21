using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Retailbanking.BL.IServices;
using Retailbanking.BL.templates;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.Services
{
    public class RedemptionService : IRedemptionService
    {
        private readonly IAsynEmailSenderWrapper _asynEmailSenderWrapper;
        private readonly ILogger<IRedemptionService> _logger;
        private readonly IGenericAssetCapitalInsuranceCustomerService _genericAssetCapitalInsuranceCustomerService;
        private readonly AssetSimplexConfig _settings;
        private readonly DapperContext _context;
        private readonly IFileService _fileService;
        private readonly ISmsBLService _smsBLService;
        private readonly IRedisStorageService _redisStorageService;
        private readonly IGeneric _genServ;
        private readonly AppSettings _appSettings;
        private readonly SimplexConfig _simplexSettings;
        private readonly TemplateService _templateService;

        public RedemptionService(IAsynEmailSenderWrapper asynEmailSenderWrapper, ILogger<IRedemptionService> logger, IGenericAssetCapitalInsuranceCustomerService customerServ, IOptions<AssetSimplexConfig>  settings, DapperContext context, IFileService fileService, ISmsBLService smsBLService, IRedisStorageService redisStorageService, IGeneric genServ, IOptions<AppSettings>  appSettings, IOptions<SimplexConfig>  simplexSettings, TemplateService templateService)
        {
            _asynEmailSenderWrapper = asynEmailSenderWrapper;
            _logger = logger;
            _genericAssetCapitalInsuranceCustomerService = customerServ;
            _settings = settings.Value;
            _context = context;
            _fileService = fileService;
            _smsBLService = smsBLService;
            _redisStorageService = redisStorageService;
            _genServ = genServ;
            _appSettings = appSettings.Value;
            _simplexSettings = simplexSettings.Value;
            _templateService = templateService;
        }

        public async Task<GenericResponse2> LiquidationServiceRequest(string Session,string BankName, string RedemptionAccount, decimal Amount, string UserName, string UserType,string CustomerName)
        {
            try
            {
                IDbConnection con = _context.CreateConnection();
                var g = _genericAssetCapitalInsuranceCustomerService.ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Message = g.Message };
                }
                if (!await _genServ.ValidateSessionForAssetCapitalInsurance(Session, UserType, con))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(UserName, UserType, con);
                if (usr == null)
                {
                    return new GenericResponse2()
                    {
                        Success = false,
                        Response = EnumResponse.UserNotFound,
                        Message = "user not found"
                    };
                }
                SendMailObject sendMailObject = new SendMailObject();
               // sendMailObject.Email = customerDataNotFromBvn != null ? customerDataNotFromBvn.email : usr.email;
                sendMailObject.Subject = _appSettings.AssetMailSubject + " Liquidation";
                CultureInfo nigerianCulture = new CultureInfo("en-NG");
                var data = new
                {
                    title = "Investment Liquidation",
                    firstname = FirstLetterUppercaseMaker.CapitalizeFirstLetter(usr.first_name),
                    lastname = FirstLetterUppercaseMaker.CapitalizeFirstLetter(usr.last_name),
                    year = DateTime.Now.Year,
                    date=DateTime.Now.ToString(),
                    Amount = Amount,
                    AccountNumber = RedemptionAccount,
                    BankName = BankName,
                    CustomerName=CustomerName
                };
                string filepath = Path.Combine(_settings.PartialViews, "liquidationtemplate.html");
               // Console.WriteLine("filepath " + filepath);
                _logger.LogInformation("filepath " + filepath);
                AssetCapitalInsuranceCustomerDataNotFromBvn customerDataNotFromBvn =
                    await CustomerServiceNotFromBvnService.getAssetCapitalInsuranceCustomerRelationService(con, (int)usr.id,UserType);
                string htmlContent = _templateService.RenderScribanTemplate(filepath, data);
                _logger.LogInformation("mail sending");
                sendMailObject.Html = htmlContent;
                _asynEmailSenderWrapper.SendmailAsnc(_genServ,sendMailObject,htmlContent,_settings,_appSettings,usr,customerDataNotFromBvn);
                return new GenericResponse2() { Response = EnumResponse.Successful,Success=true,Message="sent successfully" };
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Messsage "+ex.Message);
                return new GenericResponse2() { Response = EnumResponse.NotSuccessful, Message = ex.Message };
            }
           // return new GenericResponse2() { Response = EnumResponse.NotSuccessful};
        }
    }
}

