using System.Net.Http.Headers;
using System.Text;

namespace WebApp.Api.Middleware;

/// <summary>
/// HTTP Basic Auth gate. Reads credentials from WEB_APP_USERNAME / WEB_APP_PASSWORD env vars.
/// When those vars are absent the middleware is a no-op (local dev, DISABLE_AUTH scenarios).
/// Excluded paths: /api/health (container probe).
/// </summary>
public class BasicAuthMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<BasicAuthMiddleware> logger)
{
    private static readonly string[] ExcludedPaths = ["/api/health"];
    private static int _partialConfigWarningEmitted;

    public async Task InvokeAsync(HttpContext context)
    {
        var username = configuration["WEB_APP_USERNAME"];
        var password = configuration["WEB_APP_PASSWORD"];

        // No-op when credentials are not configured
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            if (!string.IsNullOrEmpty(username) ^ !string.IsNullOrEmpty(password))
            {
                if (Interlocked.Exchange(ref _partialConfigWarningEmitted, 1) == 0)
                {
                    logger.LogWarning("Basic auth is partially configured. Both WEB_APP_USERNAME and WEB_APP_PASSWORD are required.");
                }
            }

            await next(context);
            return;
        }

        // Skip health probe
        if (ExcludedPaths.Contains(context.Request.Path.Value, StringComparer.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        if (!TryAuthenticate(context.Request, username, password))
        {
            logger.LogWarning("Basic auth failed for {Path} from {IP}",
                context.Request.Path, context.Connection.RemoteIpAddress);

            context.Response.Headers.WWWAuthenticate = "Basic realm=\"Web App\", charset=\"UTF-8\"";
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }

    private static bool TryAuthenticate(HttpRequest request, string expectedUsername, string expectedPassword)
    {
        if (!request.Headers.TryGetValue("Authorization", out var authHeader))
            return false;

        if (!AuthenticationHeaderValue.TryParse(authHeader, out var header) ||
            !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase) ||
            header.Parameter is null)
            return false;

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
        }
        catch
        {
            return false;
        }

        var colonIndex = decoded.IndexOf(':');
        if (colonIndex < 0) return false;

        var providedUser = decoded[..colonIndex];
        var providedPass = decoded[(colonIndex + 1)..];

        // Constant-time comparison to prevent timing attacks
        return CryptographicEquals(providedUser, expectedUsername) &&
               CryptographicEquals(providedPass, expectedPassword);
    }

    /// <summary>Fixed-time string comparison to prevent timing side-channels.</summary>
    private static bool CryptographicEquals(string a, string b)
    {
        var bytesA = Encoding.UTF8.GetBytes(a);
        var bytesB = Encoding.UTF8.GetBytes(b);

        if (bytesA.Length != bytesB.Length)
        {
            // Still compare to keep timing consistent
            _ = System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(bytesA, bytesA);
            return false;
        }

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }
}
