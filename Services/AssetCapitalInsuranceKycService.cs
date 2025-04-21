using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlX.XDevAPI;
using Newtonsoft.Json;
using RestSharp;
using Retailbanking.BL.IServices;
using Retailbanking.BL.utils;
using Retailbanking.Common.CustomObj;
using Retailbanking.Common.DbObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;


namespace Retailbanking.BL.Services
{
    public class AssetCapitalInsuranceKycService : IAssetCapitalInsuranceKycService
    {
        private readonly ILogger<IAssetCapitalInsuranceKycService> _logger;
        private readonly AssetSimplexConfig _settings;
        private readonly DapperContext _context;
        private readonly IFileService _fileService;
        private readonly ISmsBLService _smsBLService;
        private readonly IUserCacheService _userCacheService;
        private readonly IRedisStorageService _redisStorageService;
        private readonly IGeneric _genServ;
        private readonly IMemoryCache _cache;
        private readonly AppSettings _appSettings;
        private readonly SimplexConfig _simplexSettings;
        private readonly IRegistration _registrationService;
        private readonly TemplateService _templateService;
        private readonly IGenericAssetCapitalInsuranceCustomerService _genericAssetCapitalInsuranceCustomerService;

        public AssetCapitalInsuranceKycService(IGenericAssetCapitalInsuranceCustomerService genericAssetCapitalInsuranceCustomerService, TemplateService templateService, IRegistration registration, ILogger<IAssetCapitalInsuranceKycService> logger, IOptions<AppSettings> appSettings, IOptions<SimplexConfig> _setting2, IOptions<AssetSimplexConfig> settings, DapperContext context, IFileService fileService, ISmsBLService smsBLService, IUserCacheService userCacheService, IRedisStorageService redisStorageService, IGeneric generic)
        {
            _logger = logger;
            _settings = settings.Value;
            _simplexSettings = _setting2.Value;
            _appSettings = appSettings.Value;
            _context = context;
            _fileService = fileService;
            _smsBLService = smsBLService;
            _userCacheService = userCacheService;
            _redisStorageService = redisStorageService;
            _genServ = generic;
            _registrationService = registration;
            _templateService = templateService;
            _genericAssetCapitalInsuranceCustomerService = genericAssetCapitalInsuranceCustomerService;
        }

        public async Task<GenericResponse2> AddUtilityBillOrIdCardOrSignature(string Session, string UserType, string UserName, string documentType, IFormFile file)
        {
            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = _genericAssetCapitalInsuranceCustomerService.ValidateUserType(UserType);
                if (!file.FileName.EndsWith(".jpeg") && !file.FileName.EndsWith(".png") && !file.FileName.EndsWith(".jpg"))
                {
                    return new GenericResponse2() { Response = EnumResponse.InvalidFileformat, Message = "All file must be in either .jpeg,png,jpg" };
                }
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(Session, UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(UserName, UserType, con);
                if (usr == null)
                {
                    return new GenericResponse2() { Response = EnumResponse.UserNotFound };
                }
                string token = await _genericAssetCapitalInsuranceCustomerService.GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await _genericAssetCapitalInsuranceCustomerService.SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not avaialble" };
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                var request = new RestRequest();
                request.AddParameter("ucid",usr.client_unique_ref);
                request.AddParameter("Kycid",int.Parse(await _genServ.GenerateUnitID(5)));
                request.AddParameter("Verified", false);
                string path = null;
                string filePath = null;
                if (_appSettings.FileUploadPath == "wwwroot")
                {
                    path = "./" + _appSettings.FileUploadPath + "/";
                    filePath = path + file.FileName;
                }
                else
                {
                    path = _appSettings.FileUploadPath + "\\";
                    filePath = Path.Combine(path,file.FileName);
                }
               // await _fileService.SaveFileAsync(file,path);
                await _fileService.SaveFileAsyncByFilePath(file, filePath);
                //string fileName = file.FileName;
                //string filePath = Path.Combine(path, fileName);
                string response = await _genServ.CallServiceAsyncForFileUploadToString(request,Method.POST, _settings.middlewarecustomerurl + "api/Customer/kyc", null,filePath, true, header);
                _logger.LogInformation("response " + response);
                if(string.IsNullOrEmpty(response))
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (genericResponse2 == null)
                {
                    return new GenericResponse2()
                    {
                        Message = "No data was found from source",
                        Response = EnumResponse.NotDataFound,
                        Success = false
                    };
                }
                _logger.LogInformation("genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    SimplexKycPost SimplexKycPost = JsonConvert.DeserializeObject<SimplexKycPost>(JsonConvert.SerializeObject(genericResponse2?.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = true, data = SimplexKycPost, Message = "data collection successful from source" };
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.StackTrace);
                return new GenericResponse2() { Success = false, Response = EnumResponse.SystemError };
            }
            genericResponse2.Response = EnumResponse.NotDataFound;
            genericResponse2.Message = "No data was found from source";
            return genericResponse2;
        }

