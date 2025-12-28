using Hangfire;
using Hangfire.Dashboard;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Lingarr.Server.Filters;

public class LingarrAuthorizationFilter : IDashboardAuthorizationFilter
{
    private readonly string _username;
    private readonly string _password;

    public LingarrAuthorizationFilter(string username, string password)
    {
        _username = username;
        _password = password;
    }

    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        string? header = httpContext.Request.Headers["Authorization"];

        if (!string.IsNullOrWhiteSpace(header))
        {
            if (AuthenticationHeaderValue.TryParse(header, out var authHeader) &&
                "Basic".Equals(authHeader.Scheme, StringComparison.OrdinalIgnoreCase) &&
                authHeader.Parameter != null)
            {
                try
                {
                    var credentialBytes = Convert.FromBase64String(authHeader.Parameter);
                    var credentials = Encoding.UTF8.GetString(credentialBytes).Split(':', 2);

                    if (credentials.Length == 2)
                    {
                        var username = credentials[0];
                        var password = credentials[1];

                        if (TimeConstantCompare(_username, username) &&
                            TimeConstantCompare(_password, password))
                        {
                            return true;
                        }
                    }
                }
                catch
                {
                    // Invalid base64 or other parsing error, return false
                }
            }
        }

        // Return 401 and WWW-Authenticate header
        httpContext.Response.Headers.Append("WWW-Authenticate", "Basic realm=\"Hangfire Dashboard\"");
        httpContext.Response.StatusCode = 401;

        return false;
    }

    private static bool TimeConstantCompare(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
