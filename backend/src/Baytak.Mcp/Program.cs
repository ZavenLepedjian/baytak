using Azure.Data.Tables;
using Azure.Identity;
using Baytak.Mcp;
using Baytak.Mcp.Api;
using Baytak.Mcp.Common;
using Baytak.Mcp.Mcp;
using Baytak.Mcp.OAuth;
using Baytak.Mcp.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

builder.Services.Configure<McpOptions>(config.GetSection("Mcp"));
var options = config.GetSection("Mcp").Get<McpOptions>() ?? new McpOptions();

// The public URL is the OAuth issuer and the token audience: it must be configured, and https outside development.
if (!Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var publicUrl))
    throw new InvalidOperationException("Set Mcp:PublicBaseUrl to this app's public URL, for example https://baytak-mcp-x.azurewebsites.net.");
if (publicUrl.Scheme != Uri.UriSchemeHttps && !isDevelopment)
    throw new InvalidOperationException("Mcp:PublicBaseUrl must use https outside development.");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Sealer>();
builder.Services.AddSingleton<ClientRegistry>();
builder.Services.AddSingleton<BaytakApi>();
builder.Services.AddSingleton<BaytakTools>();

builder.Services.AddHttpClient(BaytakApi.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddHttpClient(ClientRegistry.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(5))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

if (options.ReplayStore.Equals("Memory", StringComparison.OrdinalIgnoreCase))
{
    // Not shared between instances, so codes could be replayed across them: development only.
    if (!isDevelopment) throw new InvalidOperationException("Mcp:ReplayStore=Memory is only allowed when AZURE_FUNCTIONS_ENVIRONMENT=Development.");
    builder.Services.AddSingleton<IReplayStore, MemoryReplayStore>();
}
else
{
    builder.Services.AddSingleton(_ => CreateTableService(config));
    builder.Services.AddSingleton<IReplayStore, TableReplayStore>();
}

var host = builder.Build();

// Fail at start-up, not on the first sign-in, when the key is missing or malformed.
_ = host.Services.GetRequiredService<Sealer>();
_ = host.Services.GetRequiredService<BaytakApi>();

host.Run();

// The app's own storage account: a connection string (local, Azurite) or the managed identity (Flex Consumption).
static TableServiceClient CreateTableService(IConfiguration config)
{
    var connection = config["Mcp:StorageConnection"] ?? config["AzureWebJobsStorage"];
    if (!string.IsNullOrWhiteSpace(connection)) return new TableServiceClient(connection);

    var account = config["AzureWebJobsStorage:accountName"] ?? config["AzureWebJobsStorage__accountName"];
    if (!string.IsNullOrWhiteSpace(account))
    {
        var clientId = config["AzureWebJobsStorage:clientId"] ?? config["AzureWebJobsStorage__clientId"];
        var credential = string.IsNullOrWhiteSpace(clientId)
            ? new DefaultAzureCredential()
            : new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = clientId });
        return new TableServiceClient(new Uri($"https://{account}.table.core.windows.net"), credential);
    }

    throw new InvalidOperationException("No storage for the replay store: set AzureWebJobsStorage or Mcp:StorageConnection.");
}
