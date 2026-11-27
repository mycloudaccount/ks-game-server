using System.Security.Cryptography;
using System.Text;

namespace KingdomStackServer.Api.Hosting;

/// <summary>Middleware for running behind a proxy (CloudFront in AWS, Vite locally).</summary>
public static class CloudFrontHosting
{
    /// <summary>Header CloudFront adds to every request it forwards to the Lambda function URL.</summary>
    public const string OriginVerifyHeader = "X-Ks-Origin-Verify";

    /// <summary>
    /// When OriginVerify:Secret is set, rejects every request that lacks CloudFront's secret header,
    /// so the public function URL can't be used to bypass CloudFront.
    /// </summary>
    public static void UseOriginVerification(this WebApplication app)
    {
        var secret = app.Configuration["OriginVerify:Secret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            return;
        }

        var expected = Encoding.UTF8.GetBytes(secret);
        app.Use(async (context, next) =>
        {
            var provided = Encoding.UTF8.GetBytes(context.Request.Headers[OriginVerifyHeader].ToString());
            if (!CryptographicOperations.FixedTimeEquals(provided, expected))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            context.Request.Headers.Remove(OriginVerifyHeader);
            await next();
        });
    }

    /// <summary>
    /// When Cognito:PublicOrigin is set, makes Request.Scheme and Request.Host match the address the
    /// browser uses. Behind CloudFront the request otherwise carries the function URL's host, so absolute
    /// links (loadUrl, asset URLs) would bypass CloudFront and miss the sign-in cookie.
    /// </summary>
    public static void UsePublicOrigin(this WebApplication app)
    {
        var configured = app.Configuration["Cognito:PublicOrigin"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        var origin = new Uri(configured.TrimEnd('/'), UriKind.Absolute);
        var host = origin.IsDefaultPort ? new HostString(origin.Host) : new HostString(origin.Host, origin.Port);
        app.Use((context, next) =>
        {
            context.Request.Scheme = origin.Scheme;
            context.Request.Host = host;
            return next();
        });
    }
}
