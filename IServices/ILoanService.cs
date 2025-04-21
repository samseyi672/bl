using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface ILoanService
    {
        Task<GenericResponse> createRetailLoan(RetailLoan retailLoan);
        Task<GenericResponse> createPublicSectorLoan(PublicSectorLoan publicSectorLoan);
        Task<GenericResponse> GetLoanTypes();
        Task<GenericResponse> GetLoanRequestTypes();
        Task<GenericResponse> ViewEvaluation(PublicLoanEvaluation publicLoanEvaluation);
        Task<GenericResponse> SubmitEvaluation(SubmitLoanEvaluation submitLoanEvaluation);
        Task<GenericResponse> SecondEvaluation(SecondEvaluation secondEvaluation);
        Task<GenericResponse> FirstEvaluation(FirstEvaluation firstEvaluation);
        Task<GenericResponse> CalculateRetailLoan(RetailLoanCalculator retailLoanCalculator);
        Task<GenericResponse> ViewLoanHistory(string session, string userName, int channelId);
    }
}
