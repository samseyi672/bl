using iText.Html2pdf.Attach;
using iText.Layout.Element;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlX.XDevAPI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Utilities;
using Quartz.Impl.Triggers;
using RestSharp;
using Retailbanking.BL.IServices;
using Retailbanking.BL.utils;
using Retailbanking.Common.CustomObj;
using Retailbanking.Common.DbObj;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using static Retailbanking.BL.Services.TransferServices;


namespace Retailbanking.BL.Services
{
    public class SimplexCustomerService : ISimplexCustomerService
    {

        private readonly ILogger<SimplexCustomerService> _logger;
        private readonly SimplexConfig _settings;
        private readonly IGeneric _genServ;
        private readonly IFileService _fileService;


        public SimplexCustomerService(IFileService fileService,ILogger<SimplexCustomerService> logger, IOptions<SimplexConfig> options, IGeneric genServ)
        {
            _logger = logger;
            _settings = options.Value;
            _genServ = genServ;
            _fileService=fileService;
    }

        public async Task<string> baseApiFunction(string token, string xibsapisecret, string uri, object requestobject, string method)
        {
            var header = new Dictionary<string, string>();
            xibsapisecret = SimplexKeyComputation.ComputeApiKey(_settings.APIKey, _settings.APISecret);
            _logger.LogInformation("xibsapisecret " + xibsapisecret);
            header.TryAdd("x-ibs-api-secret", xibsapisecret);
            header.TryAdd("Authorization", "Bearer " + token);
            _logger.LogInformation("requestobject " + requestobject);
            string response = await _genServ.CallServiceAsyncToString(string.IsNullOrEmpty(method) ? Method.GET : Method.POST, _settings.baseurl + uri, requestobject, true, header);
            _logger.LogInformation("api response " + response);
            return response;
        }

        public async Task<string> baseApiFunctionforFileUpload(string token, string xibsapisecret, RestRequest request, string filepath, string uri, object requestobject)
        {
            var header = new Dictionary<string, string>();
            xibsapisecret = SimplexKeyComputation.ComputeApiKey(_settings.APIKey, _settings.APISecret);
            header.TryAdd("x-ibs-api-secret", xibsapisecret);
            header.TryAdd("Authorization", "Bearer " + token);
            string response = await _genServ.CallServiceAsyncForFileUploadToString(request, Method.POST, _settings.baseurl + uri, requestobject, filepath, true, header);
            _logger.LogInformation("token response " + response);
            return response;
        }

