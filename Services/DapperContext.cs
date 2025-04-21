using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using System.Data;

namespace Retailbanking.BL.Services
{
    public class DapperContext
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        public DapperContext(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("dbconn");
        }
        public IDbConnection CreateConnection()
            => new MySql.Data.MySqlClient.MySqlConnection(_connectionString);
    }
}
