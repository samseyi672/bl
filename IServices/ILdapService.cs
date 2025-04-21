using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;

namespace Retailbanking.BL.IServices
{
    public interface ILdapService
    {
        bool Authenticate(string username, string password);
        object getAllAdusers(int page , int size);
        List<string> SearchStaffusers(string Search);
        bool WindowADAuthentication(string username, string password);
        string GenerateJwtToken(string username,StaffRoleAndPermission staffRoleAndPermission);
        string GenerateJwtToken(string username);
    }
}