        public async Task<GenericResponse2> GetCustomerDetailAfterRegistration(string UserName, string Session, string UserType)
        {
            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = _genericAssetCapitalInsuranceCustomerService.ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(Session, UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
               var usr =  await _genServ.GetAssetCapitalInsuraceUserbyUsername(UserName,UserType,con);
                if (usr==null)
                {
                    return new GenericResponse2() {Response=EnumResponse.UserNotFound};
                }
               genericResponse2 = new GenericResponse2()
                {
                    Response = EnumResponse.Successful,
                    Success = true
                };
                return genericResponse2;
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
            }
            genericResponse2 = new GenericResponse2()
            {
                Response = EnumResponse.Successful,
            };
            return genericResponse2;
        }

        public async Task<GenericResponse2> UpdateCustomerDetail(string UserName, string UserType,int ClientId, ExtendedSimplexCustomerUpdate extendedSimplexCustomerRegistration)
        {
            GenericResponse2 genericResponse2 = null;
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var g = _genericAssetCapitalInsuranceCustomerService.ValidateUserType(UserType);
                if (!g.Success)
                {
                    return new GenericResponse2() { Response = g.Response, Message = g.Message };
                }
                if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(extendedSimplexCustomerRegistration.Session, UserType, con)))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
                }
                string token = await _genericAssetCapitalInsuranceCustomerService.GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    token = await _genericAssetCapitalInsuranceCustomerService.SetAPIToken();
                }
                if (string.IsNullOrEmpty(token))
                {
                    return new GenericResponse2() { Response = EnumResponse.RedisError, Message = "Authentication system is not avaialble" };
                }
                var usr = await _genServ.GetAssetCapitalInsuraceUserbyUsername(UserName, UserType, con);
                if (usr == null)
                {
                    new GenericResponse2() { Success = false, Response = EnumResponse.UserNotFound, Message = "Client is not found" };
                }
                IDictionary<string, string> header = new Dictionary<string, string>();
                header.Add("token", token.Split(':')[0]);
                header.Add("xibsapisecret", "");
                var RequestObject = new ExtendedSimplexCustomerRegistration();
                /*
                if (
                    string.IsNullOrEmpty(extendedSimplexCustomerRegistration.email) ||
                    string.IsNullOrEmpty(extendedSimplexCustomerRegistration.firstName) ||
                    string.IsNullOrEmpty(extendedSimplexCustomerRegistration.lastName) ||
                    string.IsNullOrEmpty(extendedSimplexCustomerRegistration.birth_date))
                {
                    return new GenericResponse2()
                    {
                        Response = EnumResponse.InvalidBvnFirstNameLastnameEmailBirthDate,
                        Message = "Bvn, firstname, lastname, email, and birth date cannot be empty"
                    };
                }
                */
                RequestObject.phoneNumber = extendedSimplexCustomerRegistration.phoneNumber;
                RequestObject.firstName = extendedSimplexCustomerRegistration.firstName;
                RequestObject.lastName = extendedSimplexCustomerRegistration.lastName;
                RequestObject.address = extendedSimplexCustomerRegistration.address;
                RequestObject.email = extendedSimplexCustomerRegistration.email;
                RequestObject.client_unique_ref = ClientId;
               // RequestObject.bvn = extendedSimplexCustomerRegistration.bvn;
                RequestObject.otherNames = extendedSimplexCustomerRegistration?.otherNames;
                RequestObject.title=extendedSimplexCustomerRegistration?.title;
                RequestObject.idCountry= extendedSimplexCustomerRegistration?.idCountry;
                RequestObject.idLga= extendedSimplexCustomerRegistration?.idLga;
                RequestObject.idState= extendedSimplexCustomerRegistration?.idState;
                RequestObject.idReligion = (int)(extendedSimplexCustomerRegistration?.idReligion);
                RequestObject.occupationId= (int)(extendedSimplexCustomerRegistration?.occupationId);
                RequestObject.employerCode = extendedSimplexCustomerRegistration?.employerCode;
                RequestObject.gender=extendedSimplexCustomerRegistration?.gender;
                RequestObject.maidenName=extendedSimplexCustomerRegistration?.maidenName;
                RequestObject.maritalStatus=extendedSimplexCustomerRegistration?.maritalStatus;
                RequestObject.sourceOfFund=extendedSimplexCustomerRegistration?.sourceOfFund;
                string dateString = extendedSimplexCustomerRegistration.birth_date; // MM/dd/yyyy format
                //DateTime date = DateTime.ParseExact(dateString, "MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
                //string formattedDate = date.ToString("dd/MM/yyyy");
                RequestObject.birth_date = dateString;
                _logger.LogInformation("RequestObject " + JsonConvert.SerializeObject(RequestObject));
                string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.middlewarecustomerurl + "api/Customer/RegisterExtended", RequestObject, true, header);
                _logger.LogInformation("response from register extended " + response);
                genericResponse2 = JsonConvert.DeserializeObject<GenericResponse2>(response);
                if (!genericResponse2.Success)
                {
                    genericResponse2.Response = EnumResponse.ProfileAlreadyExist;
                    return genericResponse2;
                }
               // var genericResponse3 = JsonConvert.DeserializeObject<TokenGenericResponse<SimplexCustomerRegistrationResponse>>(response);
                _logger.LogInformation("SimplexCustomerRegistrationResponse genericResponse2 " + JsonConvert.SerializeObject(genericResponse2));
                if (genericResponse2.Success)
                {
                    SimplexCustomerRegistrationResponse data = JsonConvert.DeserializeObject<SimplexCustomerRegistrationResponse>((string)(genericResponse2?.data));
                    return new GenericResponse2() { Response = EnumResponse.Successful, Success = genericResponse2.Success, data = JsonConvert.SerializeObject(data) };
                }
                genericResponse2 = new GenericResponse2() { Response = EnumResponse.NotSuccessful, Success = genericResponse2.Success, data = null };
                return genericResponse2;

            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
            }
            return genericResponse2;
        }

        public async Task<GenericResponse2> ValidateSessinAndUserTypeForKyc(string Session, string Username, string UserType)
        {
            IDbConnection con = _context.CreateConnection();
            var g = _genericAssetCapitalInsuranceCustomerService.ValidateUserType(UserType);
            if (!g.Success)
            {
                return new GenericResponse2() { Response = g.Response, Message = g.Message };
            }
            if (!(await _genServ.ValidateSessionForAssetCapitalInsurance(Session, UserType, con)))
            {
                return new GenericResponse2() { Success = false, Response = EnumResponse.InvalidSession, Message = "Session expires" };
            }
            return new GenericResponse2() { Response = EnumResponse.Successful, Success = true };
        }
    }

}






































































































































