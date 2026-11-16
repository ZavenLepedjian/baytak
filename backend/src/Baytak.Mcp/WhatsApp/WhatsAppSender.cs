using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baytak.Mcp.WhatsApp;

/// <summary>A reply button under a message. WhatsApp shows at most three, with titles of at most 20 characters.</summary>
public sealed record ReplyButton(string Id, string Title);

/// <summary>Sends WhatsApp messages. Sending never throws: a message that could not be sent is logged and reported as false.</summary>
public interface IWhatsAppSender
{
    Task<bool> SendTextAsync(string to, string text, CancellationToken ct);

    /// <summary>
    /// A message with reply buttons. WhatsApp allows 1024 characters beside buttons, so a longer text is sent first as an
    /// ordinary message and the buttons follow under <paramref name="shortText"/>.
    /// </summary>
    Task<bool> SendButtonsAsync(string to, string text, string shortText, IReadOnlyList<ReplyButton> buttons, CancellationToken ct);

    /// <summary>Marks the person's message as read and shows "typing" until the reply arrives (or 25 seconds pass).</summary>
    Task MarkReadAsync(string messageId, CancellationToken ct);
}

/// <summary>Meta's WhatsApp Cloud API: POST https://graph.facebook.com/{version}/{phone number id}/messages.</summary>
public sealed class GraphWhatsAppSender : IWhatsAppSender
{
    public const string HttpClientName = "whatsapp-graph";
    public const int MaxText = 4096;
    public const int MaxButtonText = 1024;

    private readonly IHttpClientFactory _http;
    private readonly WhatsAppOptions _options;
    private readonly ILogger<GraphWhatsAppSender> _logger;

    public GraphWhatsAppSender(IHttpClientFactory http, IOptions<WhatsAppOptions> options, ILogger<GraphWhatsAppSender> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> SendTextAsync(string to, string text, CancellationToken ct)
    {
        var sent = true;
        foreach (var part in Parts(text, MaxText))
        {
            sent &= await PostAsync(new JsonObject
            {
                ["messaging_product"] = "whatsapp",
                ["recipient_type"] = "individual",
                ["to"] = to,
                ["type"] = "text",
                ["text"] = new JsonObject { ["preview_url"] = false, ["body"] = part },
            }, ct);
        }
        return sent;
    }

    public async Task<bool> SendButtonsAsync(string to, string text, string shortText, IReadOnlyList<ReplyButton> buttons, CancellationToken ct)
    {
        var body = text;
        if (text.Length > MaxButtonText)
        {
            if (!await SendTextAsync(to, text, ct)) return false;
            body = shortText;
        }

        return await PostAsync(new JsonObject
        {
            ["messaging_product"] = "whatsapp",
            ["recipient_type"] = "individual",
            ["to"] = to,
            ["type"] = "interactive",
            ["interactive"] = new JsonObject
            {
                ["type"] = "button",
                ["body"] = new JsonObject { ["text"] = body },
                ["action"] = new JsonObject
                {
                    ["buttons"] = new JsonArray(buttons.Take(3).Select(b => (JsonNode?)new JsonObject
                    {
                        ["type"] = "reply",
                        ["reply"] = new JsonObject { ["id"] = b.Id, ["title"] = b.Title.Length > 20 ? b.Title[..20] : b.Title },
                    }).ToArray()),
                },
            },
        }, ct);
    }

    public Task MarkReadAsync(string messageId, CancellationToken ct) => PostAsync(new JsonObject
    {
        ["messaging_product"] = "whatsapp",
        ["status"] = "read",
        ["message_id"] = messageId,
        ["typing_indicator"] = new JsonObject { ["type"] = "text" },
    }, ct);

    private async Task<bool> PostAsync(JsonObject body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://graph.facebook.com/{_options.GraphVersion}/{_options.PhoneNumberId}/messages");
        request.Headers.Authorization = new("Bearer", _options.AccessToken);
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        try
        {
            using var response = await _http.CreateClient(HttpClientName).SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return true;
            // Meta's error code says why (131047: outside the 24-hour window, 131026: not on WhatsApp, ...). Never the message.
            _logger.LogWarning("WhatsApp refused a message: {Status} code {Code}", (int)response.StatusCode, ErrorCode(await response.Content.ReadAsStringAsync(ct)));
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            _logger.LogWarning("WhatsApp could not be reached: {Reason}", ex.GetType().Name);
            return false;
        }
    }

    private static string ErrorCode(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("code", out var code) ? code.GetRawText() : "?";
        }
        catch (JsonException)
        {
            return "?";
        }
    }

    /// <summary>Cuts a long text into messages, at a line break when there is one in the second half of a part.</summary>
    public static IEnumerable<string> Parts(string text, int max)
    {
        var rest = text.Trim();
        while (rest.Length > max)
        {
            var cut = rest.LastIndexOf('\n', max - 1, max / 2);
            if (cut <= 0) cut = max;
            yield return rest[..cut].TrimEnd();
            rest = rest[cut..].TrimStart();
        }
        if (rest.Length > 0) yield return rest;
    }
}
