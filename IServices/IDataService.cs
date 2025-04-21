using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;

namespace Retailbanking.BL.IServices
{
    public  interface IDataService
    {
        CustomerDataAtInitiationAndApproval GetDataService();
        void SetDataService(CustomerDataAtInitiationAndApproval customerDataAtInitiationAndApproval);
    }
}
