using MySqlX.XDevAPI;
using Retailbanking.Common.CustomObj;
using Retailbanking.Common.DbObj;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface ITransfer
    {
        Task<List<double>> GetTopAmountSent(string ClientKey, TopAmount request);
        Task<BankList> GetPossibleBanks(string ClientKey, string AccountNumber);
       // int GetLastDigit(string accountnumber, string bankcode);
    //    Task<BankList> GetAllBanks();
        Task<ValidateAccountResponse> ValidateNumber(string ClientKey, ValidateAccountNumber Request);
        Task<TransResponse> TransferFunds(string ClientKey, TransferRequestSingle Request);
        Task<TransResponse> TransferFund(string ClientKey, TransferRequestSingle Request);
        Task<TransResponse> RemoveBenficiary(string ClientKey, string Username, string CustomerName, string Session, int ChannelId);
        Task<TransResponse> forgetBenficiary(string ClientKey, int beneficiaryId,string Username, string Session, int ChannelId);
        Task<GetTransactionStatus> GetCustomerInterBankTransactionusStatusForOutwardInService(string transRef, string Username, int ChannelId, string Session);
        Task<GenericResponse2> GetCustomerInterBankTransactionusStatusForOutward(string transRef, string Username, int ChannelId,string Session);
        Task<TransResponse> TransferFunds(TransferRequestSingle Request, IDbConnection con, Users usr);
        Task<GetChargeResponse> GetCharge(string ClientKey, GetChargeRequest Request);
        Task<GenericResponse> eduWaecPayment(string clientKey, WaecPaymentDTO request);
        Task<GenericResponse> AirtimePurchase(string clientKey, AirTimePurchase airTimePurchase);
        Task<GenericResponse> DataPurchase(string clientKey, DataPurchase dataPurchase);
        Task<GenericResponse> ValidateVTUPhoneNumber(string clientKey, ValidatePhoneNumber validatePhoneNumber);
        Task<GenericResponse> McnValidateTVforUpgradeOrdowngrade(string clientKey, TVValidate tVValidate);
        Task<GenericResponse> mcnpaymentforupgradeordowngrade(string clientKey, TvPayment tvPayment);
        Task<GenericResponse> mcnvalidationforrenewal(string clientKey, McnTvValidationRenewal renewalTvValidation);
        Task<GenericResponse> mcnpaymentforrenewal(string clientKey, McnRenewalTvPayment mcnRenewalTvPayment);
        Task<GenericResponse> starTimeValidation(string clientKey, StarTimeValidation starTimeValidation);
        Task<GenericResponse> starTimepayment(string clientKey, StarTimepayment starTimepayment);
        Task<GenericResponse> validateDiscoCustomer(string clientKey, ValidateDisco validateDisco);
        Task<GenericResponse> DiscoPayment(string clientKey, DiscoPayment discoPayment);
        Task<GenericResponse> validateInternetCustomerId(string clientKey, InternetSubscription internetSubscription);
        Task<GenericResponse> InternetSubscriptionPayment(string clientKey, InternetSubscriptionPayment internetSubscriptionPayment);
        Task<GenericResponse> validateCustomerKyc(string clientKey, KycCustomerValidation kycCustomerValidation);
        Task<GenericResponse> GetAllBillerCategories(string clientKey, string Username, string Session, int ChannelId);
        Task<GenericResponse> Getallbillers(string clientKey,string Username,string Session,int ChannelId);
        Task<GenericResponse> GetMyBillers(string clientKey, string Username, string Session, int ChannelId);
        Task<GenericResponse> GetBouquets(string clientKey, string Username, string Session, int ChannelId,string catId,string billerId);
        Task<GenericResponse> ValidateBet(string clientKey, Bet bet);
        Task<GenericResponse> BetPayment(string clientKey, BetPayment genericRequest);
        Task<GenericResponse> RequeryTransaction(string clientKey, string requery, string Username, string Session, int ChannelId);
        Task<GenericResponse> DebitCustomer(BillPaymentGLPoster request);
        Task<GenericResponse> DebitCustomerOnInvestmentGL(BillPaymentGLPoster request, DebitMe debitMe, Users usr);
        Task<GenericResponse> checkIfbillServerIsUp(string Username, string Session, int ChannelId);
        Task<GenericResponse> DebitCustomerOnGL(BillPaymentGLPoster request);// this is working.
        Task<GenericResponse> DebitCustomerOnGL(BillPaymentGLPoster request,DebitMe debitMe,string GL);
        Task<GenericResponse> ChargeCustomer(TransferRequestSingle Request,GetChargeResponse charge);
        IDbConnection GetConnection();
        Task<GenericResponse> saveReversal(DebitMe debitMe, string username, IDbConnection con, int ChannelId);
        Task<GetChargeResponse> GetCharge(GetChargeRequest Request);
        Task<GenericResponse> DebitCustomerOnGL(BillPaymentGLPoster request,DebitMe debitMe, Users usr,string GL);// this is working.
        Task<GenericResponse> GetPaymentRecord(string username, string session, string transRefId,int ChannelId);
        void sendTransactionEmail(TransactionListRequest transactionListRequest, string sourceAccountNo,IDbConnection con,string type=null);
        Task<GenericResponse> GetTransferRecordByTransRefOrId(string clientKey, string transRef, string userName, string session,int ChannelId);
        Task<GenericResponse> ReportTransaction(string clientKey, ReportTransaction request);
        Task<GenericResponse> GetBeneficiariesIucOrMeterNumber(string clientKey,string Username, string session, int channelId,int devicetype,string deviceaccount);
        Task<GenericResponse> SaveBeneficiariesIucOrMeterNumber(string clientKey, SaveBeneficiariesIucOrMeterNumber saveBeneficiariesIucOrMeterNumber);
        Task<GenericResponse> DeleteBeneficiariesIucOrMeterNumber(string clientKey, string username, string iucOrMeterAccount, string session, int channelId);
        Task<GenericResponse> GetAccountLimitPerDay(decimal Amount,string Account,string username,string Session,int ChannelID);
        //  Task DebitCustomerOnGL(BillPaymentGLPoster billPaymentGLPoster, DebitMe debitMe, string vatChargesGL);
        Task<TransLimitValidation> CheckCustomerDailyandSingleTransactionLimit(string clientKey, string username, string session, int channelId,decimal Amount,string AccountNumber=null);
        Task<GenericResponse2> ValidateCustomerPin(string clientKey, PinValidationChecker request);
    }
}

























































