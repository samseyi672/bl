using System;
using System.Collections.Generic;
using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using iText.Kernel.Geom;
using iText.Layout.Element;


//using System.DirectoryServices;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Novell.Directory.Ldap;
using Quartz.Impl.Triggers;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;

namespace Retailbanking.BL.Services
{
    public class LdapService : ILdapService
    {
   
        private readonly LdapSettings _ldapSettings;
        private readonly JwtSettings _jwtSettings;
        private readonly IGeneric _genServ;
        private readonly ILogger<AuthenticationServices> _logger;

        public LdapService(IGeneric genServ,ILogger<AuthenticationServices> logger, IOptions<LdapSettings> ldapSettings, IOptions<JwtSettings> jwtSettings)
        {
            _ldapSettings = ldapSettings.Value;
            _jwtSettings = jwtSettings.Value;
            _logger = logger;
            _genServ = genServ;
        }

        
        public bool Authenticate(string username, string password)
        {
           
            using var ldapConnection = new LdapConnection();
            ldapConnection.Connect(_ldapSettings.LdapServer,_ldapSettings.LdapPort);
            try
            {
               // ldapConnection.A = AuthType.Basic;
               // ldapConnection.SessionOptions.VerifyServerCertificate += delegate { return true; };
                Console.WriteLine($"uid={username},{_ldapSettings.BaseDn}");
                // ldapConnection.Bind($"uid={username},{_ldapSettings.BaseDn}", password);
                // return ldapConnection.Bound;
                //  _ldapSettings.UserDn = "CN=" + username;
                //  ldapConnection.Bind($"{_ldapSettings.UserDn},{_ldapSettings.BaseDn}", password);
                //  var sdn = ldapConnection.GetSchemaDN();
                if (username.Contains("trustbancgroup.com"))
                {
                    ldapConnection.Bind($"{username}", password);
                }else
                  ldapConnection.Bind($"{username}@trustbancgroup.com", password);
                return ldapConnection.Bound;
            }
            catch (LdapException l)
            {
                Console.WriteLine("message "+l.Message);
                return false;
            }
        }


