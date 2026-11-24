using System.Security.Claims;
using KingdomStackServer.Api.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace KingdomStackServer.Api.Auth;

/// <summary>
/// Cookie-based sign-in through Amazon Cognito's hosted sign-in page (OIDC authorization code + PKCE).
/// The browser only ever holds an HttpOnly session cookie, so existing same-origin fetches with
/// credentials: "include" and plain asset URLs (img src, model loaders) keep working.
/// </summary>
public sealed class CognitoOptions
{
    public const string SectionName = "Cognito";

    /// <summary>https://cognito-idp.{region}.amazonaws.com/{userPoolId}</summary>
    public string Authority { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Hosted sign-in domain, e.g. https://kingdomstack.auth.us-east-1.amazoncognito.com</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>
    /// The origin the browser sees (e.g. http://localhost:4000 or https://kingdomstack.org).
    /// Used for the OIDC callback URL and to reject redirects to other sites.
    /// </summary>
    public string PublicOrigin { get; set; } = string.Empty;
}

public static class CognitoAuthentication
{
    public const string CallbackPath = "/api/auth/callback";
    private const string SessionCookieName = "ks_session";

    public static void AddCognitoAuthentication(this WebApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection(CognitoOptions.SectionName);
        builder.Services.Configure<CognitoOptions>(section);
        var options = section.Get<CognitoOptions>() ?? new CognitoOptions();

        foreach (var (name, value) in new[]
                 {
                     (nameof(options.Authority), options.Authority),
                     (nameof(options.ClientId), options.ClientId),
                     (nameof(options.ClientSecret), options.ClientSecret),
                     (nameof(options.Domain), options.Domain),
                     (nameof(options.PublicOrigin), options.PublicOrigin)
                 })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{CognitoOptions.SectionName}:{name} must be configured.");
            }
        }

        var publicOrigin = options.PublicOrigin.TrimEnd('/');

        builder.Services.AddHttpContextAccessor();
        builder.Services
            .AddAuthentication(auth =>
            {
                auth.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                // API calls without a session get 401 (see OnRedirectToLogin), never a redirect.
                auth.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(cookie =>
            {
                cookie.Cookie.Name = SessionCookieName;
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Lax;
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                cookie.ExpireTimeSpan = TimeSpan.FromDays(7);
                cookie.SlidingExpiration = true;
                cookie.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                cookie.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(oidc =>
            {
                oidc.Authority = options.Authority;
                oidc.ClientId = options.ClientId;
                oidc.ClientSecret = options.ClientSecret;
                oidc.ResponseType = "code";
                oidc.ResponseMode = "query"; // Cognito does not support form_post.
                oidc.UsePkce = true;
                oidc.CallbackPath = CallbackPath;
                oidc.SaveTokens = false;
                oidc.GetClaimsFromUserInfoEndpoint = false;
                oidc.MapInboundClaims = false;
                oidc.TokenValidationParameters.NameClaimType = "email";
                oidc.Scope.Clear();
                oidc.Scope.Add("openid");
                oidc.Scope.Add("email");
                oidc.Scope.Add("profile");
                oidc.CorrelationCookie.SameSite = SameSiteMode.Lax;
                oidc.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                oidc.NonceCookie.SameSite = SameSiteMode.Lax;
                oidc.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                oidc.Events.OnRedirectToIdentityProvider = context =>
                {
                    // The API sits behind a proxy (Vite locally, CloudFront in production), so build the
                    // callback URL from the configured public origin rather than the request's Host.
                    context.ProtocolMessage.RedirectUri = publicOrigin + CallbackPath;
                    return Task.CompletedTask;
                };
            });

        builder.Services.AddAuthorization();
    }

    public static void MapCognitoAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").AllowAnonymous();

        group.MapGet("/login", (string? post_login_redirect_uri, IOptions<CognitoOptions> options) =>
            Results.Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = ToSafeLocalPath(post_login_redirect_uri, options.Value.PublicOrigin),
                    IsPersistent = true
                },
                [OpenIdConnectDefaults.AuthenticationScheme]));

        group.MapGet("/logout", async (HttpContext context, IOptions<CognitoOptions> options) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            // Also end the Cognito hosted session, then come back to the site root
            // (which must be one of the app client's allowed sign-out URLs).
            var value = options.Value;
            var logoutUri = Uri.EscapeDataString(value.PublicOrigin.TrimEnd('/') + "/");
            return Results.Redirect(
                $"{value.Domain.TrimEnd('/')}/logout?client_id={Uri.EscapeDataString(value.ClientId)}&logout_uri={logoutUri}");
        });

        // Same response shape as Azure Static Web Apps' /.auth/me, so the editor's parser is unchanged.
        group.MapGet("/me", (HttpContext context) =>
        {
            var user = context.User;
            if (user.Identity?.IsAuthenticated != true)
            {
                return Results.Ok(new { clientPrincipal = (object?)null });
            }

            var sub = user.FindFirstValue("sub");
            var email = user.FindFirstValue("email");
            var name = user.FindFirstValue("name");
            var claims = new List<object>();
            if (!string.IsNullOrWhiteSpace(email))
            {
                claims.Add(new { typ = "email", val = email });
            }
            if (!string.IsNullOrWhiteSpace(name))
            {
                claims.Add(new { typ = "name", val = name });
            }

            return Results.Ok(new
            {
                clientPrincipal = new
                {
                    identityProvider = "cognito",
                    userId = sub,
                    userDetails = email ?? sub,
                    userRoles = new[] { "anonymous", "authenticated" },
                    claims
                }
            });
        });
    }

    /// <summary>Accepts only same-origin return URLs and turns them into a local path, preventing open redirects.</summary>
    private static string ToSafeLocalPath(string? returnUrl, string publicOrigin)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }

        if (returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\"))
        {
            return returnUrl;
        }

        if (Uri.TryCreate(returnUrl, UriKind.Absolute, out var absolute)
            && Uri.TryCreate(publicOrigin, UriKind.Absolute, out var origin)
            && Uri.Compare(absolute, origin, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0)
        {
            return absolute.PathAndQuery + absolute.Fragment;
        }

        return "/";
    }
}

/// <summary>Uses the signed-in Cognito user's "sub" as the storage scope key.</summary>
public sealed class CognitoUserContextProvider(IHttpContextAccessor httpContextAccessor) : IUserContextProvider
{
    public Task<StorageUserContext> GetUserContextAsync(CancellationToken cancellationToken)
    {
        var user = GetSignedInUser();
        var sub = user.FindFirstValue("sub")!;
        var scopeKey = ScopeKeys.Normalize(sub)
            ?? throw new UnauthorizedAccessException("The signed-in user has no usable 'sub' claim.");

        return Task.FromResult(new StorageUserContext(
            scopeKey,
            sub,
            user.FindFirstValue("email"),
            user.FindFirstValue("name")));
    }

    public Task<AzureStorageIdentityInfo> GetIdentityInfoAsync(CancellationToken cancellationToken)
    {
        var claims = GetSignedInUser().Claims
            .GroupBy(claim => claim.Type, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(claim => claim.Value).ToArray(), StringComparer.OrdinalIgnoreCase);

        return Task.FromResult(new AzureStorageIdentityInfo("Cognito", DateTimeOffset.MaxValue, claims));
    }

    private ClaimsPrincipal GetSignedInUser()
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(user.FindFirstValue("sub")))
        {
            throw new UnauthorizedAccessException("No signed-in user.");
        }

        return user;
    }
}
