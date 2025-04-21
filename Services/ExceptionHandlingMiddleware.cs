using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.Services
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                // Call the next middleware in the pipeline
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unhandled exception has occurred.");
                await HandleExceptionAsync(context, ex);
            }
        }

        private static Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            context.Response.ContentType = "application/json";

            if (exception is ArgumentNullException)
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                PrimeAdminResponse primeAdminResponse1 = new PrimeAdminResponse();
                primeAdminResponse1.Response = EnumResponse.NotSuccessful;
                primeAdminResponse1.Success = false;
                primeAdminResponse1.Message = exception.Message;
                var jsonResponse1 = JsonConvert.SerializeObject(primeAdminResponse1);
                return context.Response.WriteAsync(jsonResponse1);
            }

            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var response = new
            {
                StatusCode = context.Response.StatusCode,
                Message = "Internal server error.",
                Detailed = exception.Message
            };
            PrimeAdminResponse primeAdminResponse= new PrimeAdminResponse();
            primeAdminResponse.Response = EnumResponse.NotSuccessful;
            primeAdminResponse.Success = false;
            primeAdminResponse.Message = exception.Message;
            var jsonResponse = JsonConvert.SerializeObject(primeAdminResponse);
            return context.Response.WriteAsync(jsonResponse);
        }

    }

}