        public string GenerateJwtToken(string username)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            Console.WriteLine("_jwtSettings.SecretKey " + _jwtSettings.SecretKey);
            //var key = Encoding.UTF8.GetBytes(_jwtSettings.SecretKey);
            var key = Convert.FromBase64String(Convert.ToBase64String(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey)));
            Console.WriteLine("key " + key);
           // Console.WriteLine("username " + username);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, username)}),
                Expires = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationInMinutes),
                Issuer = _jwtSettings.Issuer,
                Audience = _jwtSettings.Audience,
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        public string GenerateJwtToken(string username, StaffRoleAndPermission roles)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            Console.WriteLine("_jwtSettings.SecretKey " + _jwtSettings.SecretKey);
            //var key = Encoding.UTF8.GetBytes(_jwtSettings.SecretKey);
            var key = Convert.FromBase64String(Convert.ToBase64String(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey)));
            Console.WriteLine("key " + key);
            Console.WriteLine("username " + username);
            var claims = new List<Claim>()
                        {
                        new Claim(ClaimTypes.Name, username)
                        };
            // Add roles as claims
            claims.Add(new Claim(ClaimTypes.Role, roles.role));
            if (roles.Permissions.Any()) {
                    foreach (var permission in roles.Permissions)
                    {
                    Console.WriteLine("permission " + permission);
                        claims.Add(new Claim("Permission", permission));
                    }
                 }
            //var claimsDict = claims.ToDictionary(c => c.Type, c => (object)c.Value);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationInMinutes),
                Issuer = _jwtSettings.Issuer,
                Audience = _jwtSettings.Audience,
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        public object getAllAdusers(int page,int size) { 
        List<string> userNames = new List<string>();
        using (var ldapConnection = new LdapConnection())
        {
            ldapConnection.Connect(_ldapSettings.LdapServer, _ldapSettings.LdapPort);
            ldapConnection.Bind("samson.oluwaseyi@trustbancgroup","Salome@123");
            //ldapConnection.Bind($"administrator","Trust@2023#");
                var searchBase = _ldapSettings.BaseDn2;
          var searchFilter = "(&(objectClass=user)(objectCategory=person))";
                string[] attributes = { "displayName", "mail","CN" }; // Retrieve displayName and mail attributes
                var searchResults = ldapConnection.Search(
                    searchBase,
                    LdapConnection.ScopeSub,
                    searchFilter,
                    attributes,
                    false
                );
                while (searchResults.HasMore())
                {
                    var nextEntry = searchResults.Next();
                    IEnumerator<LdapEntry> enumerator =searchResults.GetEnumerator();
                    //_logger.LogInformation("enumerator " + enumerator);
                    //_genServ.LogRequestResponse("", "enumerator", ""+enumerator);
                    while (enumerator.MoveNext())
                    {          
                        var nextEntry1 = enumerator.Current;
                       // _logger.LogInformation("DN "+nextEntry1.Dn);
                       // _genServ.LogRequestResponse("", "GetAttributeSet ", " "+nextEntry1.GetAttributeSet());
                        //_genServ.LogRequestResponse("", "nextEntry1.Dn", nextEntry1.Dn);
                      //  var displayNameAttribute = nextEntry1.GetAttribute("displayName");
                        //var mailAttribute = nextEntry1.GetAttribute("mail");
                        var CN = nextEntry1.GetAttribute("CN");
                      //  _genServ.LogRequestResponse("","CN", "CN " + CN.StringValue);
                        if(CN!=null)
                        {
                            userNames.Add(CN.StringValue);
                            // Handle the case where the attribute is not present for an entry
                            // You can log a message, skip the entry, or handle it as needed
                        }
                    }
                }
            }
          //  _logger.LogInformation("username "+userNames);
            // Calculate total pages
            var totalRecords = userNames.Count;
            var totalPages = (int)Math.Ceiling((double)totalRecords /size);
            // Apply pagination
            var paginatedData = userNames
            .Skip(page==0?page:(page - 1) * size) // Skip items from previous pages
            .Take(size)                    // Take the items for the current page
                .ToList();
            var result = new
            {
                PageNumber = page,
                PageSize = size,
                TotalRecords = totalRecords,
                TotalPages = totalPages,
                Data = paginatedData
            };
            return result;
    }

        public List<string> SearchStaffusers(string Search)
        {
            List<string> userNames = new List<string>();
            using (var ldapConnection = new LdapConnection())
            {
                ldapConnection.Connect(_ldapSettings.LdapServer, _ldapSettings.LdapPort);
                ldapConnection.Bind("samson.oluwaseyi@trustbancgroup", "Salome@123");
                //ldapConnection.Bind($"administrator","Trust@2023#");
                var searchBase = _ldapSettings.BaseDn2;
                var searchFilter = "(&(objectClass=user)(objectCategory=person))";
                string[] attributes = { "CN" }; // Retrieve displayName and mail attributes
                var searchResults = ldapConnection.Search(
                    searchBase,
                    LdapConnection.ScopeSub,
                    searchFilter,
                    attributes,
                    false
                );
                List<string> matchedValues = new List<string>();
                while (searchResults.HasMore())
                {
                    var nextEntry = searchResults.Next();
                    IEnumerator<LdapEntry> enumerator = searchResults.GetEnumerator();
                    while (enumerator.MoveNext())
                    {
                        var nextEntry1 = enumerator.Current;
                     //   _logger.LogInformation("DN " + nextEntry1.Dn);
                        var CN = nextEntry1.GetAttribute("CN");
                        _genServ.LogRequestResponse("", "CN", "CN " + CN.StringValue);
                        if (CN != null)
                        {
                            userNames.Add(CN.StringValue);
                            _logger.LogInformation("CN.StringValue " + Regex.Replace(CN.StringValue, @"\s+", ""));
                            _logger.LogInformation("Replace "+CN.StringValue.Replace(" ",""));
                            string text = CN.StringValue.Replace(" ","");
                            if (!string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(Search.ToLower()) && text.Contains(Search, StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.LogInformation("names: " + CN.StringValue);
                                matchedValues.Add(CN.StringValue);
                            }
                            if (CN.StringValue.IndexOf(Search, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                               _logger.LogInformation("index search " + CN.StringValue);
                               // matchedValues.Add(CN.StringValue);
                            }
                            // Handle the case where the attribute is not present for an entry
                            // You can log a message, skip the entry, or handle it as needed
                        }
                    }
                }
                return matchedValues;
            }
          //  _logger.LogInformation("username " +string.Join(",",userNames));
            //_logger.LogInformation("processing and checking the list");
           // List<string> matchedValues = userNames.Where(x => x.Trim().Contains(Search)).ToList();
           // return matchedValues;
        }

        public bool WindowADAuthentication(string username, string password)
        {
            {      
                return false;
            }
        }
    }


}












































































































