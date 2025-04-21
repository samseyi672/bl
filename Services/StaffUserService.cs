using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Org.BouncyCastle.Asn1.Ocsp;
using RestSharp.Validation;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using Retailbanking.Common.DbObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Permissions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static iText.StyledXmlParser.Jsoup.Select.Evaluator;

namespace Retailbanking.BL.Services
{
    public class StaffUserService : IStaffUserService
    {
        private readonly ILogger<AuthenticationServices> _logger;
        private readonly AppSettings _settings;
        private readonly SmtpDetails smtpDetails;
        private readonly LdapSettings _ldapSettings;
        private readonly IGeneric _genServ;
        private readonly IMemoryCache _cache;
        private readonly DapperContext _context;
        private readonly ILdapService _ldapService;
        private readonly ISmsBLService _smsBLService;

        public StaffUserService(ISmsBLService smsBLService, ILdapService ldapService, ILogger<AuthenticationServices> logger, IOptions<LdapSettings> ldapSettings, IOptions<AppSettings> options, IOptions<SmtpDetails> options1, IGeneric genServ, IMemoryCache memoryCache, DapperContext context)
        {
            _logger = logger;
            _smsBLService = smsBLService;
            _settings = options.Value;
            smtpDetails = options1.Value;
            _genServ = genServ;
            _cache = memoryCache;
            _context = context;
            _ldapSettings = ldapSettings.Value;
            _ldapService = ldapService;
        }

