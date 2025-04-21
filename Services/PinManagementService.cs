using Dapper;
using Microsoft.Extensions.Logging;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.Services
{
    public class PinManagementService : IPinManagementService
    {
        private readonly ILogger<TransferServices> _logger;
        private readonly IGeneric _genServ;
        private readonly DapperContext _context;
        private readonly ISmsBLService _smsBLService;
        private readonly INotification _notification;
        private readonly IRedisStorageService _redisStorageService;
        private readonly IStaffUserService _staffUserService;

        public PinManagementService(ILogger<TransferServices> logger, IGeneric genServ, DapperContext context, ISmsBLService smsBLService, INotification notification, IRedisStorageService redisStorageService, IStaffUserService staffUserService)
        {
            _logger = logger;
            _genServ = genServ;
            _context = context;
            _smsBLService = smsBLService;
            _notification = notification;
            _redisStorageService = redisStorageService;
            _staffUserService = staffUserService;
        }

        public async Task<GenericResponse2> PinApproval(PinApproval pinApproval)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var usr = await _genServ.GetUserbyUsername(pinApproval.username,con);
                pinApproval.newpin = await _genServ.GenerateUnitID(4);
                string name = pinApproval.StaffNameAndRole.Contains("_")?pinApproval.StaffNameAndRole.Split('_')[0]:pinApproval.StaffNameAndRole; // this will be staff that will approves
                var initiationStaff = (await con.QueryAsync<string>("select (select email from staff s where s.id=sf.initiationstaff) as email from staffaction sf where sf.id=@id", new { id = pinApproval.actionid })).FirstOrDefault();
                if (initiationStaff.Replace("@trustbancgroup.com", "").Trim().Equals(name.Trim(), StringComparison.CurrentCultureIgnoreCase))
                {
                    return new GenericResponse2() { Success = true, Response = EnumResponse.YouareNottheOne };
                }
                var validateActionId = (await con.QueryAsync<int>("select id from staffaction where id=@id", new { id = pinApproval.actionid })).FirstOrDefault();
                if (validateActionId == 0)
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.NoAccountExist };
                }
                //get approvalstaff
                var stafftoapprove = (await con.QueryAsync<int>("select id from staff where email=@email", new { email = name + "@trustbancgroup.com" })).FirstOrDefault();
                var PendingAction = (await con.QueryAsync<PendingStaffActionOnProfile>("select st.id,(select c.actioname from checkeraction c where c.id=st.action) as action from staffaction st where st.approvalstatus=false and st.id=@id", new { id = pinApproval.actionid })).FirstOrDefault();
                if (PendingAction == null)
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.ActionLikelyApprove };
                }
                if (pinApproval.approveordeny.Equals("approve", StringComparison.CurrentCultureIgnoreCase))
                {
                    if (PendingAction.action.ToLower() == "initiatepinapproval")
                    {
                        await con.ExecuteAsync("update staffaction set approvalstatus=true,approveordeny=@approve,approvalstaff=@approvalstaff where id=@id", new { approve = pinApproval.approveordeny, id = pinApproval.actionid, approvalstaff = stafftoapprove });
                        //set pin for customer
                       // pinApproval.newpin = _genServ.GeneratePin();
                        var encriptedPin = _genServ.EncryptString(pinApproval.newpin);
                        //update the pin
                        string query = $@"update user_credentials set temporarypin='y',credential=@cred,createdon=now() where userid=@userid and credentialtype=@credentialtype";
                        await con.ExecuteAsync(query, new { cred = encriptedPin, userid = usr.Id, credentialtype = CredentialType.TransactionPin });
                        _logger.LogInformation("pin set successfully ......");
                        await con.ExecuteAsync("update pinrequestchange set approvalstatus=true where userid=@id",new { id=usr.Id});
                        string otp = _genServ.GenerateOtp();
                        CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)usr.Id);
                        //customerDataNotFromBvn.PhoneNumber = CustomerServiceNotFromBvnService.ReplaceFirstDigit(customerDataNotFromBvn.PhoneNumber, "234");
                        SendMailObject sendMailObject = new SendMailObject();
                        sendMailObject.Subject = "TrustBanc Mobile Banking Account PIN Reset Approval Request";
                        sendMailObject.Email = customerDataNotFromBvn.Email;
                        sendMailObject.Html = $@"<p>Dear {usr.Firstname} {usr.LastName}</p>,
                                    <p>We have approved your request to reset the Transaction PIN for your TrustBanc Mobile Banking account.Pin is {pinApproval.newpin}</p>
                                    <p>Thank you for choosing TrustBanc</p>
                                     ";
                        Task.Run(async () => {
                            await _genServ.SendOtp4(OtpType.PinResetOrChange, pinApproval.newpin, customerDataNotFromBvn?.PhoneNumber+"_"+$"{usr.Firstname} {usr.LastName}", _smsBLService, "Confirmation", customerDataNotFromBvn?.Email);
                            //_genServ.SendOtp2();
                          //  _genServ.SendMail(sendMailObject);
                        });
                        return new GenericResponse2() { Response = EnumResponse.Successful,Success=true };
                    }
                }else if (pinApproval.approveordeny.Equals("deny", StringComparison.CurrentCultureIgnoreCase))
                {
                    await con.ExecuteAsync("update staffaction set approvalstatus=false,approveordeny=@approve,approvalstaff=@approvalstaff where id=@id", new { approve = pinApproval.approveordeny, id = pinApproval.actionid, approvalstaff = stafftoapprove });
                    CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)usr.Id);
                    SendMailObject sendMailObject = new SendMailObject();
                    sendMailObject.Subject = "Transaction PIN Reset Request for Your TrustBanc Mobile Banking Account";
                    sendMailObject.Email = customerDataNotFromBvn.Email;
                    sendMailObject.Html = $@"Dear {usr.Firstname} {usr.LastName},
                                            Your pin change request was not successful .
                                            Thank you for choosing TrustBanc
                                            ";
                    Task.Run(async () => {
                        await _genServ.SendOtp3(OtpType.PinResetOrChange, pinApproval.newpin, customerDataNotFromBvn?.PhoneNumber, _smsBLService, "Confirmation", customerDataNotFromBvn?.Email);
                        _genServ.SendMail(sendMailObject);
                    });
                    return new GenericResponse2(){ Response = EnumResponse.Successful, Success = true };
                }
                return new GenericResponse2() { Response = EnumResponse.NotSuccessful, Success = false };
            }
            catch (Exception ex)
            {
              return new GenericResponse2(){ Response=EnumResponse.NotSuccessful,Message=ex.Message};
            }
        }

        public async Task<GenericResponse2> InitiatePinApproval(string action, string username, string StStaffNameAndRole)
        {
            try
            {
                using IDbConnection con=_context.CreateConnection();
                var usr = await _genServ.GetUserbyUsername(username,con);
                await con.ExecuteAsync("update pinrequestchange set initiated=true where userid=@id",new { id=usr.Id});
                GenericResponse genericResponse = await _staffUserService.InitiateTask((int)usr.Id,action,StStaffNameAndRole); 
                return new GenericResponse2() { Response = EnumResponse.Successful,Success=true };
            }
            catch (Exception ex)
            {
                return new GenericResponse2() {Response=EnumResponse.NotSuccessful,Message=ex.Message};
            }
        }

        public async Task<GenericResponse2> GetListOfInitiatedPinApproval(int page, int size)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                int skip = page == 0 ? page : (page - 1) * size;
                int take = size;
                string query = $@"select sta.id as actionid,(select actioname from checkeraction where id=sta.action) as actioname,concat((select firstname from users s where s.id=sta.staffidtoaction),' ',(select lastname from users s where s.id=sta.staffidtoaction)) as name,(select email from staff s where s.id=sta.initiationstaff) as email,(select Username from users s where s.id=sta.staffidtoaction) as Username from staffaction sta where sta.action=10 and sta.approvalstatus=false";
                var PendingStaff = (await con.QueryAsync<Staff2>(query)).ToList();
               // var request = (await con.QueryAsync<PinRequestchange>("select u.username,u.Firstname,u.Lastname,p.request as reason,p.createdon,u.id as UserId from users u join pinrequestchange p on u.id=p.userid LIMIT @Take OFFSET @Skip;", new { Take = take, Skip = skip })).ToList();
                return new GenericResponse2() { data = PendingStaff, Success = true, Response = EnumResponse.Successful };
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex.Message);
                return new GenericResponse2() { Response = EnumResponse.NotSuccessful };
            }
        }
    }
}










































































