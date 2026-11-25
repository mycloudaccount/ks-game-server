using Amazon.Lambda.AspNetCoreServer;
using Amazon.Lambda.AspNetCoreServer.Hosting;
using Amazon.S3;
using Microsoft.AspNetCore.DataProtection;
using KingdomStackServer.Api;
using KingdomStackServer.Api.Auth;
using KingdomStackServer.Api.Hosting;
using KingdomStackServer.Api.Storage;

var builder = WebApplication.CreateBuilder(args);

const string ClientCorsPolicy = "ClientCors";

// On AWS, secrets (e.g. /kingdom-stack/dev/config/Cognito/ClientSecret -> Cognito:ClientSecret)
// come from SSM Parameter Store. Only enabled when the path is set, so local runs are unaffected.
var ssmConfigPath = builder.Configuration["KS_SSM_CONFIG_PATH"];
if (!string.IsNullOrWhiteSpace(ssmConfigPath))
{
    builder.Configuration.AddSystemsManager(ssmConfigPath);
}

// Runs as a normal Kestrel app locally; inside AWS Lambda it serves function URL requests instead.
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi, lambda =>
{
    // Lambda's adapter sends any content type it doesn't know as text, which corrupts binary files
    // such as .glb models (model/gltf-binary), audio and fonts. Send unknown types as binary instead.
    lambda.DefaultResponseContentEncoding = ResponseContentEncoding.Base64;
    foreach (var textType in new[] { "text/plain", "text/html", "text/css", "text/javascript", "application/javascript", "application/problem+json", "image/svg+xml" })
    {
        lambda.RegisterResponseContentEncodingForContentType(textType, ResponseContentEncoding.Default);
    }
});

// Lambda instances come and go, so the keys that encrypt the sign-in cookie must be shared.
var dataProtectionSsmPath = builder.Configuration["DataProtection:SsmPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionSsmPath))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("kingdom-stack")
        .PersistKeysToAWSSystemsManager(dataProtectionSsmPath);
}

builder.Services.Configure<AzureBlobProxyOptions>(
    builder.Configuration.GetSection(AzureBlobProxyOptions.SectionName));
builder.Services.Configure<IdentityOptions>(
    builder.Configuration.GetSection(IdentityOptions.SectionName));

// AWS settings (profile, region) come from the "AWS" config section; on AWS compute the role is used.
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonS3>();
builder.Services.AddSingleton<BlobStoreFactory>();

var identityMode = builder.Configuration[$"{IdentityOptions.SectionName}:Mode"] ?? IdentityModes.AzureCredential;
var useCognito = string.Equals(identityMode, IdentityModes.Cognito, StringComparison.OrdinalIgnoreCase);
if (useCognito)
{
    builder.AddCognitoAuthentication();
    builder.Services.AddSingleton<IUserContextProvider, CognitoUserContextProvider>();
}
else if (string.Equals(identityMode, IdentityModes.Configured, StringComparison.OrdinalIgnoreCase))
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            $"{IdentityOptions.SectionName}:Mode={IdentityModes.Configured} is only allowed in Development.");
    }

    builder.Services.AddSingleton<IUserContextProvider, ConfiguredUserContextProvider>();
}
else
{
    builder.Services.AddSingleton<IUserContextProvider, AzureCredentialUserContextProvider>();
}

builder.Services.AddSingleton<AzureBlobProxyService>();
builder.Services.AddControllers(options => options.Filters.Add<AssetFileResultFilter>());
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy(ClientCorsPolicy, policy =>
    {
        var configuredOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>();

        var allowedOrigins = (configuredOrigins ?? [])
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (allowedOrigins.Length == 0 && builder.Environment.IsDevelopment())
        {
            allowedOrigins =
            [
                "http://localhost:4000",
                "http://localhost:5173"
            ];
        }

        if (allowedOrigins.Length == 0)
        {
            return;
        }

        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

app.UseOriginVerification();
app.UsePublicOrigin();

app.UseCors(ClientCorsPolicy);
app.UseHttpsRedirection();

if (useCognito)
{
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapCognitoAuthEndpoints();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "KingdomStackServer.Api v1");
        options.RoutePrefix = "swagger";
    });
}

var controllers = app.MapControllers();
if (useCognito)
{
    // Every data and asset route requires a signed-in user; only /api/auth/* and "/" are open.
    controllers.RequireAuthorization();
}

app.MapGet("/", () => Results.Ok(new
{
    name = "KingdomStackServer.Api",
    status = "running",
    tilesBaseUrl = "/api/assets/tiles",
    tilesBundleUrl = "/api/assets/tiles/bundle",
    soundsBaseUrl = "/api/assets/sounds",
    soundsBundleUrl = "/api/assets/sounds/bundle",
    clientMessagesUrl = "/api/client-messages",
    footTrailParticleEffectsUrl = "/api/assets/particle-effects/foot-trails",
    landingImpactParticleEffectsUrl = "/api/assets/particle-effects/landing-impacts"
}));

app.Run();
