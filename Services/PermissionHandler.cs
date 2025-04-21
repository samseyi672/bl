using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.Services
{
    public class PermissionHandler : AuthorizationHandler<PermissionRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            var userClaims = context.User.Claims;

            foreach (var permission in requirement.Permission)
            {
                if (userClaims.Any(c => c.Type == "Permission" && c.Value == permission))
                {
                    Console.WriteLine("permission check ", permission.ToString());
                    context.Succeed(requirement);
                    return Task.CompletedTask;
                }
            }

            return Task.CompletedTask;
        }
    }

}

























































































































