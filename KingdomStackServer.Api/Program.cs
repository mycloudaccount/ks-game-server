using KingdomStackServer.Api;

var builder = WebApplication.CreateBuilder(args);

const string LocalClientCorsPolicy = "LocalClient";

builder.Services.Configure<AzureBlobProxyOptions>(
    builder.Configuration.GetSection(AzureBlobProxyOptions.SectionName));
builder.Services.AddSingleton<AzureBlobProxyService>();
builder.Services.AddControllers();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy(LocalClientCorsPolicy, policy =>
    {
        policy.WithOrigins(
                "http://localhost:4000",
                "http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

app.UseCors(LocalClientCorsPolicy);
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
    soundsBundleUrl = "/api/assets/sounds/bundle"
}));

app.Run();
