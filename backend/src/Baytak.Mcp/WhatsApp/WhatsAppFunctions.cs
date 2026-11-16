using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Baytak.Mcp.Common;
using Baytak.Mcp.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baytak.Mcp.WhatsApp;

/// <summary>The webhook's answer to Meta, and the messages it hands on to be answered.</summary>
public sealed class WebhookOutput
{
    [HttpResult]
    public IActionResult Result { get; set; } = new StatusCodeResult(StatusCodes.Status200OK);

    /// <summary>One sealed <see cref="Incoming"/> per message, put on the queue.</summary>
    [QueueOutput(WhatsAppFunctions.QueueName)]
    public string[] Messages { get; set; } = [];
}

/// <summary>
/// The WhatsApp side of the assistant (Meta's Cloud API, connected directly).
///   GET  /whatsapp/webhook   Meta's one-time check that the webhook is ours
///   POST /whatsapp/webhook   messages people send; checked, then queued and answered 200 at once
///   queue whatsapp-in        answers each message (the model and the tools can take several seconds)
///   timer, nightly           deletes the stored conversations of people who stopped writing
/// Meta retries a webhook that is slow or fails, so the POST does nothing slow and never repeats a message it has seen.
/// </summary>
public sealed class WhatsAppFunctions
{
    public const string QueueName = "whatsapp-in";
    public const string InboxPurpose = "wa-in";

    private const int MaxBodyBytes = 512 * 1024;
    private const int MaxText = 1500;
    private const int MaxSealedMessage = 20_000;

    /// <summary>A message Meta delivers this long after it was sent (after an outage) is no longer answered.</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(15);

    private readonly WhatsAppOptions _options;
    private readonly WhatsAppBot _bot;
    private readonly IChatStore _store;
    private readonly IReplayStore _replay;
    private readonly Sealer _sealer;
    private readonly TimeProvider _time;
    private readonly ILogger<WhatsAppFunctions> _logger;

    public WhatsAppFunctions(
        IOptions<WhatsAppOptions> options, WhatsAppBot bot, IChatStore store, IReplayStore replay, Sealer sealer, TimeProvider time,
        ILogger<WhatsAppFunctions> logger)
    {
        _options = options.Value;
        _bot = bot;
        _store = store;
        _replay = replay;
        _sealer = sealer;
        _time = time;
        _logger = logger;
    }

