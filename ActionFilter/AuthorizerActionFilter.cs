using Dapper;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RestSharp;
using Retailbanking.BL.IServices;
using Retailbanking.BL.Services;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Retailbanking.BL.ActionFilter
{
    public class AuthorizerActionFilter : IAsyncActionFilter
    {
        private readonly ILogger<TransferServices> _logger;
        private readonly IGeneric _genServ;
        private readonly IStaffServiceDbOperationFilter _staffService;
        private readonly IDataService _dataService;

        public AuthorizerActionFilter(IDataService dataService,ILogger<TransferServices> logger, IGeneric genServ, IStaffServiceDbOperationFilter staffService)
        {
            _logger = logger;
            _genServ = genServ;
            _staffService = staffService;
            _dataService = dataService;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            try
            {
                // Code before the action
                _logger.LogInformation("Before action: {ActionName}", context.ActionDescriptor.DisplayName);
                // Execute the action
                var resultContext = await next();
                // Code after the action
               _logger.LogInformation("After action: {ActionName}", context.ActionDescriptor.DisplayName);              
                CustomerDataAtInitiationAndApproval customerDataAtInitiationAndApproval = _dataService.GetDataService();
                _logger.LogInformation("customerDataAtInitiationAndApproval "+ customerDataAtInitiationAndApproval);
                Task.Run(async () =>
                {
                    var response = await _genServ.CallServiceAsyncToString(Method.POST, "https://localhost:44306/StaffUser/SendMailAllToAuthorizerOrAdmin", customerDataAtInitiationAndApproval, true);
                    _logger.LogInformation($"response {response}");
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in LoggingActionFilter: {Message}", ex.Message);
            }
        }
    }
}
