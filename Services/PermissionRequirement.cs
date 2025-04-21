using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;
using System.Text;

namespace Retailbanking.BL.Services
{
    public class PermissionRequirement:IAuthorizationRequirement
    {
        public string[] Permission { get; }
        public PermissionRequirement(params string[] permission)
        {
            Permission = permission;
        }

    }
}



















