    [Function("WhatsAppVerify")]
    public IActionResult Verify([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "whatsapp/webhook")] HttpRequest req)
    {
        if (!_options.Enabled) return Http.Text(StatusCodes.Status404NotFound, "Not found");

        var token = req.Query["hub.verify_token"].ToString();
        var matches = CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(token)), SHA256.HashData(Encoding.UTF8.GetBytes(_options.VerifyToken)));
        return req.Query["hub.mode"] == "subscribe" && matches
            ? Http.Text(StatusCodes.Status200OK, req.Query["hub.challenge"].ToString())
            : Http.Text(StatusCodes.Status403Forbidden, "Forbidden");
    }

    [Function("WhatsAppWebhook")]
    public async Task<WebhookOutput> Receive(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "whatsapp/webhook")] HttpRequest req, CancellationToken ct)
    {
        if (!_options.Enabled) return new WebhookOutput { Result = Http.Text(StatusCodes.Status404NotFound, "Not found") };

        var body = await ReadBodyAsync(req, ct);
        if (body is null) return new WebhookOutput { Result = Http.Text(StatusCodes.Status413PayloadTooLarge, "Too large") };

        // Only Meta knows the app secret, so only Meta can sign a body. Checked over the bytes exactly as they arrived.
        if (!SignatureValid(body, req.Headers["X-Hub-Signature-256"].ToString(), _options.AppSecret))
        {
            _logger.LogWarning("WhatsApp webhook with a missing or wrong signature");
            return new WebhookOutput { Result = Http.Text(StatusCodes.Status401Unauthorized, "Unauthorized") };
        }

        List<Incoming> incoming;
        try
        {
            using var doc = JsonDocument.Parse(body);
            incoming = Read(doc.RootElement, _options.PhoneNumberId, _time.GetUtcNow());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            // Signed by Meta but not in a shape we know: accepted, so it is not sent again and again.
            _logger.LogWarning("WhatsApp webhook body could not be read");
            return new WebhookOutput();
        }

        var queued = new List<string>();
        foreach (var message in incoming)
        {
            // Meta delivers at least once: a message id is answered one time only.
            if (!await _replay.TryUseAsync("wamsg", message.MessageId, _time.GetUtcNow().AddDays(3), ct)) continue;
            queued.Add(_sealer.Seal(InboxPurpose, message));
        }
        return new WebhookOutput { Messages = queued.ToArray() };
    }

    [Function("WhatsAppProcess")]
    public async Task Process([QueueTrigger(QueueName)] string message, CancellationToken ct)
    {
        if (!_options.Enabled) return;
        var incoming = _sealer.Open<Incoming>(InboxPurpose, message, MaxSealedMessage);
        if (incoming is null)
        {
            _logger.LogWarning("A queued WhatsApp message could not be opened and was dropped");
            return;
        }

        try
        {
            await _bot.HandleAsync(incoming, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not thrown on: a retry would answer (or act) twice.
            _logger.LogError(ex, "A WhatsApp message was not answered");
        }
    }

    [Function("WhatsAppPurge")]
    public async Task Purge([TimerTrigger("0 30 2 * * *")] TimerInfo timer, CancellationToken ct)
    {
        var removed = await _store.PurgeAsync(_time.GetUtcNow().AddDays(-Math.Max(1, _options.KeepDays)), ct);
        if (removed > 0) _logger.LogInformation("Deleted {Count} old WhatsApp conversations", removed);
    }

    // ==================================================================== reading Meta's webhook

    public static bool SignatureValid(byte[] body, string header, string appSecret)
    {
        const string prefix = "sha256=";
        if (appSecret.Length == 0 || !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        byte[] given;
        try
        {
            given = Convert.FromHexString(header[prefix.Length..].Trim());
        }
        catch (FormatException)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(given, HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body));
    }

    /// <summary>
    /// The messages in one webhook call that are for our number and recent. Delivery receipts ("statuses") and anything for
    /// another number are left out.
    /// </summary>
    public static List<Incoming> Read(JsonElement root, string phoneNumberId, DateTimeOffset now)
    {
        var found = new List<Incoming>();
        if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array) return found;

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array) continue;
            foreach (var change in changes.EnumerateArray())
            {
                if (Text(change, "field") != "messages" || !change.TryGetProperty("value", out var value)) continue;
                if (!value.TryGetProperty("metadata", out var metadata) || Text(metadata, "phone_number_id") != phoneNumberId) continue;
                if (!value.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array) continue;

                foreach (var m in messages.EnumerateArray())
                {
                    var from = Text(m, "from");
                    var id = Text(m, "id");
                    if (id.Length == 0 || from.Length is < 6 or > 15 || !from.All(char.IsAsciiDigit)) continue;
                    if (long.TryParse(Text(m, "timestamp"), NumberStyles.None, CultureInfo.InvariantCulture, out var sent) &&
                        now - DateTimeOffset.FromUnixTimeSeconds(sent) > MaxAge) continue;

                    var type = Text(m, "type");
                    string? text = null, button = null;
                    if (type == "text" && m.TryGetProperty("text", out var t)) text = Cut(Text(t, "body"));
                    else if (type == "interactive" && m.TryGetProperty("interactive", out var i) && i.TryGetProperty("button_reply", out var reply)) button = Text(reply, "id");
                    found.Add(new Incoming(from, id, type, text, button is { Length: > 0 and <= 256 } ? button : null));
                }
            }
        }
        return found;
    }

    private static string Text(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string Cut(string text) => text.Length > MaxText ? text[..MaxText] : text;

    private static async Task<byte[]?> ReadBodyAsync(HttpRequest req, CancellationToken ct)
    {
        if (req.ContentLength is > MaxBodyBytes) return null;
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await req.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
