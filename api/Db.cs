using System.Data;
using Microsoft.Data.SqlClient;

namespace Api;

// Hands out a new SQL connection. Always use it with `using`.
public class Db(IConfiguration config)
{
    public IDbConnection Connect() => new SqlConnection(config.GetConnectionString("Default"));
}
