using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace Retailbanking.BL.Services
{
    public  class JsonStringProcessor
    {
        public static void processError(string jsonstring)
        {
            // string jsonString = "{\"payment\":null,\"resp\":null,\"errorCode\":0,\"message\":\"Invalid Beneficiary Details\",\"success\":false,\"trustBancRef\":null}";
            JToken jToken = JToken.Parse(jsonstring);
            // Validate if the required keys exist
            if (!jToken.HasValues ||
                !jToken.ContainsKey("payment") ||
                !jToken.ContainsKey("resp") ||
                !jToken.ContainsKey("errorCode") ||
                !jToken.ContainsKey("trustBancRef"))
            {
                // Extract the required fields
                var payment = jToken["payment"]?.ToString();
                var resp = jToken["resp"]?.ToString();
                var errorCode = jToken["errorCode"]?.ToObject<int>() ?? 0; // Default to 0 if null
                var trustBancRef = jToken["trustBancRef"]?.ToString();
                string exceptionMessage = jToken["message"]?.ToString() ?? "An error occurred";
                // Check if all conditions are met
                if (payment == null && errorCode == 0 && trustBancRef == null && resp == null)
                {
                    // Throw exception with the message
                    throw new ArgumentNullException(exceptionMessage);
                }
            }
        }
    }
    // Extension method to check if a key exists in JToken
    public static class JTokenExtensions
    {
        public static bool ContainsKey(this JToken token, string key)
        {
            return token[key] != null;
        }
    }
}






















































