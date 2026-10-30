using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Shuffull.Api.Tools.Authorization;

/// <summary>
/// Guards the producer (funnel) endpoints with the shared secret: the <c>X-Import-Key</c> header must equal config
/// <c>Shuffull:Import:Key</c> (env <c>Shuffull__Import__Key</c>). An unset key on the site refuses every caller.
/// Anything else is a 401, the same status each endpoint returned when it checked the key itself.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ProducerKeyAttribute : Attribute, IAuthorizationFilter
{
    public const string HeaderName = "X-Import-Key";
    public const string ConfigKey = "Shuffull:Import:Key";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var expected = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()[ConfigKey];
        var supplied = context.HttpContext.Request.Headers[HeaderName].FirstOrDefault();

        if (!Matches(supplied, expected))
        {
            context.Result = new UnauthorizedResult();
        }
    }

    /// <summary>
    /// Constant-time compare. Hashing both sides first gives equal-length inputs, so the time doesn't leak the
    /// key's length either.
    /// </summary>
    internal static bool Matches(string? supplied, string? expected)
    {
        if (string.IsNullOrEmpty(expected) || supplied is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
    }
}