        public async Task<GenericResponse> InitiationDeleteActionOnStaffProfile(int staffid, string action, string StaffNameAndRole)
        {
            try
            {

                using IDbConnection con = _context.CreateConnection();
                var actions = (await con.QueryAsync<string>("select actioname from checkeraction")).ToList();
                _logger.LogInformation("action " + action + " actions " + string.Join(",", actions));
                Console.WriteLine("action " + action + " actions " + string.Join(",", actions));
                if (!actions.Any(a => a.Equals(action, StringComparison.OrdinalIgnoreCase)))
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.WrongAction };
                }
                var InitiatedActionId = (await con.QueryAsync<int>("select id from checkeraction where actioname=@action", new { action = action })).FirstOrDefault();
                if (InitiatedActionId == 0)
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.WrongAction };
                }
                // checking for staff validity 
                _logger.LogInformation("staffid " + staffid);
                _logger.LogInformation("StaffNameAndRole " + StaffNameAndRole);
                var staffidvalidity = (await con.QueryAsync<int>("select id from staff where id=@id", new { id = staffid })).FirstOrDefault();
                if (staffidvalidity == 0)
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.StaffNotExists };
                }
                _logger.LogInformation("StaffNameAndRole " + StaffNameAndRole);
                var name = StaffNameAndRole.Contains("_") ? StaffNameAndRole.Split('_')[0] : StaffNameAndRole;
                var staffid2 = (await con.QueryAsync<int>("select id from staff where email=@email", new { email = name + "@trustbancgroup.com" })).FirstOrDefault();
                _logger.LogInformation("staffid2 " + staffid2);
                _logger.LogInformation("going to insert ....");

                await con.ExecuteAsync($@"insert into staffaction(action,initiationstaff,choosenstaff,staffidtoaction)
                                          values(@action,@initiationstaff,@choosenstaff,@staffidtoaction)", new { action = InitiatedActionId, initiationstaff = staffid2, choosenstaff = "", staffidtoaction = staffid });
                _logger.LogInformation("initiation successful ....");
                return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public PrimeAdminResponse GetAllUsers(int page, int size)
        {
            return new PrimeAdminResponse() { Response = EnumResponse.Successful, Data = _ldapService.getAllAdusers(page, size), Success = true };
        }

        public async Task<GenericResponse> GetPermissions(string roleName)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var roleid = (await con.QueryAsync<int>("select id from role where rolename=@rolename", new { rolename = roleName })).FirstOrDefault();
                var permissions = (await con.QueryAsync<string>("select permssion from permssions where roleid=@roleid", new { roleid = roleid })).ToList();
                return new PrimeAdminResponse() { Response = EnumResponse.Successful, Success = true, Data = permissions };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> GetProfiledStaff(int page, int size)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                int skip = page == 0 ? page : (page - 1) * size;
                int take = size;
                var profiledStaff = (await con.QueryAsync<FirstNameAndLastname>("select firstname,lastname from staff LIMIT @Take OFFSET @Skip;", new { Take = take, Skip = skip })).ToList();
                return new PrimeAdminResponse() { Success = true, Data = profiledStaff, Response = EnumResponse.Successful };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> GetProfiledStaffWithAuthorities(int page, int size)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                int skip = page == 0 ? page : (page - 1) * size;
                int take = size;
                // string query = $@"select sr.staffid,(select firstname from staff s where s.id=sr.staffid) as firstname,(select lastname from staff s where s.id=sr.staffid) as lastname,(select r.rolename from role r where r.id=sr.staffrole) as staffrole,sp.roleid,group_concat((select permssion from permssions p where p.id=sp.permission) order by sp.permission separator ',') as permissions from staffrole sr join staffpermission sp on sr.staffid=sp.staffid group by sr.staffrole LIMIT @Take OFFSET @Skip;";
                //string query = "select sr.staffid,(select firstname from staff s where s.id=sr.staffid) as firstname,(select lastname from staff s where s.id=sr.staffid) as lastname,(select r.rolename from role r where r.id=sr.staffrole) as staffrole,sp.roleid,group_concat((select permssion from permssions p where p.id=sp.permission) order by sp.permission separator ',') as permissions from staffrole sr join staffpermission sp on sr.staffid=sp.staffid group by sr.staffid LIMIT @Take OFFSET @Skip;";
                string query = $@"select sr.staffid,(select firstname from staff s where s.id=sr.staffid) as firstname,(select lastname from staff s where s.id=sr.staffid) as lastname,(select r.rolename from role r where r.id=sr.staffrole) as staffrole,sp.roleid,group_concat((select permssion from permssions p where p.id=sp.permission) order by sp.permission separator ',') as permissions from staffrole sr 
                                    join staffpermission sp on sr.staffid=sp.staffid 
                                    join staff s on s.id=sr.staffid where s.approvalstatus=true 
                                    group by sr.staffid LIMIT @Take OFFSET @Skip;";
                var stafflist = (await con.QueryAsync<Staff>(query, new { Take = take, Skip = skip })).ToList();
                return new PrimeAdminResponse() { Success = true, Data = stafflist, Response = EnumResponse.Successful };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }
        public async Task<GenericResponse> GetRoles()
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var roles = (await con.QueryAsync<string>("select rolename from role")).ToList();
                return new PrimeAdminResponse() { Response = EnumResponse.Successful, Success = true, Data = roles };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> GetRolesAndPermissions(string email)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                StaffRoleAndPermission staffRoleAndPermission = new StaffRoleAndPermission();
                var staffid = (await con.QueryAsync<int>("select id from staff where email=@email", new { email = email.Contains("@trustbancgroup.com") ? email : email + "@trustbancgroup.com" })).FirstOrDefault();
                if (staffid != 0)
                {
                    var staffrole = (await con.QueryAsync<string>("select (select r.rolename from role r where r.id=sr.staffrole) as staffrole from staffrole sr where sr.staffid=@staffid", new { staffid = staffid })).FirstOrDefault();
                    var staffPermission = (await con.QueryAsync<string>("select(select permssion from permssions where id = p.permission) as permission from staffpermission p where p.staffid = @staffid", new { staffid = staffid })).ToList();
                    staffRoleAndPermission.role = staffrole;
                    staffRoleAndPermission.Permissions = staffPermission;
                }
                return new PrimeAdminResponse() { Success = true, Response = EnumResponse.Successful, Data = new { role = staffRoleAndPermission.role, permission = staffRoleAndPermission.Permissions } };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
            //return new PrimeAdminResponse() { Success=true,Response=EnumResponse.Successful};
        }

        public async Task<GenericResponse> GetStaffPermissions(int staffid)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var permissions = (await con.QueryAsync<string>("select (select permssion from permssions p where p.id=sp.permission) as permission from staffpermission sp where sp.staffid=@staffid", new { staffid = staffid })).ToList();
                return new PrimeAdminResponse() { Success = true, Response = EnumResponse.Successful, Data = new { permission = permissions } };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }
        public async Task<GenericResponse> LoginStaff(string UserName, string password)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                //  var AuthenticUser = _ldapService.Authenticate(UserName, password);
                // _logger.LogInformation("AuthenticUser " + AuthenticUser);
                var AuthenticUser = true;
                StaffRoleAndPermission staffRoleAndPermission = new StaffRoleAndPermission();
                // var staffid = (await con.QueryAsync<int>("select id from staff where email=@email",new {email=UserName.Contains("@trustbancgroup.com")?UserName:UserName.Concat("@trustbancgroup.com") })).FirstOrDefault();
                var staffid = (await con.QueryAsync<int>(
                                        "SELECT id FROM staff WHERE email = @Email and approvalstatus=true",
                                        new { Email = UserName.Contains("@trustbancgroup.com") ? UserName : UserName + "@trustbancgroup.com" }
                                    )).FirstOrDefault();

                if (staffid != 0)
                {
                    var staffrole = (await con.QueryAsync<string>("select (select r.rolename from role r where r.id=sr.staffrole) as staffrole from staffrole sr where sr.staffid=@staffid", new { staffid = staffid })).FirstOrDefault();
                    var staffPermission = (await con.QueryAsync<string>("select (select permssion from permssions where id=p.permission) as permission from staffpermission p where p.staffid=@staffid", new { staffid = staffid })).ToList();
                    if (!string.IsNullOrEmpty(staffrole) && staffPermission.Any())
                    {
                        staffRoleAndPermission.staffid = staffid;
                        staffRoleAndPermission.role = staffrole;
                        staffRoleAndPermission.Permissions = staffPermission;
                    }
                    /*
                    else {
                        
                        if (staffrole==null)
                        {
                            staffRoleAndPermission.role = "admin";
                        }
                    }
                */
                }
                else
                {
                    return new GenericResponse { Response = EnumResponse.UserNotProfiled };
                }
                //var AuthenticUser = true;       
                _logger.LogInformation("AuthenticUser " + AuthenticUser);
                string token = await Task.Run(() => _ldapService.GenerateJwtToken(UserName, staffRoleAndPermission));
                Console.WriteLine("token " + token);
                return AuthenticUser ? (new PrimeAdminResponse()
                {
                    Response = EnumResponse.Successful,
                    Data = new { token = token, authorities = staffRoleAndPermission },
                    Success = true
                }) : new GenericResponse() { Response = EnumResponse.NotSuccessful, Success = false };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> ProfileStaff(ProfileStaff profileStaff, string InitiatorStaffNameAndRole = null)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var staffid = (await con.QueryAsync<string>("select id from staff where email=@email", new { email = profileStaff.FirstName + "." + profileStaff.LastName + "@trustbancgroup.com" })).FirstOrDefault();
                if (!string.IsNullOrEmpty(staffid))
                {
                    //for roles 
                    List<int> roleId = new List<int>();
                    if (!profileStaff.roles.Any())
                    {
                        throw new ArgumentNullException("Roles cannot be empty");
                    }
                    if (profileStaff.roles.Count() > 1)
                    {
                        throw new ArgumentNullException("Only one role can be assigned");
                    }
                    if (!profileStaff.RolesAndPermission.Any())
                    {
                        throw new ArgumentNullException("Permissions cannot be empty");
                    }
                    int roleinsertedId = 0;
                    foreach (string rolename in profileStaff.roles)
                    {
                        var queryrole = @"
                                            UPDATE staffrole 
                                            SET staffrole = @staffrole
                                            WHERE staffid = @staffid;
                                            SELECT staffrole FROM staffrole WHERE staffid = @staffid;";
                        var roleid = (await con.QueryAsync<int>("select id from role where rolename=@rolename", new { rolename = rolename })).FirstOrDefault();
                        roleinsertedId = await con.QuerySingleAsync<int>(queryrole, new
                        {
                            staffrole = roleid,
                            staffid = int.Parse(staffid)
                        });
                        new Thread(() =>
                        {
                            SendMailObject sendMailObject = new SendMailObject();
                            sendMailObject.Subject = "PrimeApp office Staff Profiling";
                            sendMailObject.Email = $@"{profileStaff.FirstName}.{profileStaff.LastName}@trustbancgroup.com";
                            sendMailObject.Html = $"Dear {profileStaff.FirstName.ToUpper()},kindly know that you have been profiled on the PrimeApp Admin with role-{(string.Join(",", profileStaff.roles))}";
                            _genServ.SendMail(sendMailObject); // notify the user  
                        }).Start();
                        //clear the permission table for the user
                    }
                    await con.ExecuteAsync("delete from staffpermission where staffid=" + int.Parse(staffid));
                    List<RoleAndPermissions> roleAndPermissions = profileStaff.RolesAndPermission;
                    //for permissions 
                    List<string> rolesofPermission = roleAndPermissions.Select(e => e.RoleName).ToList();
                    //compare the two list to see they are equal
                    var areEqual = rolesofPermission.OrderBy(x => x).SequenceEqual(profileStaff.roles.OrderBy(x => x));
                    if (!areEqual)
                    {
                        return new PrimeAdminResponse() { Success = false, Response = EnumResponse.TheListAreNotEqueal };
                    }
                    foreach (RoleAndPermissions rp in profileStaff.RolesAndPermission)
                    {
                        List<string> list = rp.Permissions;
                        if (list.Any())
                        {
                            foreach (string permissionname in list)
                            {
                                //var permid = (await con.QueryAsync<int>("select id from permssions where permssion=@permname",new {permname=permissionname}).FirstOrDefault();
                                var permid = (await con.QueryAsync<int>(
                                                                    "select id from permssions where permssion=@permname",
                                                                    new { permname = permissionname }
                                                                )).FirstOrDefault();
                                var permissionquery = @"
                                                            INSERT INTO staffpermission (roleid,staffid,permission)
                                                            VALUES (@roleid, @staffid,@permission);
                                                            SELECT LAST_INSERT_ID()";
                                var perminsertedId = await con.QuerySingleAsync<int>(permissionquery, new
                                {
                                    roleid = roleinsertedId,
                                    staffid = staffid,
                                    permission = permid
                                });
                            }
                        }
                        else
                        {
                            return new GenericResponse() { Success = true, Response = EnumResponse.NoPermissions };
                        }
                    }
                    await con.ExecuteAsync("update staff set approvalstatus=false where id=@staffid", new { staffid = int.Parse(staffid) });
                    await con.ExecuteAsync("delete from staffaction where staffidtoaction=@staffid and action=1", new { staffid = int.Parse(staffid) });
                    if (InitiatorStaffNameAndRole != null)
                    {
                        InitiatorStaffNameAndRole = InitiatorStaffNameAndRole.Contains("_") ? InitiatorStaffNameAndRole.Split('_')[0] : InitiatorStaffNameAndRole;

                    }
                    return await InitiationDeleteActionOnStaffProfile(int.Parse(staffid), "create", InitiatorStaffNameAndRole + "_" + "rolename");
                    //return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
                }
                else
                {
                    var query = @"
                            INSERT INTO staff (firstname, lastname, email)
                            VALUES (@FirstName, @LastName, @Email);
                            SELECT LAST_INSERT_ID()";
                    var insertedId = await con.QuerySingleAsync<int>(query, new
                    {
                        FirstName = profileStaff.FirstName,
                        LastName = profileStaff.LastName,
                        Email = profileStaff.FirstName + "." + profileStaff.LastName + "@trustbancgroup.com"
                    });
                    new Thread(() =>
                    {
                        SendMailObject sendMailObject = new SendMailObject();
                        sendMailObject.Subject = "PrimeApp Backoffice Profiling";
                        sendMailObject.Email = $@"{profileStaff.FirstName}.{profileStaff.LastName}@trustbancgroup.com";
                        sendMailObject.Html = $"Dear {profileStaff.FirstName.ToUpper()},Kindly know that you have been profiled on the PrimeApp Admin with role-{(String.Join(",", profileStaff.roles))}";
                        _genServ.SendMail(sendMailObject); // notify the user  
                    }).Start();
                    //for roles 
                    List<int> roleId = new List<int>();
                    foreach (string rolename in profileStaff.roles)
                    {
                        var roleid = (await con.QueryAsync<int>("select id from role where rolename=@rolename", new { rolename = rolename })).FirstOrDefault();
                        var queryrole = @"
                            INSERT INTO staffrole(staffrole,staffid)
                            VALUES (@staffrole, @staffid);
                            SELECT LAST_INSERT_ID()";
                        var roleinsertedId = await con.QuerySingleAsync<int>(queryrole, new
                        {
                            staffrole = roleid,
                            staffid = insertedId
                        });
                        // roleId.Add(roleinsertedId);
                        List<RoleAndPermissions> roleAndPermissions = profileStaff.RolesAndPermission;
                        //for permissions 
                        List<string> rolesofPermission = roleAndPermissions.Select(e => e.RoleName).ToList();
                        //compare the two list to see they are equal
                        var areEqual = rolesofPermission.OrderBy(x => x).SequenceEqual(profileStaff.roles.OrderBy(x => x));
                        if (!areEqual)
                        {
                            return new PrimeAdminResponse() { Success = false, Response = EnumResponse.TheListAreNotEqueal };
                        }
                        foreach (RoleAndPermissions rp in profileStaff.RolesAndPermission)
                        {
                            List<string> list = rp.Permissions;
                            if (list.Any())
                            {
                                foreach (string permissionname in list)
                                {
                                    var permid = (await con.QueryAsync<int>(
                                                                        "select id from permssions where permssion=@permname",
                                                                        new { permname = permissionname }
                                                                    )).FirstOrDefault();
                                    var permissionquery = @"
                            INSERT INTO staffpermission (roleid,staffid,permission)
                            VALUES (@roleid, @staffid,@permission);
                            SELECT LAST_INSERT_ID()";
                                    var perminsertedId = await con.QuerySingleAsync<int>(permissionquery, new
                                    {
                                        roleid = roleinsertedId,
                                        staffid = insertedId,
                                        permission = permid
                                    });
                                }
                            }
                        }
                    }
                    return await InitiationDeleteActionOnStaffProfile(insertedId, "create", profileStaff.FirstName + "." + profileStaff.LastName + "_" + "rolename");
                    //  return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
                }
                // return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public PrimeAdminResponse SearchStaffUsers(string Search)
        {
            return new PrimeAdminResponse() { Response = EnumResponse.Successful, Data = _ldapService.SearchStaffusers(Search), Success = true };
        }

        public async Task<GenericResponse> GetPendingDeleteActionOnStaffProfile()
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
               // var PendingAction = (await con.QueryAsync<PendingStaffActionOnProfile>("select st.id as actionid,(select actioname from checkeraction where id=st.action) as actioname,st.action,st.choosenstaff from staffaction st where st.approvalstatus=false and st.action in (2)")).ToList();
               // PendingAction.ForEach(e => e.choosenstaff.Replace("@trustbancgroup.com", ""));
                string query = $@"
                                    SELECT sta.id as actionid,sta.approveordeny as approveordeny,
                                    CONCAT(staff1.firstname, ' ', staff1.lastname) AS initiationstaff,
                                    sr.staffid,
                                    s.firstname,
                                    s.lastname,
                                    r.rolename AS staffrole,
                                    sp.roleid,
                                    GROUP_CONCAT(p.permssion ORDER BY sp.permission SEPARATOR ',') AS permissions
                                FROM 
                                    staffrole sr
                                    JOIN staffpermission sp ON sr.staffid = sp.staffid
                                    JOIN staff s ON s.id = sr.staffid
                                    JOIN role r ON r.id = sr.staffrole
                                    JOIN permssions p ON p.id = sp.permission
                                    JOIN staffaction sta ON sta.staffidtoaction = s.id and sta.action=2
                                    JOIN staff staff1 ON staff1.id = sta.initiationstaff
                                WHERE 
                                    s.approvalstatus = true and sta.approveordeny='Awaiting approval or deny'
                                GROUP BY 
                                    sr.staffid;";
                var PendingStaffDelete = (await con.QueryAsync<Staff>(query)).ToList();
                return new PrimeAdminResponse() { Response = EnumResponse.Successful, Data = PendingStaffDelete, Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> DeleteStaffProfile(int actionid, string staffNameAndRole, string approveordeny)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var validateActionId = (await con.QueryAsync<int>("select id from staffaction where id=@id", new { id = actionid })).FirstOrDefault();
                if (validateActionId == 0)
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.NoAccountExist };
                }
                //var PendingAction = (await con.QueryAsync<PendingStaffActionOnProfile>("select id,action from staffaction where approvalstatus=false and id=@id", new { id = actionid })).FirstOrDefault();
                var PendingAction = (await con.QueryAsync<PendingStaffActionOnProfile>("select st.id,(select c.actioname from checkeraction c where c.id=st.action) as action from staffaction st where st.approvalstatus=false and st.id=@id", new { id = actionid })).FirstOrDefault();
                string name = staffNameAndRole.Split('_')[0];
                /*
                string[] choosenstaff = PendingAction.choosenstaff.Split(",");
                if (choosenstaff.Length == 2)
                {
                    if (!name.Equals(choosenstaff[0], StringComparison.CurrentCultureIgnoreCase) && !name.Equals(choosenstaff[1], StringComparison.CurrentCultureIgnoreCase))
                    {
                        return new GenericResponse() { Success = false, Response = EnumResponse.YouareNottheOne };
                    }
                }
                else if (!name.Equals(choosenstaff[0], StringComparison.CurrentCultureIgnoreCase))
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.YouareNottheOne };
                }
                */
                //go ahead and update and delete 
                if (PendingAction == null)
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.ActionLikelyApprove };
                }
                if (approveordeny.Equals("approve", StringComparison.CurrentCultureIgnoreCase))
                {
                    if (PendingAction.action.ToLower() == "delete")
                    {
                   //     await con.ExecuteAsync("update staffaction set approvalstatus=true,approveordeny=@approve where id=@id", new { id = actionid, approve = approveordeny });
                        //then get staff id and do delete
                        var todeletestaffid = (await con.QueryAsync<int>("select staffidtoaction from staffaction where id=@id", new { id = actionid })).FirstOrDefault();
                        await con.ExecuteAsync("delete from staffpermission where staffid=@staffid", new { staffid = todeletestaffid });
                        await con.ExecuteAsync("delete from staffrole where staffid=@staffid", new { staffid = todeletestaffid });
                        await con.ExecuteAsync("delete from staff where id=@staffid", new { staffid = todeletestaffid });
                       // return new GenericResponse() { Success = true, Response = EnumResponse.Successful, Message = "Delete successful" };
                    }
                    else
                    {
                        return new GenericResponse() { Success = false, Response = EnumResponse.WrongAction };
                    }
                }
                else if (approveordeny.Equals("deny", StringComparison.CurrentCultureIgnoreCase))
                {
                    await con.ExecuteAsync("update staffaction set approvalstatus=true,approveordeny=@approve where id=@id", new { id = actionid, approve = approveordeny });
                    return new GenericResponse() { Success = true, Response = EnumResponse.Successful,Message="Denied" };
                }
                else
                {
                    return new GenericResponse() { Success = true, Response = EnumResponse.WrongInput };
                }
                return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> GetPendingProfiledStaff()
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                //var PendingStaffApproval = (await con.QueryAsync<FirstNameAndLastname>("select id,firstname,lastname from staff where approvalstatus=false")).ToList();
                /*
                string query =   $@"select sr.staffid,(select firstname from staff s where s.id=sr.staffid) as firstname,(select lastname from staff s where s.id=sr.staffid) as lastname,(select r.rolename from role r where r.id=sr.staffrole) as staffrole,sp.roleid,group_concat((select permssion from permssions p where p.id=sp.permission) order by sp.permission separator ',') as permissions from staffrole sr 
                                    join staffpermission sp on sr.staffid=sp.staffid 
                                    join staff s on s.id=sr.staffid where s.approvalstatus=false 
                                    join 
                                    group by sr.staffid";
                */
                /*
                string query = $@"select (select email from staff sa where sa.id=sta.initiationstaff) as initiationstaff ,sr.staffid,(select firstname from staff s where s.id=sr.staffid) as firstname,(select lastname from staff s where s.id=sr.staffid) as lastname,(select r.rolename from role r where r.id=sr.staffrole) as staffrole,sp.roleid,group_concat((select permssion from permssions p where p.id=sp.permission) order by sp.permission separator ',') as permissions from staffrole sr 
                                join staffpermission sp on sr.staffid=sp.staffid 
                                join staff s on s.id=sr.staffid 
                                join staffaction sta on sta.staffidtoaction= s.id where s.approvalstatus=true
                                group by sr.staffid";
                */
                /*
                string query = $@"select (select email from staff stf where stf.id=sta.initiationstaff) as initiationstaff,sr.staffid,(select firstname from staff s where s.id=sr.staffid) as firstname,(select lastname from staff s where s.id=sr.staffid) as lastname,(select r.rolename from role r where r.id=sr.staffrole) as staffrole,sp.roleid,group_concat((select permssion from permssions p where p.id=sp.permission) order by sp.permission separator ',') as permissions from staffrole sr 
                                join staffpermission sp on sr.staffid=sp.staffid 
                                join staff s on s.id=sr.staffid
                                join staffaction sta on sta.staffidtoaction=s.id where s.approvalstatus=false
								group by sr.staffid;";
                */
                /*
                string query = $@"select concat((select firstname from staff stf where stf.id=sta.initiationstaff),' ',(select firstname from staff stf where stf.id=sta.initiationstaff)) as initiationstaff,sr.staffid,(select firstname from staff s where s.id=sr.staffid) as firstname,(select lastname from staff s where s.id=sr.staffid) as lastname,(select r.rolename from role r where r.id=sr.staffrole) as staffrole,sp.roleid,group_concat((select permssion from permssions p where p.id=sp.permission) order by sp.permission separator ',') as permissions from staffrole sr 
                                join staffpermission sp on sr.staffid=sp.staffid 
                                join staff s on s.id=sr.staffid
                                join staffaction sta on sta.staffidtoaction=s.id where s.approvalstatus=false
								group by sr.staffid;";
                */
                /*
                string query = $@"SELECT 
                                    CONCAT(staff1.firstname, ' ', staff1.lastname) AS initiationstaff,
                                    sr.staffid,
                                    s.firstname,
                                    s.lastname,
                                    r.rolename AS staffrole,
                                    sp.roleid,
                                    GROUP_CONCAT(p.permission ORDER BY sp.permission SEPARATOR ',') AS permissions
                                FROM 
                                    staffrole sr
                                    JOIN staffpermission sp ON sr.staffid = sp.staffid
                                    JOIN staff s ON s.id = sr.staffid
                                    JOIN role r ON r.id = sr.staffrole
                                    JOIN permissions p ON p.id = sp.permission
                                    JOIN staffaction sta ON sta.staffidtoaction = s.id
                                    JOIN staff staff1 ON staff1.id = sta.initiationstaff
                                WHERE 
                                    s.approvalstatus = false
                                GROUP BY 
                                    sr.staffid;
                                ";
                     */
                string query = $@"
                                    SELECT sta.id as actionid,sta.approveordeny as approveordeny,
                                    CONCAT(staff1.firstname, ' ', staff1.lastname) AS initiationstaff,
                                    sr.staffid,
                                    s.firstname,
                                    s.lastname,
                                    r.rolename AS staffrole,
                                    sp.roleid,
                                    GROUP_CONCAT(p.permssion ORDER BY sp.permission SEPARATOR ',') AS permissions
                                FROM 
                                    staffrole sr
                                    JOIN staffpermission sp ON sr.staffid = sp.staffid
                                    JOIN staff s ON s.id = sr.staffid
                                    JOIN role r ON r.id = sr.staffrole
                                    JOIN permssions p ON p.id = sp.permission
                                    JOIN staffaction sta ON sta.staffidtoaction = s.id and sta.action=1
                                    JOIN staff staff1 ON staff1.id = sta.initiationstaff
                                WHERE 
                                    s.approvalstatus = false and sta.approveordeny='Awaiting approval or deny'
                                GROUP BY 
                                    sr.staffid;";
                var PendingStaff = (await con.QueryAsync<Staff>(query)).ToList();
                return new PrimeAdminResponse() { Success = true, Response = EnumResponse.Successful, Data = PendingStaff };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> ApproveStaffProfile(int actionid, string staffNameAndRole, string approveordeny)
        {
            try
            {
                //approveordeny shd  be either approve or deny.
                using IDbConnection con = _context.CreateConnection();
                _logger.LogInformation("staffNameAndRole " + staffNameAndRole);
                string name = staffNameAndRole.Contains("_") ? staffNameAndRole.Split('_')[0] : staffNameAndRole; // this will be staff that will approves
                var initiationStaff = (await con.QueryAsync<string>("select (select email from staff s where s.id=sf.initiationstaff) as email from staffaction sf where sf.id=@id", new { id = actionid })).FirstOrDefault();
                _logger.LogInformation("initiationStaff " + initiationStaff);
                if (initiationStaff.Replace("@trustbancgroup.com", "").Trim().Equals(name.Trim(), StringComparison.CurrentCultureIgnoreCase))
                {
                    return new GenericResponse() { Success = true, Response = EnumResponse.YouareNottheOne };
                }

                var validateActionId = (await con.QueryAsync<int>("select id from staffaction where id=@id", new { id = actionid })).FirstOrDefault();
                if (validateActionId == 0)
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.NoAccountExist };
                }
                var PendingAction = (await con.QueryAsync<PendingStaffActionOnProfile>("select st.id,(select c.actioname from checkeraction c where c.id=st.action) as action from staffaction st where st.approvalstatus=false and st.id=@id", new { id = actionid })).FirstOrDefault();
                /*
                string[] choosenstaff = PendingAction.choosenstaff.Split(",");
                if (choosenstaff.Length == 2)
                {
                    if (!name.Equals(choosenstaff[0], StringComparison.CurrentCultureIgnoreCase) && !name.Equals(choosenstaff[1], StringComparison.CurrentCultureIgnoreCase))
                    {
                        return new GenericResponse() { Success = false, Response = EnumResponse.YouareNottheOne };
                    }
                }
                else if (!name.Equals(choosenstaff[0], StringComparison.CurrentCultureIgnoreCase))
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.YouareNottheOne };
                }
                */
                //go ahead and update
                if (PendingAction == null)
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.ActionLikelyApprove };
                }
                if (approveordeny.Equals("approve", StringComparison.CurrentCultureIgnoreCase))
                {
                    if (PendingAction.action.ToLower() == "create")
                    {
                        await con.ExecuteAsync("update staffaction set approvalstatus=true,approveordeny=@approve where id=@id", new { approve = approveordeny, id = actionid });
                        var staffidtoaction = (await con.QueryAsync<int>("select staffidtoaction from staffaction where id=@id", new { id = actionid })).FirstOrDefault();
                        //  await con.ExecuteAsync("update staffaction set approvalstatus=true where id=@id", new { id = actionid });
                        await con.ExecuteAsync("update staff set approvalstatus=true where id=@id", new { id = staffidtoaction });
                    }
                    else
                    {
                        // if (PendingAction.action.ToLower() != "delete") { 
                        await con.ExecuteAsync("update staffaction set approvalstatus=true,approveordeny=@approve where id=@id", new { approve = approveordeny, id = actionid });
                        var staffidtoaction = (await con.QueryAsync<int>("select staffidtoaction from staffaction where id=@id", new { id = actionid })).FirstOrDefault();
                    }
                }
                else if (approveordeny.Equals("deny", StringComparison.CurrentCultureIgnoreCase))
                {
                    await con.ExecuteAsync("update staffaction set approveordeny='deny' where id=@id", new { id = actionid });
                    return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
                }
                else
                {
                    return new GenericResponse() { Success = true, Response = EnumResponse.WrongInput };
                }
                return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> GetPendingProfiledStaffCount()
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var counter = (await con.QueryAsync<int>("select count(*) from where approvalstatus=false")).ToList();
                return new PrimeAdminResponse() { Response = EnumResponse.Successful, Data = counter.Count, Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> GetPendingActionOnStaffProfileCount()
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                var PendingAction = (await con.QueryAsync<PendingStaffActionOnProfile>("select count(*) from staffaction where approvalstatus=false")).ToList();
                return new PrimeAdminResponse() { Response = EnumResponse.Successful, Data = PendingAction.Count, Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> GetOtherPendingTaskKyc(string path)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                /*
                string query = $@"select (select firstname from users s where s.id=sta.staffidtoaction) as firstname,
                                concat((select firstname from users s where s.id=sta.staffidtoaction),' ',
                                (select lastname from users s where s.id=sta.staffidtoaction)) as name,
                                (select email from staff s where s.id=sta.initiationstaff) as email
                                ,(select Username from users s where s.id=sta.staffidtoaction) as Username,(select actioname from checkeraction where id=sta.action) as actioname
                               ,sta.approveordeny,sta.accountnumber,sta.typeofdocument,sta.approvalstatus,sta.id as actionid from kycstaffaction sta  where approveordeny!='approve' and approveordeny!='deny';";
                */
                             /*
                                string query = $@"SELECT 
                                            CASE 
                                                WHEN sta.typeofdocument = 'idcard' THEN idu.Filelocation
                                                WHEN sta.typeofdocument = 'passport' THEN dtp.filelocation
                                                WHEN sta.typeofdocument = 'signature' THEN dts.filelocation
                                                WHEN sta.typeofdocument = 'utilitybill' THEN dtu.filelocation
                                            END AS imageurl,
                                              CASE 
	                                            WHEN ca.actioname='rejectkyc' and sta.typeofdocument = 'idcard' THEN (select k.purpose from kycdocumentstatus k where k.typeofdocument='idcard' and k.idcardstatus='reject' and k.userid=sta.staffidtoaction)
	                                            WHEN ca.actioname='rejectkyc' and sta.typeofdocument = 'passport' THEN (select k.purpose from kycdocumentstatus k where k.typeofdocument='passport' and k.passportstatus='reject' and k.userid=sta.staffidtoaction)
	                                            WHEN ca.actioname='rejectkyc' and sta.typeofdocument = 'signature' THEN (select k.purpose from kycdocumentstatus k where k.typeofdocument='singnature' and k.signaturestatus='reject' and k.userid=sta.staffidtoaction)
	                                            WHEN ca.actioname='rejectkyc' and sta.typeofdocument = 'utilitybill' THEN (select k.purpose from kycdocumentstatus k where k.typeofdocument='utilitybill' and k.utlitybillstatus='reject' and k.userid=sta.staffidtoaction)
                                            END AS rejectionreason,
                                            u.firstname AS firstname,
                                            CONCAT(u.firstname, ' ', u.lastname) AS name,
                                            st.email AS email,
                                            u.username AS Username,
                                            ca.actioname AS actioname,
                                            sta.approveordeny,
                                            sta.accountnumber,
                                            sta.typeofdocument,
                                            sta.approvalstatus,
                                            sta.id AS actionid
                                        FROM 
                                            kycstaffaction sta
                                        LEFT JOIN 
                                            idcard_upload idu 
                                            ON idu.userId = sta.staffidtoaction AND sta.typeofdocument = 'idcard'
                                        LEFT JOIN 
                                            document_type dtp 
                                            ON dtp.userid = sta.staffidtoaction AND dtp.Document = 'passport'
                                        LEFT JOIN 
                                            document_type dts 
                                            ON dts.userid = sta.staffidtoaction AND dts.Document = 'signature'
                                        LEFT JOIN 
                                            document_type dtu 
                                            ON dtu.userid = sta.staffidtoaction AND dtu.Document = 'utilitybill'
                                        LEFT JOIN 
                                            users u 
                                            ON u.id = sta.staffidtoaction
                                        LEFT JOIN 
                                            staff st 
                                            ON st.id = sta.initiationstaff
                                        LEFT JOIN 
                                            checkeraction ca 
                                            ON ca.id = sta.action
                                        WHERE 
                                            sta.approveordeny NOT IN ('approve', 'deny');
                                        ";
                                     */
                // the above query is correct but does not include rejectreason incase of reject
                                     /*
                                 string query = $@"
                                               SELECT 
                                            CASE 
	                                            WHEN sta.typeofdocument = 'idcard' THEN idu.Filelocation
	                                            WHEN sta.typeofdocument = 'passport' THEN dtu.filelocation
	                                            WHEN sta.typeofdocument = 'signature' THEN dtu.filelocation
	                                            WHEN sta.typeofdocument = 'utilitybill' THEN dtu.filelocation
                                            END AS imageurl,
                                             CASE 
	                                            WHEN ca.actioname='rejectkyc' and k.typeofdocument = 'idcard' and k.idcardstatus='reject' THEN k.purpose
	                                            WHEN ca.actioname='rejectkyc' and k.typeofdocument = 'passport' and k.passportstatus='reject' THEN k.purpose
	                                            WHEN ca.actioname='rejectkyc' and k.typeofdocument = 'signature' and k.signaturestatus='reject' THEN k.purpose
	                                            WHEN ca.actioname='rejectkyc' and k.typeofdocument = 'utilitybill' and k.utlitybillstatus='reject' THEN k.purpose
                                            END AS rejectionreason,
                                            u.firstname AS firstname,
                                            (select email from staff s where s.id=sta.initiationstaff ) as email,
                                            CONCAT(u.firstname, ' ', u.lastname) AS name,
                                            u.username AS Username,
                                            ca.actioname AS actioname,
                                            sta.approveordeny,
                                            sta.accountnumber,
                                            sta.typeofdocument,
                                            sta.approvalstatus,
                                            sta.id AS actionid
                                            FROM 
                                            kycstaffaction sta
                                            LEFT JOIN 
                                            idcard_upload idu 
                                            ON idu.userId = sta.staffidtoaction
                                            LEFT JOIN 
                                            document_type dtu 
                                            ON dtu.userid = sta.staffidtoaction
                                            LEFT JOIN 
                                            kycdocumentstatus k
                                            ON idu.userId = k.userid
                                            LEFT JOIN 
                                            users u 
                                            ON u.id = sta.staffidtoaction
                                            LEFT JOIN 
                                            checkeraction ca 
                                            ON ca.id = sta.action
                                            WHERE 
                                            sta.approveordeny NOT IN ('approve', 'deny');
                                                    ";
                                   */ 
                                     string query=$@"SELECT 
                                                            CASE 
                                                                WHEN sta.typeofdocument = 'idcard' THEN idu.Filelocation
                                                                WHEN sta.typeofdocument = 'passport' THEN dtp.filelocation
                                                                WHEN sta.typeofdocument = 'signature' THEN dts.filelocation
                                                                WHEN sta.typeofdocument = 'utilitybill' THEN dtu.filelocation
                                                            END AS imageurl,
                                                            CASE 
                                                                WHEN ca.id = 4 AND sta.typeofdocument = 'idcard' THEN kyc_idcard.purpose
                                                                WHEN ca.id = 4 AND sta.typeofdocument = 'passport' THEN kyc_passport.purpose
                                                                WHEN ca.id=4 AND sta.typeofdocument = 'signature' THEN kyc_signature.purpose
                                                                WHEN ca.id=4 AND sta.typeofdocument = 'utilitybill' THEN kyc_utilitybill.purpose
                                                            END AS rejectionreason,
                                                            u.firstname AS firstname,
                                                            CONCAT(u.firstname, ' ', u.lastname) AS name,
                                                            st.email AS email,
                                                            u.username AS Username,
                                                            ca.actioname AS actioname,
                                                            sta.approveordeny,
                                                            sta.accountnumber,
                                                            sta.typeofdocument,
                                                            sta.approvalstatus,
                                                            sta.id AS actionid
                                                        FROM 
                                                            kycstaffaction sta
                                                        LEFT JOIN 
                                                            idcard_upload idu 
                                                            ON idu.userId = sta.staffidtoaction AND sta.typeofdocument = 'idcard'
                                                        LEFT JOIN 
                                                            document_type dtp 
                                                            ON dtp.userid = sta.staffidtoaction AND dtp.Document = 'passport'
                                                        LEFT JOIN 
                                                            document_type dts 
                                                            ON dts.userid = sta.staffidtoaction AND dts.Document = 'signature'
                                                        LEFT JOIN 
                                                            document_type dtu 
                                                            ON dtu.userid = sta.staffidtoaction AND dtu.Document = 'utilitybill'
                                                        LEFT JOIN 
                                                            users u 
                                                            ON u.id = sta.staffidtoaction
                                                        LEFT JOIN 
                                                            staff st 
                                                            ON st.id = sta.initiationstaff
                                                        LEFT JOIN 
                                                            checkeraction ca 
                                                            ON ca.id = sta.action
                                                        LEFT JOIN 
                                                            kycdocumentstatus kyc_idcard 
                                                            ON kyc_idcard.userid = sta.staffidtoaction 
                                                            AND kyc_idcard.typeofdocument = 'idcard' 
                                                            AND kyc_idcard.idcardstatus = 'reject'
                                                        LEFT JOIN 
                                                            kycdocumentstatus kyc_passport 
                                                            ON kyc_passport.userid = sta.staffidtoaction 
                                                            AND kyc_passport.typeofdocument = 'passport' 
                                                            AND kyc_passport.passportstatus = 'reject'
                                                        LEFT JOIN 
                                                            kycdocumentstatus kyc_signature 
                                                            ON kyc_signature.userid = sta.staffidtoaction 
                                                            AND kyc_signature.typeofdocument = 'signature' 
                                                            AND kyc_signature.signaturestatus = 'reject'
                                                        LEFT JOIN 
                                                            kycdocumentstatus kyc_utilitybill 
                                                            ON kyc_utilitybill.userid = sta.staffidtoaction 
                                                            AND kyc_utilitybill.typeofdocument = 'utilitybill' 
                                                            AND kyc_utilitybill.utlitybillstatus = 'reject'
                                                        WHERE 
                                                            sta.approveordeny NOT IN ('approve', 'deny');
                                                        ";
                var PendingStaff = (await con.QueryAsync<Staff2>(query)).ToList();
                if (PendingStaff.Any()) { 
                PendingStaff.ForEach(pendingstaff =>
                {
                    pendingstaff.imageurl = path + _settings.Urlfilepath + "/KycView/" + Path.GetFileName(pendingstaff.imageurl);
                });
                }
              return new PrimeAdminResponse() { Success = true, Response = EnumResponse.Successful, Data =PendingStaff };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> ApproveTask(int actionid, string staffNameAndRole, string approveordeny, string shortdescription, string AccountNumber = null)
        {
            //acceptkyc,rejectkyc,customeractivation,customerdeactivation,acceptlimit,rejectlimit
            try
            {
                using IDbConnection con = _context.CreateConnection();
                _logger.LogInformation("staffNameAndRole " + staffNameAndRole);
                //  Users usr = null;
                string name = staffNameAndRole.Contains("_") ? staffNameAndRole.Split('_')[0] : staffNameAndRole; // this will be staff that will approves
                _logger.LogInformation("name " + name);
                var initiationStaff = (await con.QueryAsync<string>("select (select email from staff s where s.id=sf.initiationstaff) as email from staffaction sf where sf.id=@id", new { id = actionid })).FirstOrDefault();
                      
                if (initiationStaff.Replace("@trustbancgroup.com", "").Trim().Equals(name.Trim(), StringComparison.CurrentCultureIgnoreCase))
                {
                    return new GenericResponse() { Success = true, Response = EnumResponse.YouareNottheOne };
                }
                
                var validateActionId = (await con.QueryAsync<int>("select id from staffaction where id=@id", new { id = actionid })).FirstOrDefault();
                if (validateActionId == 0)
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.NoAccountExist };
                }
                //get approvalstaff
                var stafftoapprove = (await con.QueryAsync<int>("select id from staff where email=@email", new { email = name + "@trustbancgroup.com" })).FirstOrDefault();
                var PendingAction = (await con.QueryAsync<PendingStaffActionOnProfile>("select st.id,(select c.actioname from checkeraction c where c.id=st.action) as action ,st.staffidtoaction as userid from staffaction st where st.approvalstatus=false and st.id=@id", new { id = actionid })).FirstOrDefault();
                _logger.LogInformation("PendingAction " + JsonConvert.SerializeObject(PendingAction));
                if (PendingAction == null)
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.ActionLikelyApprove };
                }
                if (approveordeny.Equals("approve", StringComparison.CurrentCultureIgnoreCase))
                {
                    if (PendingAction.action.ToLower() == "initiatecustomeractivation")
                    {
                        await con.ExecuteAsync("update staffaction set approvalstatus=true,approveordeny=@approve,approvalstaff=@approvalstaff where id=@id", new { approve = approveordeny, id = actionid, approvalstaff = stafftoapprove });
                        var customeridtoaction = (await con.QueryAsync<int>("select staffidtoaction from staffaction where id=@id", new { id = actionid })).FirstOrDefault();
                        //update the customer status here . set it to 1 since activation is 1.
                        _logger.LogInformation("customeridtoaction " + customeridtoaction);
                        await con.ExecuteAsync("update users set Status=@status,actionid=@actionid where id=@userid", new { status = 1, userid = customeridtoaction, actionid = actionid });
                        Users usr = (await con.QueryAsync<Users>("select * from users where id=@id", new { id = customeridtoaction })).FirstOrDefault();
                        CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)PendingAction.userid);
                        SendMailObject sendMailObject = new SendMailObject();
                        sendMailObject.Subject = "TrustBanc Mobile Banking Account Recovery";
                        string email = $@"
                                <p>Dear {usr.Username} {usr.LastName}</p>,
                                <p>We have approved your request to recover your account for TrustBanc Mobile Banking.</p> 
                                <p>You can now log in to your mobile banking app using this username. If you encounter any issues or need further assistance, please do not hesitate to contact us.</p>
                                <p>
                                For additional security:
                                •	If you did not request this recovery, please contact TrustBanc Support immediately at {_settings.SupportPhoneNumber}.
                                •	If you need to reset your password or make any changes to your account, visit the TrustBanc website or mobile app for further instructions.
                                </p>
                                <p>Thank you for banking with TrustBanc!</p>
                                <p>
                                Best regards,
                                TrustBanc Mobile Banking Support Team
                                {_settings.SupportPhoneNumber}
                                {_settings.SupportEmail}
                               </p>
                                ";
                        sendMailObject.Email = customerDataNotFromBvn.Email;
                        sendMailObject.Html = email;
                        Task.Run(() =>
                        {
                            _genServ.SendMail(sendMailObject);
                        });
                        return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
                    }else if (PendingAction.action.ToLower() == "initiatecustomerdeactivation")
                    {
                        await con.ExecuteAsync("update staffaction set approvalstatus=true,approveordeny=@approve,approvalstaff=@approvalstaff where id=@id", new { approve = approveordeny, id = actionid, approvalstaff = stafftoapprove });
                        var customeridtoaction = (await con.QueryAsync<int>("select staffidtoaction from staffaction where id=@id", new { id = actionid })).FirstOrDefault();
                        //update the customer status here . set it to 1 since activation is 1.
                        _logger.LogInformation("customeridtoaction " + customeridtoaction);
                        await con.ExecuteAsync("update users set Status=@status,actionid=@actionid where id=@userid", new { status = 2, userid = customeridtoaction, actionid = actionid });
                        Users usr = (await con.QueryAsync<Users>("select * from users where id=@id", new { id = customeridtoaction })).FirstOrDefault();
                        CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)PendingAction.userid);
                        SendMailObject sendMailObject = new SendMailObject();
                        sendMailObject.Subject = "TrustBanc Mobile Banking Account Deactivation";
                        string email = $@"
                                <p>Dear {usr.Username} {usr.LastName}</p>,
                                <p>We have approved your request to deactivate your account for TrustBanc Mobile Banking.</p> 
                                <p>
                                For additional security:
                                •	If you did not request this recovery, please contact TrustBanc Support immediately at {_settings.SupportPhoneNumber}.
                                •	If you need to reset your password or make any changes to your account, visit the TrustBanc website or mobile app for further instructions.
                                </p>
                                <p>Thank you for banking with TrustBanc!</p>
                                <p>
                                Best regards,</p>
                                <p>
                                 TrustBanc Mobile Banking Support Team
                                </p><p>
                                {_settings.SupportPhoneNumber}
                                {_settings.SupportEmail}
                               </p>
                                ";
                        sendMailObject.Email = customerDataNotFromBvn.Email;
                        sendMailObject.Html = email;
                        Task.Run(() =>
                        {
                            _genServ.SendMail(sendMailObject);
                        });
                        return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
                    }
                    else if (PendingAction.action.ToLower() == "initiatecustomerindemnity")
                    {
                        // if (PendingAction.action.ToLower() != "delete") { 
                        await con.ExecuteAsync("update staffaction set approvalstatus=true,approveordeny=@approve,approvalstaff=@approvalstaff where id=@id", new { approve = approveordeny, id = actionid, approvalstaff = stafftoapprove });
                        var customeridtoaction = (await con.QueryAsync<int>("select staffidtoaction from staffaction where id=@id", new { id = actionid })).FirstOrDefault();
                        await con.ExecuteAsync("update customerindemnity set indemnitystatus=@status,indemnityapproval=true,actionid=@actionid,IndemnityType='customerindemnity' where userid=@userid and (AccountNumber='' or AccountNumber is null)", new { actionid = actionid, status = "accept", userid = customeridtoaction });
                        // var staffid = (await con.QueryAsync<int>("select staffidtoaction from staffaction where approvalstatus=true and id=@id", new { id = actionid })).FirstOrDefault();
                        CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, customeridtoaction);
                        // var username = (await con.QueryAsync<string>("select username from users where id=@id",new {id=customeridtoaction})).FirstOrDefault();
                        var usr = await _genServ.GetUserbyUsername(customerDataNotFromBvn.username, con);
                        if (customeridtoaction != 0) // send to the customer that it has been approved.
                        {
                            new Thread(() =>
                            {
                                SendMailObject sendMailObject = new SendMailObject();
                                sendMailObject.Subject = "Trustbanc Indemnity Request Approval";
                                sendMailObject.Email = customerDataNotFromBvn.Email;
                                // sendMailObject.Html = $"Dear ${usr.Firstname.ToUpper()},the requested customer indemnity you required has been approved .<br/>Thank you for Banking with us.";
                                sendMailObject.Html = $@"Indemnity Request Approval for {usr.Firstname} {usr.LastName}
                                        <p>Dear {usr.Firstname} {usr.LastName}</p>,

                                        <p>We are pleased to inform you that your indemnity request for the following matter has been approved by TrustBanc.</p> 
                                       <p>
                                        Indemnity Request Details:
                                        •	Customer Name: {usr.Firstname} {usr.LastName}
                                        •	Account Number/ID: Indemnity across Account
                                        •	Incident/Transaction Date: {DateTime.Now}
                                        </p>
                                        <p>
                                        Next Steps:
                                        1.	Indemnity Granted: TrustBanc has officially granted indemnity for the situation described above. The affected customer(s) will not bear responsibility for any loss or liability incurred.
                                        2.	Resolution Process: Please proceed with any necessary actions to rectify the issue, including reversing transactions, updating records, or communicating with the affected customer.
                                        3.	Documentation: Kindly ensure that all required documentation is completed and filed as per TrustBanc’s internal policies.
                                        </p>
                                        <p>
                                        Additional Notes:
                                        •	If any further issues arise or additional information is required, please contact us immediately at [Support Phone Number] or [Support Email Address].
                                        •	We recommend reviewing the process that led to this indemnity request to avoid future occurrences.
                                        Thank you for your cooperation in resolving this matter. If you have any further questions or need assistance, please don’t hesitate to reach out.
                                       </p>
                                        <p>
                                        Best regards,</p>
                                        <p>
                                         TrustBanc Customer Support/Legal/Compliance Team
                                        </p>
                                        <p>
                                        {_settings.SupportPhoneNumber}
                                        {_settings.SupportEmail}
                                        {_settings.SupportWebsite}
                                        </p>
                                        ";
                                _genServ.SendMail(sendMailObject); // notify the user  
                            }).Start();
                        }
                        return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
                    }
                    else if (PendingAction.action.ToLower() == "acceptkyc")
                    {
                        await con.ExecuteAsync("update staffaction set approveordeny=@approve,approvalstaff=@approvalstaff where id=@id", new { approve = approveordeny, id = actionid, approvalstaff = stafftoapprove });
                        var customeridtoaction = (await con.QueryAsync<int>("select staffidtoaction from staffaction where id=@id", new { id = actionid })).FirstOrDefault();
                        CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, customeridtoaction);
                        // var username = (await con.QueryAsync<string>("select username from users where id=@id", new { id = customeridtoaction })).FirstOrDefault();    
                        var usr = await _genServ.GetUserbyUsername(customerDataNotFromBvn.username, con);
                        await con.ExecuteAsync("update customerkycstatus set kycstatus=@status,actionid=@actionid where username=@username", new { status = true, username = customerDataNotFromBvn.username, actionid = actionid });
                        // await con.ExecuteAsync("update customerkycstatus set kycstatus=@status,actionid=@actionid where username=@username", new { status = true, username = customerDataNotFromBvn.username, actionid = actionid });
                        // send mail to customer
                        new Thread(async () =>
                        {
                            SendMailObject sendMailObject = new SendMailObject();
                            sendMailObject.Subject = "Notification on Kyc Documentation";
                            sendMailObject.Email = customerDataNotFromBvn.Email;
                            sendMailObject.Html = $@"Dear {usr.Firstname.ToUpper()},Please find below the status of your Kyc:
                                              <p>${shortdescription}</p>
                                              For any feedback or enquiries, please call our Contact Center on 07004446147 or send an email to support@trustbancgroup.com.</p> 
                                              <p>Thank you for choosing TrustBanc J6 MfB</p>
                                              ";
                            _genServ.SendMail(sendMailObject);
                            await _smsBLService.SendSmsNotificationToCustomer("Trustbanc Mobile", customerDataNotFromBvn.PhoneNumber, "KYC approved", "KYC");
                        }).Start();
                        return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
                    }
                    else if (PendingAction.action.ToLower() == "initiateaccountindemnitylimit" && AccountNumber != null)
                    {
                        await con.ExecuteAsync("update staffaction set approvalstatus=true,approveordeny=@approve,approvalstaff=@approvalstaff where id=@id", new { approve = approveordeny, id = actionid, approvalstaff = stafftoapprove });
                        var customeridtoaction = (await con.QueryAsync<int>("select staffidtoaction from staffaction where id=@id", new { id = actionid })).FirstOrDefault();
                        await con.ExecuteAsync("update customerindemnity set AccountNumber=@AccountNumber,indemnitystatus=@status,indemnityapproval=true,actionid=@actionid where userid=@userid", new { AccountNumber = AccountNumber, actionid = actionid, status = "accept", userid = customeridtoaction });
                        CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, customeridtoaction);
                        var usr = await _genServ.GetUserbyUsername(customerDataNotFromBvn.username, con);
                        if (customeridtoaction != 0) // send to the customer that it has been approved.
                        {
                            new Thread(() =>
                            {
                                SendMailObject sendMailObject = new SendMailObject();
                                sendMailObject.Subject = "TrustBanc Indemnity Request Approval";
                                sendMailObject.Email = customerDataNotFromBvn.Email;
                                sendMailObject.Html = $"Dear ${usr.Firstname.ToUpper()} {usr.LastName},the requested account indemnity you required has been approved .<br/>Thank you for Banking with us.";
                                _genServ.SendMail(sendMailObject); // notify the user  
                            }).Start();
                        }
                        return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
                    }
                    return new GenericResponse() { Success = true, Response = EnumResponse.WrongAction };
                }
                else if (approveordeny.Equals("deny", StringComparison.CurrentCultureIgnoreCase))
                {
                    if (PendingAction.action.ToLower() == "initiatecustomerindemnity")
                    {
                        await con.ExecuteAsync("update staffaction set approvalstatus=true, approveordeny=@approve,approvalstaff=@approvalstaff where id=@id", new { approve = approveordeny, id = actionid, approvalstaff = stafftoapprove });
                        var customeridtoaction = (await con.QueryAsync<int>("select staffidtoaction from staffaction where id=@id", new { id = actionid })).FirstOrDefault();
                        await con.ExecuteAsync("update customerindemnity set indemnitystatus=@status,actionid=@actionid where userid=@userid", new { actionid = actionid, status = "reject", userid = customeridtoaction });
                        // var staffid = (await con.QueryAsync<int>("select staffidtoaction from staffaction where approvalstatus=true and id=@id", new { id = actionid })).FirstOrDefault();
                        // var username = (await con.QueryAsync<string>("select username from users where id=@id", new { id = customeridtoaction })).FirstOrDefault();
                        CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, customeridtoaction);
                        var usr = await _genServ.GetUserbyUsername(customerDataNotFromBvn.username, con);
                        if (customeridtoaction != 0) // send to the customer that it has been approved.
                        {
                            new Thread(() =>
                            {
                                SendMailObject sendMailObject = new SendMailObject();
                                sendMailObject.Subject = "Trustbanc Account Indemnity";
                                sendMailObject.Email = customerDataNotFromBvn.Email;
                                sendMailObject.Html = $"<p>Dear ${usr.Firstname.ToUpper()}</p>,<p>the requested account indemnity limit you required has been rejected</p> .<br/><p>Thank you for Banking with us.</p>";
                                _genServ.SendMail(sendMailObject); // notify the user  
                            }).Start();
                        }
                        return new GenericResponse() { Success = true, Response = EnumResponse.Successful, Message = "rejected" };
                    }
                    else if (PendingAction.action.ToLower() == "initiatecustomeractivation")
                    {
                        await con.ExecuteAsync("update staffaction set approvalstatus=true,approveordeny=@approve,approvalstaff=@approvalstaff where id=@id", new { approve = approveordeny, id = actionid, approvalstaff = stafftoapprove });
                        var customeridtoaction = (await con.QueryAsync<int>("select staffidtoaction from staffaction where id=@id", new { id = actionid })).FirstOrDefault();
                        return new GenericResponse() { Success = true, Response = EnumResponse.Successful, Message = "rejected" };
                    }
                    else if (PendingAction.action.ToLower() == "initiatecustomerdeactivation")
                    {
                        await con.ExecuteAsync("update staffaction set approvalstatus=true,approveordeny=@approve,approvalstaff=@approvalstaff where id=@id", new { approve = approveordeny, id = actionid, approvalstaff = stafftoapprove });
                        var customeridtoaction = (await con.QueryAsync<int>("select staffidtoaction from staffaction where id=@id", new { id = actionid })).FirstOrDefault();                     
                        return new GenericResponse() { Success = true, Response = EnumResponse.Successful, Message = "rejected" };
                    }
                    else if (PendingAction.action.ToLower() == "rejectkyc")
                    {
                        await con.ExecuteAsync("update staffaction set approveordeny=@approve,approvalstaff=@approvalstaff where id=@id", new { approve = approveordeny, id = actionid, approvalstaff = stafftoapprove });
                        var customeridtoaction = (await con.QueryAsync<int>("select staffidtoaction from staffaction where id=@id", new { id = actionid })).FirstOrDefault();
                        CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, customeridtoaction);
                        // var username = (await con.QueryAsync<string>("select username from users where id=@id", new { id = customeridtoaction })).FirstOrDefault();    
                        var usr = await _genServ.GetUserbyUsername(customerDataNotFromBvn.username, con);
                        await con.ExecuteAsync("update customerkycstatus set kycstatus=@status,actionid=@actionid where username=@username", new { status = false, username = customerDataNotFromBvn.username, actionid = actionid });
                        // send mail to customer
                        new Thread(async () =>
                        {
                            SendMailObject sendMailObject = new SendMailObject();
                            sendMailObject.Subject = "Notification on Kyc Documentation";
                            sendMailObject.Email = customerDataNotFromBvn.Email;
                            sendMailObject.Html = $@"Dear {usr.Firstname.ToUpper()},Please find below the status of your kyc:
                                              <p>{shortdescription}</p>
                                              For any feedback or enquiries, please call our Contact Center on 07004446147 or send an email to support@trustbancgroup.com.</p> 
                                              <p>Thank you for choosing TrustBanc J6 MfB</p>
                                              ";
                            _genServ.SendMail(sendMailObject);
                            await _smsBLService.SendSmsNotificationToCustomer("Trustbanc Mobile", customerDataNotFromBvn.PhoneNumber, "Kyc rejected", "KYC");
                        }).Start();
                        return new GenericResponse() { Success = true, Response = EnumResponse.Successful, Message = "rejected" };
                    }
                    else if (PendingAction.action.ToLower() == "initiateaccountindemnitylimit" && AccountNumber != null)
                    {
                        await con.ExecuteAsync("update staffaction set approvalstatus=true,approveordeny=@approve,approvalstaff=@approvalstaff where id=@id", new { approve = approveordeny, id = actionid, approvalstaff = stafftoapprove });
                        var customeridtoaction = (await con.QueryAsync<int>("select staffidtoaction from staffaction where id=@id", new { id = actionid })).FirstOrDefault();
                        await con.ExecuteAsync("update customerindemnity set AccountNumber=@AccountNumber,indemnitystatus=@status,indemnityapproval=false,actionid=@actionid where userid=@userid", new { AccountNumber = AccountNumber, actionid = actionid, status = "reject", userid = customeridtoaction });
                        CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, customeridtoaction);
                        var usr = await _genServ.GetUserbyUsername(customerDataNotFromBvn.username, con);
                        if (customeridtoaction != 0) // send to the customer that it has been approved.
                        {
                            new Thread(() =>
                            {
                                SendMailObject sendMailObject = new SendMailObject();
                                sendMailObject.Subject = "TrustBanc AccountIndemnity Rejection";
                                sendMailObject.Email = customerDataNotFromBvn.Email;
                                sendMailObject.Html = $"Dear ${usr.Firstname.ToUpper()},the requested account indemnity you required has been rejected .<br/>Thank you for Banking with us.";
                                _genServ.SendMail(sendMailObject); // notify the user  
                            }).Start();
                        }
                        return new GenericResponse() { Success = true, Response = EnumResponse.Successful, Message = "rejected" };
                    }
                    return new GenericResponse() { Success = true, Response = EnumResponse.ApprovalDenied };
                }
                else
                {
                    return new GenericResponse() { Success = true, Response = EnumResponse.WrongInput };
                }
                //  return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> InitiateTask(int customerid, string action, string StaffNameAndRole)
        {
            try
            {

                using IDbConnection con = _context.CreateConnection();
                var actions = (await con.QueryAsync<string>("select actioname from checkeraction")).ToList();
                _logger.LogInformation("action " + action + " actions " + string.Join(",", actions));
                Console.WriteLine("action " + action + " actions " + string.Join(",", actions));
                if (!actions.Any(a => a.Equals(action, StringComparison.OrdinalIgnoreCase)))
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.WrongAction };
                }
                var InitiatedActionId = (await con.QueryAsync<int>("select id from checkeraction where actioname=@action", new { action = action })).FirstOrDefault();
                if (InitiatedActionId == 0)
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.WrongAction };
                }
                _logger.LogInformation("StaffNameAndRole " + StaffNameAndRole);
                var name = StaffNameAndRole.Contains("_") ? StaffNameAndRole.Split('_')[0] : StaffNameAndRole;
                var staffid2 = (await con.QueryAsync<int>("select id from staff where email=@email", new { email = name + "@trustbancgroup.com" })).FirstOrDefault();
                Console.WriteLine("staffid2 " + staffid2);
                //staffidtoaction is the customerid in this case.
                var checkuserorStaff = (await con.QueryAsync<int>("select staffidtoaction from staffaction where action=@actionid and staffidtoaction=@userid", new { actionid = InitiatedActionId, userid = customerid })).FirstOrDefault();
                if (checkuserorStaff != 0)
                {
                    await con.ExecuteAsync("delete from staffaction where action=@actionId and staffidtoaction=@staffidtoaction and approvalstatus=false", new { actionId = InitiatedActionId, staffidtoaction = checkuserorStaff });
                }
                await con.ExecuteAsync($@"insert into staffaction(action,initiationstaff,choosenstaff,staffidtoaction)
                                          values(@action,@initiationstaff,@choosenstaff,@staffidtoaction)", new { action = InitiatedActionId, initiationstaff = staffid2, choosenstaff = "", staffidtoaction = customerid });
                return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> InitiateTask(int customerid, string action, string StaffNameAndRole, string AccountNumber)
        {
            try
            {

                using IDbConnection con = _context.CreateConnection();
                var actions = (await con.QueryAsync<string>("select actioname from checkeraction")).ToList();
                _logger.LogInformation("action " + action + " actions " + string.Join(",", actions));
                Console.WriteLine("action " + action + " actions " + string.Join(",", actions));
                if (!actions.Any(a => a.Equals(action, StringComparison.OrdinalIgnoreCase)))
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.WrongAction };
                }
                var InitiatedActionId = (await con.QueryAsync<int>("select id from checkeraction where actioname=@action", new { action = action })).FirstOrDefault();
                if (InitiatedActionId == 0)
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.WrongAction };
                }
                _logger.LogInformation("StaffNameAndRole " + StaffNameAndRole);
                var name = StaffNameAndRole.Contains("_") ? StaffNameAndRole.Split('_')[0] : StaffNameAndRole;
                var staffid2 = (await con.QueryAsync<int>("select id from staff where email=@email", new { email = name + "@trustbancgroup.com" })).FirstOrDefault();
                Console.WriteLine("staffid2 " + staffid2);
                //staffidtoaction is the customerid in this case.
                var checkInitiationStaff = (await con.QueryAsync<int>("select initiationstaff from staffaction where action=@actionid", new { actionid = InitiatedActionId })).FirstOrDefault();
                if (checkInitiationStaff != 0)
                {
                    await con.ExecuteAsync("delete from staffaction where action=@actionId and initiationstaff=@initiationstaff and accountnumber=@accountNumber and approvalstatus=false", new { actionId = InitiatedActionId, initiationstaff = checkInitiationStaff, accountnumber = AccountNumber });
                }
                if (checkInitiationStaff != 0)
                {
                    await con.ExecuteAsync("delete from staffaction where action=@actionId and staffidtoaction=@customerid and accountnumber=@accountNumber and approvalstatus=false", new { actionId = InitiatedActionId, customerid = customerid, accountnumber = AccountNumber });
                }
                await con.ExecuteAsync("delete from staffaction where action=@actionId and staffidtoaction=@customerid and approvalstatus=false", new { actionId = InitiatedActionId, customerid = customerid });
                await con.ExecuteAsync($@"insert into staffaction(action,initiationstaff,choosenstaff,staffidtoaction,accountnumber)
                                          values(@action,@initiationstaff,@choosenstaff,@staffidtoaction,@accountnumber)", new { action = InitiatedActionId, initiationstaff = staffid2, choosenstaff = "", staffidtoaction = customerid, accountnumber = AccountNumber != null ? AccountNumber : null });
                return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }

        }

        public async Task<GenericResponse> GetcustomerIndemnityPendingTask()
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                // string query = $@"select sta.id as actionid,(select actioname from checkeraction where id=sta.action) as actioname,concat((select firstname from staff s where s.id=sta.initiationstaff),' ',(select email from staff s where s.id=sta.initiationstaff)) as name,(select email from staff s where s.id=sta.initiationstaff) as email from staffaction sta where sta.action=20";
                //  string query = $@"select (select c.Dailywithdrawaltransactionlimit from customerindemnity c where c.userid=sta.staffidtoaction and IndemnityType='customerindemnity') as Dailywithdrawaltransactionlimit,(select c.Singlewithdrawaltransactionlimit from customerindemnity c where c.userid=sta.staffidtoaction and IndemnityType='customerindemnity') as Singlewithdrawaltransactionlimit,sta.id as actionid,(select actioname from checkeraction where id=sta.action) as actioname,(select username from users s where s.id=sta.staffidtoaction) as username,(select firstname from users s where s.id=sta.staffidtoaction) as firstname,concat((select firstname from users s where s.id=sta.staffidtoaction),' ',(select lastname from users s where s.id=sta.initiationstaff)) as name,(select email from staff s where s.id=sta.initiationstaff) as email from staffaction sta where sta.action=20 and sta.approvalstatus=false and approvalstatus='Awaiting review'";
                /*
                     string query = $@"SELECT 
                            ci.Dailywithdrawaltransactionlimit AS DailyWithdrawalTransactionLimit,
                            ci.Singlewithdrawaltransactionlimit AS SingleWithdrawalTransactionLimit,
                            ci.AccountNumber AS AccountNumber,
                            sta.id AS ActionID,
                            u.Username AS UserName,
                            ca.actioname AS ActionName,
                            u.firstname AS FirstName,
                            CONCAT(u.firstname, ' ', u.lastname) AS name,
                            st.email AS Email
                        FROM 
                            staffaction sta
                        JOIN 
                            customerindemnity ci 
                            ON ci.userid = sta.staffidtoaction 
                           AND ci.IndemnityType = 'customerindemnity'
                           AND sta.approvalstatus = 'Awaiting review';
                        JOIN 
                            users u 
                            ON u.id = sta.staffidtoaction
                        JOIN 
                            checkeraction ca 
                            ON ca.id = sta.action
                        JOIN 
                            staff st 
                            ON st.id = sta.initiationstaff
                        WHERE 
                            sta.action = 19 
                            AND sta.approvalstatus = FALSE 
                        ";
                  */
                                                string query = $@"SELECT 
                                                    ci.Dailywithdrawaltransactionlimit AS DailyWithdrawalTransactionLimit,
                                                    ci.Singlewithdrawaltransactionlimit AS SingleWithdrawalTransactionLimit,
                                                    ci.AccountNumber AS AccountNumber,
                                                    sta.id AS ActionID,
                                                    u.username AS UserName,  -- Ensure the correct column name
                                                    ca.actioname AS ActionName,
                                                    u.firstname AS FirstName,
                                                    CONCAT(u.firstname, ' ', u.lastname) AS name,
                                                    st.email AS Email
                                                FROM 
                                                    staffaction sta
                                                JOIN 
                                                    customerindemnity ci 
                                                    ON ci.userid = sta.staffidtoaction 
                                                    AND ci.IndemnityType = 'customerindemnity'
                                                JOIN 
                                                    users u 
                                                    ON u.id = sta.staffidtoaction
                                                JOIN 
                                                    checkeraction ca 
                                                    ON ca.id = sta.action
                                                JOIN 
                                                    staff st 
                                                    ON st.id = sta.initiationstaff
                                                WHERE 
                                                    sta.action = 20 
                                                    AND sta.approvalstatus in ('Awaiting review','Awaiting approval or deny')";
                                      var PendingStaff = (await con.QueryAsync<Staff2>(query)).ToList();
                                     return new PrimeAdminResponse() { Success = true, Response = EnumResponse.Successful, Data = PendingStaff };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> GetAccountIndemnityPendingTask()
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                //string query = $@"select sta.id as actionid,(select actioname from checkeraction where id=sta.action) as actioname,concat((select firstname from staff s where s.id=sta.initiationstaff),' ',(select email from staff s where s.id=sta.initiationstaff)) as name,(select email from staff s where s.id=sta.initiationstaff) as email from staffaction sta where sta.action=20";
                //string query = $@"select (select c.Dailywithdrawaltransactionlimit from customerindemnity c where c.userid=sta.staffidtoaction and IndemnityType='accountindemnity') as Dailywithdrawaltransactionlimit,(select c.Singlewithdrawaltransactionlimit from customerindemnity c where c.userid=sta.staffidtoaction and IndemnityType='accountindemnity') as Singlewithdrawaltransactionlimit,(select AccountNumber from customerindemnity where IndemnityType='accountindemnity' and userid=sta.staffidtoaction) as AccountNumber, sta.id as actionid,(select username from users s where s.id=sta.staffidtoaction) as username,(select actioname from checkeraction where id=sta.action) as actioname,(select firstname from users s where s.id=sta.staffidtoaction) as firstname,concat((select firstname from users s where s.id=sta.staffidtoaction),' ',(select lastname from users s where s.id=sta.staffidtoaction)) as name,(select email from staff s where s.id=sta.initiationstaff) as email from staffaction sta where sta.action=19 and sta.approvalstatus=false and approvalstatus='Awaiting review'";
                /*
                string query = $@"SELECT 
                                                    ci.Dailywithdrawaltransactionlimit AS DailyWithdrawalTransactionLimit,
                                                    ci.Singlewithdrawaltransactionlimit AS SingleWithdrawalTransactionLimit,
                                                    ci.AccountNumber AS AccountNumber,
                                                    sta.id AS ActionID,
                                                    u.username AS UserName,
                                                    ca.actioname AS ActionName,
                                                    u.firstname AS FirstName,
                                                    CONCAT(u.firstname, ' ', u.lastname) AS name,
                                                    st.email AS Email
                                                FROM 
                                                    staffaction sta
                                                JOIN 
                                                    customerindemnity ci 
                                                    ON ci.userid = sta.staffidtoaction 
                                                   AND ci.IndemnityType = 'accountindemnity'
                                                   AND sta.approvalstatus = 'Awaiting review';
                                                JOIN 
                                                    users u 
                                                    ON u.id = sta.staffidtoaction
                                                JOIN 
                                                    checkeraction ca 
                                                    ON ca.id = sta.action
                                                JOIN 
                                                    staff st 
                                                    ON st.id = sta.initiationstaff
                                                WHERE 
                                                    sta.action = 19 
                                                    AND sta.approvalstatus = FALSE
                                                    AND sta.approvalstatus in ('Awaiting review','Awaiting approval or deny')
                                                ";
                                               */
                                                string query = $@"SELECT 
                                                    ci.Dailywithdrawaltransactionlimit AS DailyWithdrawalTransactionLimit,
                                                    ci.Singlewithdrawaltransactionlimit AS SingleWithdrawalTransactionLimit,
                                                    ci.AccountNumber AS AccountNumber,
                                                    sta.id AS ActionID,
                                                    u.username AS UserName,
                                                    ca.actioname AS ActionName,
                                                    u.firstname AS FirstName,
                                                    CONCAT(u.firstname, ' ', u.lastname) AS name,
                                                    (select email from staff s where s.id=sta.initiationstaff) AS Email
                                                FROM 
                                                    staffaction sta
                                                JOIN 
                                                    customerindemnity ci 
                                                    ON ci.userid = sta.staffidtoaction 
                                                    AND ci.IndemnityType = 'accountindemnity'
                                                JOIN 
                                                    users u 
                                                    ON u.id = sta.staffidtoaction
                                                JOIN 
                                                    checkeraction ca 
                                                    ON ca.id = sta.action
                                                WHERE 
                                                    sta.action = 19 
                                                    AND sta.approvalstatus IN ('Awaiting review', 'Awaiting approval or deny')";
                var PendingStaff = (await con.QueryAsync<Staff2>(query)).ToList();
                return new PrimeAdminResponse() { Success = true, Response = EnumResponse.Successful, Data = PendingStaff };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> GetPendingCustomerActivationOrDeactivationTask()
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                string query = $@"select sta.id as actionid,(select actioname from checkeraction where id=sta.action) as actioname,(select username from users s where s.id=sta.staffidtoaction) as username,(select firstname from users s where s.id=sta.staffidtoaction) as firstname,concat((select firstname from users s where s.id=sta.staffidtoaction),' ',(select lastname from users s where s.id=sta.staffidtoaction)) as name,(select email from staff s where s.id=sta.initiationstaff) as email from staffaction sta where sta.action in (21,25) and sta.approvalstatus=false";
                var PendingStaff = (await con.QueryAsync<Staff2>(query)).ToList();
                return new PrimeAdminResponse() { Success = true, Response = EnumResponse.Successful, Data = PendingStaff };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse2> GetIndemnityRequest(int page, int size, string indemnitytype)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                int skip = page == 0 ? page : (page - 1) * size;
                int take = size;
                if (!indemnitytype.Equals("account", StringComparison.CurrentCultureIgnoreCase) && !indemnitytype.Equals("customer", StringComparison.CurrentCultureIgnoreCase))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.WrongAction };
                }
                if (indemnitytype.Equals("customer", StringComparison.CurrentCultureIgnoreCase))
                {
                    var CustomerIndemnity = (await con.QueryAsync<IndemnityRequest>("select indemnityapproval,Singlewithdrawaltransactionlimit,Dailywithdrawaltransactionlimit,requestpurpose,createdAt,accounttier,indemnitystatus,AccountNumber,IndemnityType,(select u.username from users u where u.id=userid) as username  from customerindemnity where indemnityapproval=false and IndemnityType='customerindemnity' and initiated=false LIMIT @Take OFFSET @Skip", new { Take = take, Skip = skip })).ToList();
                    return new GenericResponse2() { data = CustomerIndemnity, Response = EnumResponse.Successful, Success = true };
                }
                else
                {
                    var AccountIndemnity = (await con.QueryAsync<IndemnityRequest>("select indemnityapproval,Singlewithdrawaltransactionlimit,Dailywithdrawaltransactionlimit,requestpurpose,createdAt,accounttier,indemnitystatus,AccountNumber,IndemnityType,(select u.username from users u where u.id=userid) as username  from customerindemnity where indemnityapproval=false and IndemnityType='accountindemnity' and initiated=false LIMIT @Take OFFSET @Skip", new { Take = take, Skip = skip })).ToList();
                    return new GenericResponse2() { data = AccountIndemnity, Response = EnumResponse.Successful, Success = true };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse2> GetApprovedIndemnityRequest(int page, int size, string indemnitytype)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                int skip = page == 0 ? page : (page - 1) * size;
                int take = size;
                if (!indemnitytype.Equals("account", StringComparison.CurrentCultureIgnoreCase) && !indemnitytype.Equals("customer", StringComparison.CurrentCultureIgnoreCase))
                {
                    return new GenericResponse2() { Success = false, Response = EnumResponse.WrongAction };
                }
                if (indemnitytype.Equals("customer", StringComparison.CurrentCultureIgnoreCase))
                {
                    var CustomerIndemnity = (await con.QueryAsync<IndemnityRequest>("select indemnityapproval,Singlewithdrawaltransactionlimit,Dailywithdrawaltransactionlimit,requestpurpose,createdAt,accounttier,indemnitystatus,AccountNumber,IndemnityType,(select u.username from users u where u.id=userid) as username  from customerindemnity where indemnityapproval=true and IndemnityType='customerindemnity' and indemnitystatus in ('accept','approved') LIMIT @Take OFFSET @Skip", new { Take = take, Skip = skip })).ToList();
                    return new GenericResponse2() { data = CustomerIndemnity, Response = EnumResponse.Successful, Success = true };
                }
                else
                {
                    var AccountIndemnity = (await con.QueryAsync<IndemnityRequest>("select indemnityapproval,Singlewithdrawaltransactionlimit,Dailywithdrawaltransactionlimit,requestpurpose,createdAt,accounttier,indemnitystatus,AccountNumber,IndemnityType,(select u.username from users u where u.id=userid) as username  from customerindemnity where indemnityapproval=true and IndemnityType='accountindemnity' and indemnitystatus in ('accept','approved')  LIMIT @Take OFFSET @Skip", new { Take = take, Skip = skip })).ToList();
                    return new GenericResponse2() { data = AccountIndemnity, Response = EnumResponse.Successful, Success = true };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse> ApproveKycTask(int actionid, string staffNameAndRole, string approveordeny, string shortdescription, string typeofdocument)
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                _logger.LogInformation("staffNameAndRole " + staffNameAndRole);
                //  Users usr = null;
                string name = staffNameAndRole.Contains("_") ? staffNameAndRole.Split('_')[0] : staffNameAndRole; // this will be staff that will approves
                _logger.LogInformation("name " + name);
                var initiationStaff = (await con.QueryAsync<string>("select (select email from staff s where s.id=sf.initiationstaff) as email from kycstaffaction sf where sf.id=@id", new { id = actionid })).FirstOrDefault();
                
                if (initiationStaff.Replace("@trustbancgroup.com", "").Trim().Equals(name.Trim(), StringComparison.CurrentCultureIgnoreCase))
                {
                    return new GenericResponse() { Success = true, Response = EnumResponse.YouareNottheOne };
                }
                
                var validateActionId = (await con.QueryAsync<int>("select id from kycstaffaction where id=@id", new { id = actionid })).FirstOrDefault();
                if (validateActionId == 0)
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.NoAccountExist };
                }
                //get approvalstaff
                var stafftoapprove = (await con.QueryAsync<int>("select id from staff where email=@email", new { email = name + "@trustbancgroup.com" })).FirstOrDefault();
                var PendingAction = (await con.QueryAsync<PendingStaffActionOnProfile>("select st.id,(select c.actioname from checkeraction c where c.id=st.action) as action,(select id from users where id=st.staffidtoaction) as userid from kycstaffaction st where st.approvalstatus=false and st.id=@id", new { id = actionid })).FirstOrDefault();
                if (PendingAction == null)
                {
                    return new GenericResponse() { Success = false, Response = EnumResponse.ActionLikelyApprove };
                }
                if (approveordeny.Equals("approve", StringComparison.CurrentCultureIgnoreCase))
                {
                    if (PendingAction.action.ToLower() == "acceptkyc")
                    {
                        await con.ExecuteAsync("update kycstaffaction set approveordeny=@approve,approvalstaff=@approvalstaff,approvalstatus=true where staffidtoaction=@userid and typeofdocument=@typeofdocument", new { approve = approveordeny, userid = PendingAction.userid, approvalstaff = stafftoapprove, typeofdocument = typeofdocument });
                        //var customeridtoaction = (await con.QueryAsync<int>("select staffidtoaction from staffaction where id=@id", new { id = actionid })).FirstOrDefault();
                        CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)PendingAction.userid);
                        // var username = (await con.QueryAsync<string>("select username from users where id=@id", new { id = customeridtoaction })).FirstOrDefault();    
                        var usr = await _genServ.GetUserbyUsername(customerDataNotFromBvn.username, con);
                        await con.ExecuteAsync("update customerkycstatus set kycstatus=@status,actionid=@actionid where username=@username", new { status = true, username = customerDataNotFromBvn.username, actionid = actionid });
                        if (typeofdocument.Equals("utilitybill", StringComparison.CurrentCultureIgnoreCase))
                        {
                            await con.ExecuteAsync("update kycdocumentstatus set utlitybillstatus='accept',utilityapprovalstatus=true where userid=@id and typeofdocument=@typeofdocument", new { id = usr.Id, typeofdocument = typeofdocument });
                            await con.ExecuteAsync("update document_type set submittedrequest=2 where Document=@Document and userid=@id", new { Document = typeofdocument, id = usr.Id });
                        }
                        else if (typeofdocument.Equals("idcard", StringComparison.CurrentCultureIgnoreCase))
                        {
                            await con.ExecuteAsync("update kycdocumentstatus set idcardstatus='accept',idcardapprovalstatus=true where userid=@id and typeofdocument=@typeofdocument", new { id = usr.Id, typeofdocument = typeofdocument });
                           // await con.ExecuteAsync("update document_type set submittedrequest=2 where Document=@Document and userid=@id", new { Document = typeofdocument, id = usr.Id });
                            await con.ExecuteAsync("update idcard_upload set submittedrequest=2 where userid=@id", new { id = usr.Id });
                        }
                        else if (typeofdocument.Equals("passport", StringComparison.CurrentCultureIgnoreCase))
                        {
                            await con.ExecuteAsync("update kycdocumentstatus set passportstatus='accept',passportapprovalstatus=true where userid=@id and typeofdocument=@typeofdocument", new { id = usr.Id, typeofdocument = typeofdocument });
                            await con.ExecuteAsync("update document_type set submittedrequest=2 where Document=@Document and userid=@id", new { Document = typeofdocument, id = usr.Id });
                        }
                        else if (typeofdocument.Equals("signature", StringComparison.CurrentCultureIgnoreCase))
                        {
                            await con.ExecuteAsync("update kycdocumentstatus set signaturestatus='accept',signatureapprovalstatus=true where userid=@id and actionid=@actionid", new { id = usr.Id, typeofdocument = typeofdocument });
                            await con.ExecuteAsync("update document_type set submittedrequest=2 where Document=@Document and userid=@id", new { Document = typeofdocument, id = usr.Id });
                        }
                        // send mail to customer
                        new Thread(async () =>
                        {
                            SendMailObject sendMailObject = new SendMailObject();
                            sendMailObject.Subject = "Notification on Kyc Documentation";
                            sendMailObject.Email = customerDataNotFromBvn.Email;
                            sendMailObject.Html = $@"<p>Dear {usr.Firstname.ToUpper()}</p>,<p>Please find below the status of your Kyc for {typeofdocument}:</p>
                                              <p>${shortdescription}</p>
                                              <p>For any feedback or enquiries, please call our Contact Center on 07004446147 or send an email to support@trustbancgroup.com.</p> 
                                              <p>Thank you for choosing TrustBanc J6 MfB</p>
                                              ";
                            _genServ.SendMail(sendMailObject);
                            await _smsBLService.SendSmsNotificationToCustomer("Trustbanc Mobile", customerDataNotFromBvn.PhoneNumber, "KYC approved", "KYC");
                        }).Start();
                        return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
                    }
                    else if (PendingAction.action.ToLower() == "rejectkyc")
                    {
                        await con.ExecuteAsync("update kycstaffaction set approveordeny=@approve,approvalstaff=@approvalstaff,approvalstatus=true where staffidtoaction=@userid and typeofdocument=@typeofdocument", new { approve = approveordeny, userid = PendingAction.userid, approvalstaff = stafftoapprove, typeofdocument = typeofdocument });
                        //var customeridtoaction = (await con.QueryAsync<int>("select staffidtoaction from staffaction where id=@id", new { id = actionid })).FirstOrDefault();
                        CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)PendingAction.userid);
                        // var username = (await con.QueryAsync<string>("select username from users where id=@id", new { id = customeridtoaction })).FirstOrDefault();    
                        var usr = await _genServ.GetUserbyUsername(customerDataNotFromBvn.username, con);
                        await con.ExecuteAsync("update customerkycstatus set kycstatus=@status,actionid=@actionid where username=@username", new { status = true, username = customerDataNotFromBvn.username, actionid = actionid });
                        if (typeofdocument.Equals("utilitybill", StringComparison.CurrentCultureIgnoreCase))
                        {
                            await con.ExecuteAsync("update kycdocumentstatus set utlitybillstatus='accept',utilityapprovalstatus=true where userid=@id and typeofdocument=@typeofdocument", new { id = usr.Id, typeofdocument = typeofdocument });
                            await con.ExecuteAsync("update document_type set submittedrequest=2 where Document=@Document and userid=@id", new { Document = typeofdocument, id = usr.Id });
                        }
                        else if (typeofdocument.Equals("idcard", StringComparison.CurrentCultureIgnoreCase))
                        {
                            await con.ExecuteAsync("update kycdocumentstatus set idcardstatus='accept',idcardapprovalstatus=true where userid=@id and typeofdocument=@typeofdocument", new { id = usr.Id, typeofdocument = typeofdocument });
                            // await con.ExecuteAsync("update document_type set submittedrequest=2 where Document=@Document and userid=@id", new { Document = typeofdocument, id = usr.Id });
                            await con.ExecuteAsync("update idcard_upload set submittedrequest=2 where userid=@id", new { id = usr.Id });
                        }
                        else if (typeofdocument.Equals("passport", StringComparison.CurrentCultureIgnoreCase))
                        {
                            await con.ExecuteAsync("update kycdocumentstatus set passportstatus='accept',passportapprovalstatus=true where userid=@id and typeofdocument=@typeofdocument", new { id = usr.Id, typeofdocument = typeofdocument });
                            await con.ExecuteAsync("update document_type set submittedrequest=2 where Document=@Document and userid=@id", new { Document = typeofdocument, id = usr.Id });
                        }
                        else if (typeofdocument.Equals("signature", StringComparison.CurrentCultureIgnoreCase))
                        {
                            await con.ExecuteAsync("update kycdocumentstatus set signaturestatus='accept',signatureapprovalstatus=true where userid=@id and actionid=@actionid", new { id = usr.Id, typeofdocument = typeofdocument });
                            await con.ExecuteAsync("update document_type set submittedrequest=2 where Document=@Document and userid=@id", new { Document = typeofdocument, id = usr.Id });
                        }
                        // send mail to customer
                        new Thread(async () =>
                        {
                            SendMailObject sendMailObject = new SendMailObject();
                            sendMailObject.Subject = "Notification on Kyc Documentation";
                            sendMailObject.Email = customerDataNotFromBvn.Email;
                            sendMailObject.Html = $@"<p>Dear {usr.Firstname.ToUpper()}</p>,<p>Please find below the status of your Kyc for {typeofdocument}:</p>
                                              <p>${shortdescription}</p>
                                              <p>For any feedback or enquiries, please call our Contact Center on 07004446147 or send an email to support@trustbancgroup.com.</p> 
                                              <p>Thank you for choosing TrustBanc J6 MfB</p>
                                              ";
                            _genServ.SendMail(sendMailObject);
                            await _smsBLService.SendSmsNotificationToCustomer("Trustbanc Mobile", customerDataNotFromBvn.PhoneNumber, "KYC approved", "KYC");
                        }).Start();
                        return new GenericResponse() { Success = true, Response = EnumResponse.Successful };
                    }
                    return new GenericResponse() { Success = true, Response = EnumResponse.WrongAction };
                }
                else if (approveordeny.Equals("deny", StringComparison.CurrentCultureIgnoreCase))
                {
                    if (PendingAction.action.ToLower() == "rejectkyc")
                    {
                        await con.ExecuteAsync("update kycstaffaction set approvalstatus=true, approveordeny=@approve,approvalstaff=@approvalstaff where staffidtoaction=@id and typeofdocument=@typeofdocument", new { approve = approveordeny, id = PendingAction.userid, approvalstaff = stafftoapprove, typeofdocument = typeofdocument });
                        var customeridtoaction = (await con.QueryAsync<int>("select staffidtoaction from staffaction where id=@id", new { id = actionid })).FirstOrDefault();
                        CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)PendingAction.userid);
                        // var username = (await con.QueryAsync<string>("select username from users where id=@id", new { id = customeridtoaction })).FirstOrDefault();    
                        var usr = await _genServ.GetUserbyUsername(customerDataNotFromBvn.username, con);
                        await con.ExecuteAsync("update customerkycstatus set kycstatus=@status,actionid=@actionid where username=@username", new { status = false, username = customerDataNotFromBvn.username, actionid = actionid });
                        if (typeofdocument.Equals("utilitybill", StringComparison.CurrentCultureIgnoreCase))
                        {
                            await con.ExecuteAsync("update kycdocumentstatus set utlitybillstatus='reject',utilityapprovalstat=true where userid=@id and typeofdocument=@typeofdocument", new { id = usr.Id, typeofdocument = typeofdocument });
                            await con.ExecuteAsync("update document_type set submittedrequest=2 where Document=@Document and userid=@id", new { Document = typeofdocument, id = usr.Id });
                        }
                        else if (typeofdocument.Equals("idcard", StringComparison.CurrentCultureIgnoreCase))
                        {
                            await con.ExecuteAsync("update kycdocumentstatus set idcardstatus='reject',idcardapprovalstatus=true where userid=@id and typeofdocument=@typeofdocument", new { id = usr.Id, typeofdocument = typeofdocument });
                            await con.ExecuteAsync("update idcard_upload set submittedrequest=2 where userid=@id", new { id = usr.Id });
                        }
                        else if (typeofdocument.Equals("passport", StringComparison.CurrentCultureIgnoreCase))
                        {
                            await con.ExecuteAsync("update kycdocumentstatus set passportstatus='reject',passportapprovalstatus=true where userid=@id and typeofdocument=@typeofdocument", new { id = usr.Id, typeofdocument = typeofdocument });
                            await con.ExecuteAsync("update document_type set submittedrequest=2 where Document=@Document and userid=@id", new { Document = typeofdocument, id = usr.Id });
                        }
                        else if (typeofdocument.Equals("signature", StringComparison.CurrentCultureIgnoreCase))
                        {
                            await con.ExecuteAsync("update kycdocumentstatus set signaturestatus='reject',signatureapprovalstatus=true where userid=@id and typeofdocument=@typeofdocument", new { id = usr.Id, typeofdocument = typeofdocument });
                            await con.ExecuteAsync("update document_type set submittedrequest=2 where Document=@Document and userid=@id", new { Document = typeofdocument, id = usr.Id });
                        }
                        // send mail to customer
                        new Thread(async () =>
                        {
                            SendMailObject sendMailObject = new SendMailObject();
                            sendMailObject.Subject = "Notification on Kyc Documentation";
                            sendMailObject.Email = initiationStaff;
                            sendMailObject.Html = $@"<p>Dear {usr.Firstname.ToUpper()} {usr.LastName.ToUpper()}</p>,<p>Please find below the status of your kyc for {typeofdocument}:</p>
                                              <p>{shortdescription}</p>
                                              <p>For any feedback or enquiries, please call our Contact Center on 07004446147 or send an email to support@trustbancgroup.com.</p> 
                                              <p>Thank you for choosing TrustBanc J6 MfB</p>
                                              ";
                            _genServ.SendMail(sendMailObject);
                            await _smsBLService.SendSmsNotificationToCustomer("Trustbanc Mobile", customerDataNotFromBvn.PhoneNumber, "Kyc rejected", "KYC");
                        }).Start();
                        return new GenericResponse() { Success = true, Response = EnumResponse.Successful, Message = "rejected" };
                    }
                    else if (PendingAction.action.ToLower() == "acceptkyc")
                    {
                        await con.ExecuteAsync("update kycstaffaction set approvalstatus=true, approveordeny=@approve,approvalstaff=@approvalstaff where staffidtoaction=@id and typeofdocument=@typeofdocument", new { approve = approveordeny, id = PendingAction.userid, approvalstaff = stafftoapprove, typeofdocument = typeofdocument });
                        var customeridtoaction = (await con.QueryAsync<int>("select staffidtoaction from staffaction where id=@id", new { id = actionid })).FirstOrDefault();
                        CustomerDataNotFromBvn customerDataNotFromBvn = await CustomerServiceNotFromBvnService.getCustomerRelationService(con, (int)PendingAction.userid);
                        // var username = (await con.QueryAsync<string>("select username from users where id=@id", new { id = customeridtoaction })).FirstOrDefault();    
                        var usr = await _genServ.GetUserbyUsername(customerDataNotFromBvn.username, con);
                        await con.ExecuteAsync("update customerkycstatus set kycstatus=@status,actionid=@actionid where username=@username", new { status = false, username = customerDataNotFromBvn.username, actionid = actionid });
                        if (typeofdocument.Equals("utilitybill", StringComparison.CurrentCultureIgnoreCase))
                        {
                            await con.ExecuteAsync("update kycdocumentstatus set utlitybillstatus='reject',utilityapprovalstat=true where userid=@id and typeofdocument=@typeofdocument", new { id = usr.Id, typeofdocument = typeofdocument });
                            await con.ExecuteAsync("update document_type set submittedrequest=2 where Document=@Document and userid=@id", new { Document = typeofdocument, id = usr.Id });
                        }
                        else if (typeofdocument.Equals("idcard", StringComparison.CurrentCultureIgnoreCase))
                        {
                            await con.ExecuteAsync("update kycdocumentstatus set idcardstatus='reject',idcardapprovalstatus=true where userid=@id and typeofdocument=@typeofdocument", new { id = usr.Id, typeofdocument = typeofdocument });
                            await con.ExecuteAsync("update idcard_upload set submittedrequest=2 where userid=@id", new { id = usr.Id });
                        }
                        else if (typeofdocument.Equals("passport", StringComparison.CurrentCultureIgnoreCase))
                        {
                            await con.ExecuteAsync("update kycdocumentstatus set passportstatus='reject',passportapprovalstatus=true where userid=@id and typeofdocument=@typeofdocument", new { id = usr.Id, typeofdocument = typeofdocument });
                            await con.ExecuteAsync("update document_type set submittedrequest=2 where Document=@Document and userid=@id", new { Document = typeofdocument, id = usr.Id });
                        }
                        else if (typeofdocument.Equals("signature", StringComparison.CurrentCultureIgnoreCase))
                        {
                            await con.ExecuteAsync("update kycdocumentstatus set signaturestatus='reject',signatureapprovalstatus=true where userid=@id and typeofdocument=@typeofdocument", new { id = usr.Id, typeofdocument = typeofdocument });
                            await con.ExecuteAsync("update document_type set submittedrequest=2 where Document=@Document and userid=@id", new { Document = typeofdocument, id = usr.Id });
                        }
                        // send mail to customer
                        new Thread(async () =>
                        {
                            SendMailObject sendMailObject = new SendMailObject();
                            sendMailObject.Subject = "Notification on Kyc Documentation";
                            sendMailObject.Email = initiationStaff; // send to initiator
                            sendMailObject.Html = $@"<p>Dear {usr.Firstname.ToUpper()} {usr.LastName.ToUpper()}</p>,<p>Please find below the status of your kyc for {typeofdocument}:</p>
                                              <p>{shortdescription}</p>
                                              <p>For any feedback or enquiries, please call our Contact Center on 07004446147 or send an email to support@trustbancgroup.com.</p> 
                                              <p>Thank you for choosing TrustBanc J6 MfB</p>
                                              ";
                            _genServ.SendMail(sendMailObject);
                            await _smsBLService.SendSmsNotificationToCustomer("Trustbanc Mobile", customerDataNotFromBvn.PhoneNumber, "Kyc rejected", "KYC");
                        }).Start();
                        return new GenericResponse() { Success = true, Response = EnumResponse.Successful, Message = "rejected" };
                    }
                    return new GenericResponse() { Success = true, Response = EnumResponse.ApprovalDenied };
                }
                else
                {
                    return new GenericResponse() { Success = true, Response = EnumResponse.WrongInput };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse() { Response = EnumResponse.SystemError };
            }

        }

        public async Task<GenericResponse2> CountOfPendingKycInitiationAndApproval()
        {
            try
            {
                using IDbConnection con = _context.CreateConnection();
                string pendingapprovalquery = "select count(*) from kycdocumentstatus where (passportapprovalstatus=false and passportstatus in ('accept','reject')) or (idcardapprovalstatus=false and idcardstatus in ('accept','reject')) or (signatureapprovalstatus=false and signaturestatus in ('accept','reject')) or (utilityapprovalstatus=false and utlitybillstatus in ('accept','reject'));";
                var pendingapproval = (await con.QueryAsync<int>(pendingapprovalquery)).FirstOrDefault();
                string pendinginitiationquery = "select count(*) from kycdocumentstatus where (passportapprovalstatus=false and passportstatus='Awaiting review') or (idcardapprovalstatus=false and idcardstatus='Awaiting review') or (signatureapprovalstatus=false and signaturestatus='Awaiting review') or (utilityapprovalstatus=false and utlitybillstatus='Awaiting review');";
                var pendinginitiation = (await con.QueryAsync<int>(pendinginitiationquery)).FirstOrDefault();
                //pending init and approval for pin
                string pinpendingapprovalquery = "select count(*) from pinrequestchange where initiated=true;";
                var pinpendingapproval = (await con.QueryAsync<int>(pinpendingapprovalquery)).FirstOrDefault();
                string pinpendinginitiationlquery = "select count(*) from pinrequestchange where initiated=false;";
                var pinpendinginitiation = (await con.QueryAsync<int>(pinpendinginitiationlquery)).FirstOrDefault();
                //account pending init and approval for indemnity 
                string accountindemnitypendingapprovalquery = "select count(*) from customerindemnity where initiated=true and IndemnityType='accountindemnity' and indemnityapproval=false and indemnitystatus in ('accept','reject');";
                var accountindemnitypendingapproval = (await con.QueryAsync<int>(accountindemnitypendingapprovalquery)).FirstOrDefault();
                string accountindenitypendinginitiationlquery = "select count(*) from customerindemnity where initiated=true and IndemnityType='accountindemnity' and indemnityapproval=false and indemnitystatus in ('Awaiting review') ;";
                var accountindemnitypendinginitiation = (await con.QueryAsync<int>(accountindenitypendinginitiationlquery)).FirstOrDefault();
                //customer pending init and approval for indemnity 
                string customerindemnitypendingapprovalquery = "select count(*) from customerindemnity where initiated=true and IndemnityType='customerindemnity' and indemnityapproval=false and indemnitystatus in ('accept','reject');";
                var customerindemnitypendingapproval = (await con.QueryAsync<int>(customerindemnitypendingapprovalquery)).FirstOrDefault();
                string customerindenitypendinginitiationlquery = "select count(*) from customerindemnity where initiated=true and IndemnityType='customerindemnity' and indemnityapproval=false and indemnitystatus='Awaiting review';";
                var customerindemnitypendinginitiation = (await con.QueryAsync<int>(customerindenitypendinginitiationlquery)).FirstOrDefault();
                //pendingfor account upgrade approval
                string accountpendingapprovalquery = "select count(*) from accountupgradedviachannel where inititiated=true;";
                var accountpendingapproval = (await con.QueryAsync<int>(accountpendingapprovalquery)).FirstOrDefault();
                //pending user activation and deactivation
                string customeractivationordeactivationpendinginitiationquery = "select count(*) from staffaction where approvalstatus=false and action=21;";
                var customeractivationordeactivationpending = (await con.QueryAsync<int>(customeractivationordeactivationpendinginitiationquery)).FirstOrDefault();
                return new GenericResponse2() { Success = true, Response = EnumResponse.Successful, data = new { kycpendingapproval = pendingapproval, kycpendinginitiation = pendinginitiation
                     ,accountindemnitypendingapproval=accountindemnitypendingapproval,accountindemnitypendinginitiation=accountindemnitypendinginitiation,
                    pinpendingapproval = pinpendingapproval,pinpendinginitiation=pinpendinginitiation,customerindemnitypendinginitiation=customerindemnitypendinginitiation,
                     customerindemnitypendingapproval=customerindemnitypendingapproval,
                     accountpendingapproval=accountpendingapproval,
                    customeractivationordeactivationpending = customeractivationordeactivationpending
                }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse2> ApproveTransactionCappedLimit(string approveordeny, TransactionCappedLimit setTransactionCappedLimit, int actionid, string staffNameAndRole)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    //  Users usr = null;
                    string name = staffNameAndRole.Contains("_") ? staffNameAndRole.Split('_')[0] : staffNameAndRole; // this will be staff that will approves
                    _logger.LogInformation("name " + name);
                    var initiationStaff = (await con.QueryAsync<string>("select (select email from staff s where s.id=sf.initiationstaff) as email from staffaction sf where sf.id=@id", new { id = actionid })).FirstOrDefault();

                    if (initiationStaff.Replace("@trustbancgroup.com", "").Trim().Equals(name.Trim(), StringComparison.CurrentCultureIgnoreCase))
                    {
                        return new GenericResponse2() { Success = true, Response = EnumResponse.YouareNottheOne };
                    }

                    var validateActionId = (await con.QueryAsync<int>("select id from staffaction where id=@id", new { id = actionid })).FirstOrDefault();
                    if (validateActionId == 0)
                    {
                        return new GenericResponse2() { Success = false, Response = EnumResponse.NoAccountExist };
                    }
                    //get approvalstaff
                    var stafftoapprove = (await con.QueryAsync<int>("select id from staff where email=@email", new { email = name + "@trustbancgroup.com" })).FirstOrDefault();
                    var PendingAction = (await con.QueryAsync<PendingStaffActionOnProfile>("select st.id,(select c.actioname from checkeraction c where c.id=st.action) as action from staffaction st where st.approvalstatus=false and st.action=@id", new { id = actionid })).FirstOrDefault();
                    if (PendingAction == null)
                    {
                        return new GenericResponse2() { Success = false, Response = EnumResponse.ActionLikelyApprove };
                    }
                    if (approveordeny.Equals("approve", StringComparison.CurrentCultureIgnoreCase))
                    {
                        if (PendingAction.action.ToLower() == "initiatetranscappedlimit")
                        {
                            var CappedLimit = (await con.QueryAsync<TransactionCappedLimit>("select * from transactioncappedlimit")).FirstOrDefault();
                            //insert into cappedlimit
                            var StaggingCappedLimit = (await con.QueryAsync<TransactionCappedLimit>("select * from StagingTransactionCappedLimit")).FirstOrDefault();
                            if (CappedLimit == null)
                            {
                                await con.ExecuteAsync($@"insert into transactioncappedlimit(SingleTransactionLimit,DailyCummulativeLimit,actionid,Initiated,createdAt)
                                                 values(@SingleTransactionLimit,@DailyCummulativeLimit,@actionid,@Initiated,now())", new { SingleTransactionLimit = StaggingCappedLimit.SingleTransactionLimit, DailyCummulativeLimit = StaggingCappedLimit.DailyCummulativeLimit, actionid = StaggingCappedLimit.actionid, Initiated = true, ApprovalStatus=true });
                            }
                            else
                            {
                                await con.ExecuteAsync($@"update transactioncappedlimit set
                                                  SingleTransactionLimit=@SingleTransactionLimit,
                                                  DailyCummulativeLimit=@DailyCummulativeLimit,
                                                  Initiated=@Initiated,
                                                  actionid=@actionid,
                                                  ApprovalStatus=@ApprovalStatus,
                                                  createdAt=@createdAt
                                                    ", new
                                {
                                    SingleTransactionLimit = StaggingCappedLimit.SingleTransactionLimit,
                                    DailyCummulativeLimit = StaggingCappedLimit.DailyCummulativeLimit,
                                    Initiated = true,
                                    actionid = StaggingCappedLimit.actionid,
                                    ApprovalStatus = true,
                                    createdAt = DateTime.Now
                                });
                            }
                            await con.ExecuteAsync("update transactioncappedlimit set ApprovalStatus=true,ApprovedStaff=@email", new { email = name + "@trustbancgroup.com" });
                            await con.ExecuteAsync("update staffaction set approvalstatus=true,approveordeny=@approve,approvalstaff=@approvalstaff where id=@id", new { approve = approveordeny, id = actionid, approvalstaff = stafftoapprove });
                            SendMailObject sendMailObject = new SendMailObject();
                            sendMailObject.Email = initiationStaff;
                            sendMailObject.Html = $@"<p>Dear {name}</p>,
                                                    <p>The Capped transaction was approved.</p>
                                                    ";
                            Task.Run(() =>
                            {
                                _genServ.SendMail(sendMailObject);
                            });
                        }
                    }
                    else if (approveordeny.Equals("deny", StringComparison.CurrentCultureIgnoreCase))
                    {
                        if (PendingAction.action.ToLower() == "initiatetranscappedlimit")
                        {
                           // await con.ExecuteAsync("update StagingTransactionCappedLimit set ApprovalStatus=true,ApprovedStaff=@email", new { email = name + "@trustbancgroup.com" });
                            await con.ExecuteAsync("delete from StagingTransactionCappedLimit"); // clear the staging if denied
                            await con.ExecuteAsync("update staffaction set approvalstatus=true,approveordeny=@approve,approvalstaff=@approvalstaff where id=@id", new { approve = approveordeny, id = actionid, approvalstaff = stafftoapprove });
                            SendMailObject sendMailObject = new SendMailObject();
                            sendMailObject.Email = initiationStaff;
                            sendMailObject.Html = $@"<p>Dear {name}</p>,
                                                    <p>The Capped transaction was not approved.</p>
                                                    ";
                            Task.Run(() =>
                            {
                                _genServ.SendMail(sendMailObject);
                            });
                        }
                    }
                }
                return new GenericResponse2() { Success = true, Response = EnumResponse.Successful };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse2> InitiateTransactionCappedLimit(string action, TransactionCappedLimit transactionCappedLimit, string StaffNameAndRole)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var actions = (await con.QueryAsync<string>("select actioname from checkeraction")).ToList();
                    _logger.LogInformation("action " + actions + " actions " + string.Join(",", actions));
                    if (!actions.Any(a => a.Equals(action, StringComparison.OrdinalIgnoreCase)))
                    {
                        return new GenericResponse2() { Success = false, Response = EnumResponse.WrongAction };
                    }
                    var InitiatedActionId = (await con.QueryAsync<int>("select id from checkeraction where actioname=@action", new { action = action })).FirstOrDefault();
                    if (InitiatedActionId == 0)
                    {
                        return new GenericResponse2() { Success = false, Response = EnumResponse.WrongAction };
                    }
                    var name = StaffNameAndRole.Split('_')[0];
                    var staffid2 = (await con.QueryAsync<int>("select id from staff where email=@email", new { email = name + "@trustbancgroup.com" })).FirstOrDefault();
                    Console.WriteLine("staffid2 " + staffid2);
                    var CappedLimit = (await con.QueryAsync<TransactionCappedLimit>("select * from StagingTransactionCappedLimit")).FirstOrDefault();
                    //insert into cappedlimit
                    if (CappedLimit == null)
                    {
                        /*
                        await con.ExecuteAsync($@"insert into transactioncappedlimit(SingleTransactionLimit,DailyCummulativeLimit,actionid,Initiated,createdAt)
                                         values(@SingleTransactionLimit,@DailyCummulativeLimit,@actionid,@Initiated,now())", new { SingleTransactionLimit = transactionCappedLimit.SingleTransactionLimit, DailyCummulativeLimit = transactionCappedLimit.DailyCummulativeLimit, actionid = InitiatedActionId, Initiated = true });
                        */
                               await con.ExecuteAsync($@"insert into StagingTransactionCappedLimit(SingleTransactionLimit,DailyCummulativeLimit,actionid,Initiated,createdAt)
                                         values(@SingleTransactionLimit,@DailyCummulativeLimit,@actionid,@Initiated,now())", new { SingleTransactionLimit = transactionCappedLimit.SingleTransactionLimit, DailyCummulativeLimit = transactionCappedLimit.DailyCummulativeLimit, actionid = InitiatedActionId, Initiated = true });
                       
                        await con.ExecuteAsync($@"insert into staffaction(action,initiationstaff,choosenstaff,staffidtoaction,createdAt)
                                         values(@action,@initiationstaff,@choosenstaff,@staffidtoaction,now())", new { action = InitiatedActionId, initiationstaff = staffid2, choosenstaff = "", staffidtoaction = staffid2 });
                    }
                    else
                    {
                        await con.ExecuteAsync("delete from staffaction where approvalstatus=false and action=@action", new { action = InitiatedActionId });
                        await con.ExecuteAsync($@"update StagingTransactionCappedLimit set
                                                  SingleTransactionLimit=@SingleTransactionLimit,
                                                  DailyCummulativeLimit=@DailyCummulativeLimit,
                                                  Initiated=@Initiated,
                                                  actionid=@actionid,
                                                  ApprovalStatus=@ApprovalStatus,
                                                  createdAt=@createdAt
                                                    ", new
                        {
                            SingleTransactionLimit = transactionCappedLimit.SingleTransactionLimit,
                            DailyCummulativeLimit = transactionCappedLimit.DailyCummulativeLimit,
                            Initiated = true,
                            actionid = InitiatedActionId,
                            ApprovalStatus = false,
                            createdAt = DateTime.Now
                        });
                        await con.ExecuteAsync($@"insert into staffaction(action,initiationstaff,choosenstaff,staffidtoaction,createdAt)
                                          values(@action,@initiationstaff,@choosenstaff,@staffidtoaction,now())", new { action = InitiatedActionId, initiationstaff = staffid2, choosenstaff = "", staffidtoaction = staffid2 });
                    }
                    return new GenericResponse2() { Success = true, Response = EnumResponse.Successful };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
        }
        public async Task<GenericResponse2> GetPendingTransactionCappedLimit()
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var TransactionCappedLimit = (await con.QueryAsync<TransactionCappedLimit>("select t.SingleTransactionLimit,t.DailyCummulativeLimit,t.Initiated,t.actionid from StagingTransactionCappedLimit t join staffaction sta on t.actionid=sta.action where t.Initiated=true and t.Approvalstatus=false and sta.approvalstatus=false")).FirstOrDefault();
                    if (TransactionCappedLimit==null)
                    {
                        return new GenericResponse2() { Success = true, Response = EnumResponse.NonPending };
                    }
                    return new GenericResponse2() { Success = true, Response = EnumResponse.Successful, data = TransactionCappedLimit };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
        }

        public async Task<GenericResponse2> GetCappedTransactionLimit()
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var TransactionCappedLimit = (await con.QueryAsync<TransactionCappedLimit>("select * from transactioncappedlimit where ApprovalStatus=true")).FirstOrDefault();
                    if (TransactionCappedLimit == null)
                    {
                        return new GenericResponse2() { Success = false, Response = EnumResponse.NotApprovetYet };
                    }
                    return new GenericResponse2() { Success = true, Response = EnumResponse.Successful, data = TransactionCappedLimit };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message + " " + ex.Message);
                return new GenericResponse2() { Response = EnumResponse.SystemError };
            }
        }
    }
}



























































































































































