using Microsoft.AspNetCore.Mvc.RazorPages;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface IPinService
    {
        Task<GenericResponse2> ChangePin(CustomerPin customerPin);
        Task<GenericResponse2> GetForgotPinRequest(int page=0, int size=10);
        Task<GenericResponse2> ForgotPin(string Username , string Session , int ChannelId, string request);
        Task<GenericResponse2> AssetCapitalInsuranceForgotPin(string Username, string Session, int ChannelId, string request, string UserType);
        Task<GenericResponse2> AssetCapitalInsuranceChangePin(CustomerPin customerPin, string UserType);
        Task<GenericResponse2> GetAssetCapitalInsuranceForgotPinRequest(int page, int size,string UserType);
    }
}











































































