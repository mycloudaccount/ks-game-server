using KingdomStackServer.Api;

var builder = WebApplication.CreateBuilder(args);

const string ClientCorsPolicy = "ClientCors";

builder.Services.Configure<AzureBlobProxyOptions>(
    builder.Configuration.GetSection(AzureBlobProxyOptions.SectionName));
builder.Services.AddSingleton<AzureBlobProxyService>();
builder.Services.AddControllers();
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

app.UseCors(ClientCorsPolicy);
app.UseHttpsRedirection();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "KingdomStackServer.Api v1");
        options.RoutePrefix = "swagger";
    });
}

app.MapControllers();

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
