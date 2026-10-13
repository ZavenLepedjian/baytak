using Baytak.Api.Auth;
using Baytak.Api.Auth.Sms;
using Baytak.Api.Common;
using Baytak.Api.Data;
using Baytak.Api.Functions;
using Baytak.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();
builder.UseMiddleware<SecurityHeadersMiddleware>();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// Application Insights adds a default filter that hides Information logs from the app; lift it.
builder.Logging.Services.Configure<LoggerFilterOptions>(options =>
{
    var rule = options.Rules.FirstOrDefault(r =>
        r.ProviderName == "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");
    if (rule is not null) options.Rules.Remove(rule);
});

var config = builder.Configuration;
var isDevelopment = string.Equals(config["AZURE_FUNCTIONS_ENVIRONMENT"], "Development", StringComparison.OrdinalIgnoreCase);

// ---- configuration
builder.Services.Configure<JwtOptions>(config.GetSection("Jwt"));
builder.Services.Configure<OtpOptions>(config.GetSection("Otp"));
builder.Services.Configure<SmsOptions>(config.GetSection("Sms"));
builder.Services.Configure<PushOptions>(config.GetSection("Push"));
builder.Services.Configure<PhoneOptions>(config.GetSection("Phone"));

// ---- database: Azure SQL in production, SQLite for local development
var provider = config["Database:Provider"] ?? "SqlServer";
var connection = config.GetConnectionString("Baytak") ?? config["ConnectionStrings:Baytak"];
if (string.IsNullOrWhiteSpace(connection))
    throw new InvalidOperationException("Set ConnectionStrings:Baytak (env var ConnectionStrings__Baytak).");

builder.Services.AddDbContext<BaytakDbContext>(options =>
{
    if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)) options.UseSqlite(connection);
    else options.UseSqlServer(connection, sql => sql.EnableRetryOnFailure());
});

// ---- services
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddScoped<IAccessGuard, AccessGuard>();
builder.Services.AddScoped<IOtpService, OtpService>();

builder.Services.AddHttpClient(TwilioSmsSender.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient(ExpoPushSender.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddScoped<IPushSender, ExpoPushSender>();

var smsProvider = config["Sms:Provider"] ?? "Twilio";
if (smsProvider.Equals("Log", StringComparison.OrdinalIgnoreCase))
{
    // Verification codes would end up in the logs: refuse to run that way outside development.
    if (!isDevelopment) throw new InvalidOperationException("Sms:Provider=Log is only allowed when AZURE_FUNCTIONS_ENVIRONMENT=Development.");
    builder.Services.AddScoped<ISmsSender, LogSmsSender>();
}
else
{
    builder.Services.AddScoped<ISmsSender, TwilioSmsSender>();
}

var host = builder.Build();

// ---- schema
if (config.GetValue("Database:AutoMigrate", true))
{
    using var scope = host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<BaytakDbContext>();
    if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)) db.Database.EnsureCreated();
    else db.Database.Migrate();
}

host.Run();
