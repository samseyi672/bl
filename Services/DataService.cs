using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;

namespace Retailbanking.BL.Services
{
    public class DataService : IDataService
    {
       private CustomerDataAtInitiationAndApproval _customerDataAtInitiationAndApproval;

        /*
        public DataService(CustomerDataAtInitiationAndApproval ustomerDataAtInitiationAndApproval)
        {
            _customerDataAtInitiationAndApproval = ustomerDataAtInitiationAndApproval;
        }
        */

        public CustomerDataAtInitiationAndApproval GetDataService()
        {
            return _customerDataAtInitiationAndApproval;
        }

        public void SetDataService(CustomerDataAtInitiationAndApproval customerDataAtInitiationAndApproval)
        {
            _customerDataAtInitiationAndApproval=customerDataAtInitiationAndApproval;
        }
    }
}