        public async Task<GenericResponse2> AddOrUpdateClientBankDetails(string token, string xibsapisecret, SimplexClientBankDetails simplexInHouseBanks)
        {
            string response = await baseApiFunction(token, xibsapisecret, "client/bank-details", simplexInHouseBanks, "post");
            _logger.LogInformation("AddOrUpdateClientBankDetails response " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var SimplexClientBankDetailsResponse = JsonConvert.DeserializeObject<SimplexClientBankDetailsResponse>(response);
                return new GenericResponse2()
                {
                    data = SimplexClientBankDetailsResponse,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> CheckIfAClientExistsByEmail(string token, string xibsapisecret, string email)
        {
            string response = await baseApiFunction(token, xibsapisecret, "client/exist/" + email, null, "get");
            _logger.LogInformation("CheckIfAClientExistsByEmail response " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var SimplexCustomerFullDetailsResponse = JsonConvert.DeserializeObject<SimplexCustomerFullDetailsResponse>(response);
                return new GenericResponse2()
                {
                    data = SimplexCustomerFullDetailsResponse,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> ClientCountries(string token, string xibsapisecret)
        {
            string response = await baseApiFunction(token, xibsapisecret, "client/countries", null,null);
            _logger.LogInformation("ClientCountries response " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var ClientCountries = JsonConvert.DeserializeObject<ClientCountries>(response);
                return new GenericResponse2()
                {
                    data = ClientCountries,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> ClientLga(string token, string xibsapisecret, string state)
        {
            string response = await baseApiFunction(token, xibsapisecret, "client/lga/" + state, null, null);
            _logger.LogInformation("ClientLgas response " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var ClientLgas = JsonConvert.DeserializeObject<ClientLgas>(response);
                return new GenericResponse2()
                {
                    data = ClientLgas,
                    Success = true,
                    Response = EnumResponse.Successful
                };          
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> ClientStates(string token, string xibsapisecret)
        {
            string response = await baseApiFunction(token, xibsapisecret, "client/states", null,null);
            _logger.LogInformation("states response " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var ClientStates = JsonConvert.DeserializeObject<ClientStates>(response);
                return new GenericResponse2()
                {
                    data = ClientStates,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> CreateOrUpdateSimplexClientMinor(string token, string xibsapisecret, SimplexClientMinor simplexClientMinor)
        {
            string response = await baseApiFunction(token, xibsapisecret, "client/minor", simplexClientMinor, null);
            _logger.LogInformation("simplexClientMinor response " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                // var simplexClientMinor = JsonConvert.DeserializeObject<string>(response);
                return new GenericResponse2()
                {
                    data = response,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        //come back to this for the response format
        public async Task<GenericResponse2> CreateOrUpdateSimplexClientNextofKin(string token, string xibsapisecret, SimplexClientNextOfKin simplexClientNextOfMinor)
        {
            string response = await baseApiFunction(token, xibsapisecret, "client/next-of-kin", simplexClientNextOfMinor, "post");
            _logger.LogInformation("simplexClientMinor response " + response);
            if (string.IsNullOrEmpty(response))
            {
                return new GenericResponse2()
                {
                    data = null,
                    Success = false,
                    Response = EnumResponse.Successful
                };
            }
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var SimplexClientNextOfKinResponse = JsonConvert.DeserializeObject<SimplexClientNextOfKinResponse>(response);
                return new GenericResponse2()
                {
                    data = SimplexClientNextOfKinResponse,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = JsonConvert.DeserializeObject<SimplexClientNextOfKinResponse>(response),
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        //come back to this
        public async Task<GenericResponse2> CreateSimplexKycResponse(string token, string xibsapisecret, int ucid, IFormFile file, int Kycid, bool Verified)
        {
            //string response = await baseApiFunction(token, xibsapisecret, "kyc",null);
            var request = new RestRequest();
            request.AddParameter("ucid", ucid);
            request.AddParameter("Kycid", Kycid);
            request.AddParameter("Verified", Verified);
            // IFormFile file = file;
            if (file == null || file.Length == 0)
            {
                return new GenericResponse2() { Message = "No file uploaded.", Success = false, Response = EnumResponse.NoFileUploaded };
            }
            // Save the file to a specific location
            var filePath = Path.Combine(_settings.SimplexUploadedFile, file.FileName);
            await _fileService.SaveFileAsyncByFilePath(file, filePath);
            string response = await baseApiFunctionforFileUpload(token, xibsapisecret, request, filePath, "kyc", null);
            _logger.LogInformation("SimplexKycPost response " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var SimplexKycPost = JsonConvert.DeserializeObject<SimplexKycPost>(response);
                return new GenericResponse2()
                {
                    data = SimplexKycPost,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> GetClientPicture(string token, string xibsapisecret, ClientPictureRequest clientPictureRequest)
        {
            IFormFile file = clientPictureRequest.file;
            if (file == null || file.Length == 0)
            {
                return new GenericResponse2() { Message = "No file uploaded.", Success = false, Response = EnumResponse.NoFileUploaded };
            }

            // Save the file to a specific location
            //var filePath = Path.Combine("Path/To/Save/File", file.FileName);
            var filePath = Path.Combine(_settings.SimplexUploadedFile, file.FileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }
            //string response = await baseApiFunction(token, xibsapisecret, "client/profile-picture",clientPictureRequest);
            var request = new RestRequest();
            request.AddParameter("ucid", clientPictureRequest.ucid);
            string response = await baseApiFunctionforFileUpload(token, xibsapisecret, request, filePath, "client/profile-picture", clientPictureRequest);
            if(string.IsNullOrEmpty(response))
            {
                return new GenericResponse2()
                {
                    data = response,
                    Success = false,
                    Response = EnumResponse.Successful
                };
            }
            _logger.LogInformation("SimplexCustomerFullDetailsResponse response " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var ClientPicture = JsonConvert.DeserializeObject<ClientPicture>(response);
                return new GenericResponse2()
                {
                    data = ClientPicture,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> GetClientTitles(string token, string xibsapisecret)
        {
            string response = await baseApiFunction(token, xibsapisecret, "client/titles", null, null);
            _logger.LogInformation("SimplexCustomerFullDetailsResponse response " + response);
            if (string.IsNullOrEmpty(response))
            {
                return new GenericResponse2()
                {
                    data = response,
                    Success = false,
                    Response = EnumResponse.Successful
                };
            }
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var ClientTitles = JsonConvert.DeserializeObject<ClientTitles>(response);
                return new GenericResponse2()
                {
                    data = ClientTitles,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> GetEmployers(string token, string xibsapisecret)
        {
            string response = await baseApiFunction(token, xibsapisecret, "client/employers", null, null);
            _logger.LogInformation("GetEmployers response " + response);
            if (string.IsNullOrEmpty(response))
            {
                return new GenericResponse2()
                {
                    data = response,
                    Success = false,
                    Response = EnumResponse.Successful
                };
            }
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var Employers = JsonConvert.DeserializeObject<Employers>(response);
                return new GenericResponse2()
                {
                    data = Employers,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> GetFullDetails(string token, string xibsapisecret, int accountCode)
        {
            string response = await baseApiFunction(token, xibsapisecret, "client/full-details/" + accountCode, null, "post");
            _logger.LogInformation("SimplexCustomerFullDetailsResponse response " + response);
            if (string.IsNullOrEmpty(response))
            {
                return new GenericResponse2()
                {
                    data = response,
                    Success = false,
                    Response = EnumResponse.Successful
                };
            }
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var SimplexCustomerFullDetailsResponse = JsonConvert.DeserializeObject<SimplexCustomerFullDetailsResponse>(response);
                return new GenericResponse2()
                {
                    data = SimplexCustomerFullDetailsResponse,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> GetFullDetailsByClientRef(string token, string xibsapisecret, int Clientuniqueref)
        {
            string response = await baseApiFunction(token, xibsapisecret, "client/full-details/ucid/" + Clientuniqueref, null, null);
            _logger.LogInformation("SimplexCustomerFullDetailsByClientRefResponse response " + response);
            if (string.IsNullOrEmpty(response))
            {
                return new GenericResponse2()
                {
                    data = response,
                    Success = false,
                    Response = EnumResponse.Successful
                };
            }
            if (string.IsNullOrEmpty(response))
            {
                return new GenericResponse2()
                {
                    data = response,
                    Success = false,
                    Response = EnumResponse.Successful
                };
            }
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var SimplexCustomerFullDetailsByClientRefResponse = JsonConvert.DeserializeObject<SimplexCustomerFullDetailsByClientRefResponse>(response);
                return new GenericResponse2()
                {
                    data = SimplexCustomerFullDetailsByClientRefResponse,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> GetInhouseBanks(string token, string xibsapisecret)
        {
            string response = await baseApiFunction(token, xibsapisecret, "client/inhouse-banks", null, null);
            _logger.LogInformation("token response " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var SimplexInHouseBanksResponse = JsonConvert.DeserializeObject<SimplexInHouseBanksResponse>(response);
                return new GenericResponse2()
                {
                    data = SimplexInHouseBanksResponse,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> GetOccupations(string token, string xibsapisecret)
        {
            string response = await baseApiFunction(token, xibsapisecret, "client/occupations", null, null);
            _logger.LogInformation("token response " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var Occupations = JsonConvert.DeserializeObject<Occupations>(response);
                return new GenericResponse2()
                {
                    data = Occupations,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> GetRelationship(string token, string xibsapisecret)
        {
            string response = await baseApiFunction(token, xibsapisecret, "client/relationships", null, null);
            _logger.LogInformation("token response " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var SimplexKycResponse = JsonConvert.DeserializeObject<SimplexCustomerRelationship>(response);
                return new GenericResponse2()
                {
                    data = SimplexKycResponse,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> GetReligious(string token, string xibsapisecret)
        {
            // string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.baseurl + "client/religions", null, true, header);
            string response = await baseApiFunction(token, xibsapisecret, "client/religions", null, null);
            _logger.LogInformation("token response " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var SimplexKycResponse = JsonConvert.DeserializeObject<Religions>(response);
                return new GenericResponse2()
                {
                    data = SimplexKycResponse,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> GetSimplexKycList(string token, string xibsapisecret, int client_unique_ref)
        {
            var header = new Dictionary<string, string>();
            xibsapisecret = SimplexKeyComputation.ComputeApiKey(_settings.APIKey, _settings.APISecret);
            header.TryAdd("x-ibs-api-secret", xibsapisecret);
            header.TryAdd("Authorization", "Bearer " + token);
            string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.baseurl + "kyc/" + client_unique_ref, null, true, header);
            _logger.LogInformation("token response " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var SimplexKycResponse = JsonConvert.DeserializeObject<SimplexKycResponse>(response);
                return new GenericResponse2()
                {
                    data = SimplexKycResponse,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> GetSourcesOffund(string token, string xibsapisecret)
        {
            var header = new Dictionary<string, string>();
            xibsapisecret = SimplexKeyComputation.ComputeApiKey(_settings.APIKey, _settings.APISecret);
            header.TryAdd("x-ibs-api-secret", xibsapisecret);
            header.TryAdd("Authorization", "Bearer " + token);
            string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.baseurl + "client/source-of-fund", null, true, header);
            _logger.LogInformation("token response " + response);
            if(string.IsNullOrEmpty(response))
            {
                return new GenericResponse2()
                {
                    data = response,
                    Success = false,
                    Response = EnumResponse.Successful
                };
            }
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var SourcesOfFund = JsonConvert.DeserializeObject<SourcesOfFund>(response);
                return new GenericResponse2()
                {
                    data = SourcesOfFund,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> GetToken(string xibsapisecret, SimplexToken token)
        {
            var header = new Dictionary<string, string>();
            xibsapisecret = SimplexKeyComputation.ComputeApiKey(_settings.APIKey, _settings.APISecret);
            header.TryAdd("x-ibs-api-secret", xibsapisecret);
            string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.baseurl + "auth/token", token, true, header);
            _logger.LogInformation("token response " + response);
            JObject json = (JObject)JToken.Parse(response);
            string access_token = json.ContainsKey("data") && json["data"].Type != JTokenType.Null ? json["data"]["access_token"].ToString() : "";
            int statusCode = json.ContainsKey("statusCode") && int.Parse(json["statusCode"].ToString()) == 200 ? int.Parse(json["statusCode"].ToString()) : 0;
            //convert to response object 
            if (!string.IsNullOrEmpty(access_token) && statusCode == 200)
            {
                var SimplexTokenResponse = JsonConvert.DeserializeObject<SimplexTokenResponse>(response);
                return new GenericResponse2()
                {
                    data = SimplexTokenResponse,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public Task<LoginResponse> LoginUser(LoginRequest Request, bool LoginWithFingerPrint = false)
        {
            throw new NotImplementedException();
        }

        public async Task<GenericResponse2> RefreshToken(string xibsapisecret, SimplexRefreshToken token)
        {
            var header = new Dictionary<string, string>();
            xibsapisecret = SimplexKeyComputation.ComputeApiKey(_settings.APIKey, _settings.APISecret);
            header.TryAdd("x-ibs-api-secret", xibsapisecret);
            string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.baseurl + "auth/token/refresh", token, true, header);
            _logger.LogInformation("token response " + response);
            JObject json = (JObject)JToken.Parse(response);
            string access_token = json.ContainsKey("data") && json["data"].Type != JTokenType.Null ? json["data"]["access_token"].ToString() : "";
            int statusCode = json.ContainsKey("statusCode") && int.Parse(json["statusCode"].ToString()) == 200 ? int.Parse(json["statusCode"].ToString()) : 0;
            //convert to response object 
            if (string.IsNullOrEmpty(access_token) && statusCode == 200)
            {
                var SimplexTokenResponse = JsonConvert.DeserializeObject<SimplexTokenResponse>(response);
                return new GenericResponse2()
                {
                    data = SimplexTokenResponse,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> Register(string token, string xibsapisecret, SimplexCustomerRegistration registration)
        {
            var header = new Dictionary<string, string>();
            xibsapisecret = SimplexKeyComputation.ComputeApiKey(_settings.APIKey, _settings.APISecret);
            _logger.LogInformation("xibsapisecret " + token);
            header.TryAdd("x-ibs-api-secret", xibsapisecret);
            _logger.LogInformation("token " + token);
            header.TryAdd("Authorization", "Bearer " + token);
            string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.baseurl + "client/register", registration, true, header);
            _logger.LogInformation("registration response " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var SimplexCustomerRegistrationResponse = JsonConvert.DeserializeObject<SimplexCustomerRegistrationResponse>(response);
                return new GenericResponse2()
                {
                    data = SimplexCustomerRegistrationResponse,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> RegisterExtended(string token, string xibsapisecret, ExtendedSimplexCustomerRegistration extendedSimplexCustomerRegistration)
        {
            var header = new Dictionary<string, string>();
            xibsapisecret = SimplexKeyComputation.ComputeApiKey(_settings.APIKey, _settings.APISecret);
            header.TryAdd("x-ibs-api-secret", xibsapisecret);
            header.TryAdd("Authorization", "Bearer " + token);
            //  string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.baseurl + "client/register", extendedSimplexCustomerRegistration, true, header);
            string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.baseurl + "client/register-extended", extendedSimplexCustomerRegistration, true, header);
            _logger.LogInformation("registration response " + response);
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var SimplexCustomerRegistrationResponse = JsonConvert.DeserializeObject<SimplexCustomerRegistrationResponse>(response);
                return new GenericResponse2()
                {
                    data = SimplexCustomerRegistrationResponse,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> CheckIfCustomerExist(string token, string xibsapisecret, SimplexCustomerChecker customerChecker)
        {
            try
            {
                var header = new Dictionary<string, string>();
                xibsapisecret = SimplexKeyComputation.ComputeApiKey(_settings.APIKey, _settings.APISecret);
                header.TryAdd("x-ibs-api-secret", xibsapisecret);
                header.TryAdd("Authorization", "Bearer " + token);
                string response = await _genServ.CallServiceAsyncToString(Method.GET, _settings.baseurl + "client/exist/" + customerChecker.email, customerChecker, true, header);
                _logger.LogInformation("CheckIfCustomerExist response " + response);
                JObject json = (JObject)JToken.Parse(response);
                if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
                {
                    var SimplexCustomerCheckerResponse = JsonConvert.DeserializeObject<SimplexCustomerCheckerResponse>(response);
                    return new GenericResponse2()
                    {
                        data = SimplexCustomerCheckerResponse,
                        Success = true,
                        Response = EnumResponse.Successful
                    };
                }
                return new GenericResponse2()
                {
                    data = response,
                    Success = false,
                    Response = EnumResponse.Successful
                };
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
            }
            return new GenericResponse2()
            {
                data = null,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> GetWalletBalance(string token, string xibsapisecret, string currency, int clientid)
        {
            try
            {
                var header = new Dictionary<string, string>();
                xibsapisecret = SimplexKeyComputation.ComputeApiKey(_settings.APIKey, _settings.APISecret);
                header.TryAdd("x-ibs-api-secret", xibsapisecret);
                header.TryAdd("Authorization", "Bearer " + token);
                string response = await _genServ.CallServiceAsyncToString(Method.GET, _settings.baseurl + "wallet/balance/" + currency + "/" + clientid, null, true, header);
                _logger.LogInformation("GetWalletBalance response " + response);
                if(string.IsNullOrEmpty(response))
                {
                    return new GenericResponse2()
                    {
                        data = response,
                        Success = false,
                        Response = EnumResponse.Successful
                    };
                }
                JObject json = (JObject)JToken.Parse(response);
                if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
                {
                    var SimplexWalletBalance = JsonConvert.DeserializeObject<SimplexWalletBalance>(response);
                    return new GenericResponse2()
                    {
                        data = SimplexWalletBalance,
                        Success = true,
                        Response = EnumResponse.Successful
                    };
                }
                return new GenericResponse2()
                {
                    data = response,
                    Success = false,
                    Response = EnumResponse.Successful
                };
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
            }
            return new GenericResponse2()
            {
                data = null,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> AddBankOrUpdateForClient(string token, string xibsapisecret, SimplexClientBankDetails simplexClientBankDetails)
        {
            try
            {
                var header = new Dictionary<string, string>();
                xibsapisecret = SimplexKeyComputation.ComputeApiKey(_settings.APIKey, _settings.APISecret);
                header.TryAdd("x-ibs-api-secret", xibsapisecret);
                header.TryAdd("Authorization", "Bearer " + token);
                string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.baseurl + "client/bank-details",simplexClientBankDetails, true, header);
                _logger.LogInformation("GetWalletBalance response " + response);
                if(string.IsNullOrEmpty(response))
                {
                    return new GenericResponse2()
                    {
                        data = null,
                        Success = false,
                        Response = EnumResponse.Successful
                    };
                }
                JObject json = (JObject)JToken.Parse(response);
                if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
                {
                    var SimplexClientBankDetailsResponse = JsonConvert.DeserializeObject<SimplexClientBankDetailsResponse>(response);
                    return new GenericResponse2()
                    {
                        data = SimplexClientBankDetailsResponse,
                        Success = true,
                        Response = EnumResponse.Successful
                    };
                }
                return new GenericResponse2()
                {
                    data = response,
                    Success = false,
                    Response = EnumResponse.Successful
                };
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
            }
            return new GenericResponse2()
            {
                data = null,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public async Task<GenericResponse2> AddOrUpdateClientMinor(string token, string xibsapisecret, SimplexClientMinor simplexClientMinor)
        {
            try
            {
                var header = new Dictionary<string, string>();
                xibsapisecret = SimplexKeyComputation.ComputeApiKey(_settings.APIKey, _settings.APISecret);
                header.TryAdd("x-ibs-api-secret", xibsapisecret);
                header.TryAdd("Authorization", "Bearer " + token);
                string response = await _genServ.CallServiceAsyncToString(Method.POST, _settings.baseurl + "/client/minor", simplexClientMinor, true, header);
                _logger.LogInformation("AddOrUpdateClientMinor response " + response);
                if (string.IsNullOrEmpty(response))
                {
                    return new GenericResponse2()
                    {
                        data = null,
                        Success = false,
                        Response = EnumResponse.Successful
                    };
                }
                JObject json = (JObject)JToken.Parse(response);
                if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
                {
                    var simplexClientMinorResponse = JsonConvert.DeserializeObject<SimplexClientMinorResponse>(response);
                    return new GenericResponse2()
                    {
                        data = simplexClientMinorResponse,
                        Success = true,
                        Response = EnumResponse.Successful
                    };
                }
                return new GenericResponse2()
                {
                    data = response,
                    Success = false,
                    Response = EnumResponse.Successful
                };
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
            }
            return new GenericResponse2()
            {
                data = null,
                Success = false,
                Response = EnumResponse.Successful
            };
        }

        public Task<GenericResponse2> AddOrUpdateClientNextOfKin(string token, string xibsapisecret, SimplexClientNextOfKin simplexClientNextOfKin)
        {
            return this.CreateOrUpdateSimplexClientNextofKin(token,xibsapisecret,simplexClientNextOfKin);
        }

        public async Task<GenericResponse2> RemoveBankOrUpdateForClient(string token, string xibsapisecret, string idBank, string clientId)
        {
            string response = await baseApiFunction(token, xibsapisecret, "bank-details/remove/"+clientId+"/"+int.Parse(idBank), null, "post");
            _logger.LogInformation("simplexClientMinor response " + response);
            if (string.IsNullOrEmpty(response))
            {
                return new GenericResponse2()
                {
                    data = null,
                    Success = false,
                    Response = EnumResponse.Successful
                };
            }
            JObject json = (JObject)JToken.Parse(response);
            if (json.ContainsKey("hasError") && bool.Parse(json["hasError"].ToString()) == false && int.Parse(json["statusCode"].ToString()) == 200)
            {
                var settings = new JsonSerializerSettings
                {
                    MissingMemberHandling = MissingMemberHandling.Ignore
                };
                var SimplexGenericResponse = JsonConvert.DeserializeObject<SimplexGenericResponse>(response,settings);
                return new GenericResponse2()
                {
                    data = SimplexGenericResponse,
                    Success = true,
                    Response = EnumResponse.Successful
                };
            }
            return new GenericResponse2()
            {
                data = response,
                Success = false,
                Response = EnumResponse.Successful
            };
        }
    }

}


































