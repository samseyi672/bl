using Google.Protobuf.WellKnownTypes;
using iText.StyledXmlParser.Jsoup.Parser;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public  interface ISimplexCustomerService
    {

     Task<LoginResponse> LoginUser(LoginRequest Request, bool LoginWithFingerPrint = false);

        // auth/token
        //SimplexTokenResponse
     Task<GenericResponse2> GetToken(string xibsapisecret,SimplexToken token); 
     
       // auth/token/refresh -SimplexRefreshTokenResponse
     Task<GenericResponse2> RefreshToken(string xibsapisecret, SimplexRefreshToken token);//post

        //client/register - SimplexCustomerRegistration,SimplexCustomerRegistrationResponse
     Task<GenericResponse2> Register(string token,string xibsapisecret, SimplexCustomerRegistration registration);//post

        //URI: /client/register-extended/client/register-extended,SimplexCustomerRegistrationResponse
     Task<GenericResponse2> RegisterExtended(string token, string xibsapisecret, ExtendedSimplexCustomerRegistration extendedSimplexCustomerRegistration);//post

        ///client/exist/:email,IfClientExistResponse,SimplexCustomerFullDetailsResponse
     Task<GenericResponse2> CheckIfAClientExistsByEmail(string token,string xibsapisecret, string email);//get

      //URI: /client/full-details/:accountCode,SimplexCustomerFullDetailsResponse
     Task<GenericResponse2> GetFullDetails(string token,string xibsapisecret,int accountCode);//post

        //URI: /client/full-details/ucid/:Client_unique_ref,
        //Response - SimplexCustomerFullDetailsByClientRefResponse
     Task<GenericResponse2> GetFullDetailsByClientRef(string token, string xibsapisecret,int Clientuniqueref);//post

        //URI: /client/minor-SimplexClientMinor
     Task<GenericResponse2> CreateOrUpdateSimplexClientMinor(string token,string xibsapisecret, SimplexClientMinor simplexClientMinor);//post

     // URI: /client/next-of-kin
     Task<GenericResponse2> CreateOrUpdateSimplexClientNextofKin(string token,string xibsapisecret, SimplexClientNextOfKin simplexClientMinor);//post

        //URI: /client/relationships
        //Response -SimplexCustomerRelationship
        Task<GenericResponse2> GetRelationship(string token,string xibsapisecret);//get

        //URI: /client/inhouse-banks
        //SimplexInHouseBanksResponse simplexInHouseBanks
        Task<GenericResponse2> GetInhouseBanks(string token,string xibsapisecret);//get

        //URI: /client/bank-details,
        //Response -SimplexClientBankDetailsResponse
     Task<GenericResponse2> AddOrUpdateClientBankDetails(string token,string xibsapisecret, SimplexClientBankDetails simplexInHouseBanks);//post

        // URI: /client/countries
        //Response -ClientCountries
    Task<GenericResponse2> ClientCountries(string token, string xibsapisecret);//get

        // URI: /client/states
        //Response - ClientStates
     Task<GenericResponse2> ClientStates(string token,string xibsapisecret);//get

        //  URI: /client/lga/{IdState}
        //Response -ClientLgas
     Task<GenericResponse2> ClientLga(string token,string xibsapisecret,string state);//get

        // URI: /client/titles
        //Response- ClientTitles
     Task<GenericResponse2> GetClientTitles(string token,string xibsapisecret);//get

        //URI: /client/source-of-fund
        //Response- SourcesOfFund
     Task<GenericResponse2> GetSourcesOffund(string token,string xibsapisecret); //get

        //URI: /client/employers
        //Response - Employers
     Task<GenericResponse2> GetEmployers(string token,string xibsapisecret); //get

        //URI: /client/occupations
        //Response = Occupations
     Task<GenericResponse2>  GetOccupations(string token,string xibsapisecret); // get

        //URI: /client/religions
        //Response- Religions
     Task<GenericResponse2> GetReligious(string token,string xibsapisecret); //get

        //URI: /client/profile-picture
        //Response = ClientPicture
     Task<GenericResponse2> GetClientPicture(string token,string xibsapisecret, ClientPictureRequest clientPictureRequest); //post

        //URI: /kyc/{client_unique_ref} 
        //Response - SimplexKycResponse
     Task<GenericResponse2> GetSimplexKycList(string token,string xibsapisecret, int client_unique_ref); //get

        //URI: /kyc
        //Response - SimplexKycPost
     Task<GenericResponse2> CreateSimplexKycResponse(string token,string xibsapisecret,int ucid , IFormFile file ,int Kycid,bool Verified); //post
        Task<GenericResponse2> CheckIfCustomerExist(string token, string xibsapisecret, SimplexCustomerChecker customerChecker);
        Task<GenericResponse2> GetWalletBalance(string token, string xibsapisecret, string currency, int clientid);
        Task<GenericResponse2> AddBankOrUpdateForClient(string token, string xibsapisecret, SimplexClientBankDetails simplexClientBankDetails);
        Task<GenericResponse2> AddOrUpdateClientMinor(string token, string xibsapisecret, SimplexClientMinor simplexClientMinor);
        Task<GenericResponse2> AddOrUpdateClientNextOfKin(string token, string xibsapisecret, SimplexClientNextOfKin simplexClientNextOfKin);
        Task<GenericResponse2> RemoveBankOrUpdateForClient(string token, string xibsapisecret, string idBank, string clientId);
    }

}





























































































































