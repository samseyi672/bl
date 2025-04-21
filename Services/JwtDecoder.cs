using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text;

namespace Retailbanking.BL.Services
{
    public  class JwtDecoder
    {
        public static string DecodeJwtToken(string token)
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token); // Parses the token
            // Get specific claims from the token
            string name = jwtToken.Claims.Where(c => c.Type == "unique_name").Select(c=>c.Value).FirstOrDefault();
            string role = jwtToken.Claims.Where(c => c.Type == "role").Select(c => c.Value).FirstOrDefault();
            return name+"_"+role;
        }

    }
}
