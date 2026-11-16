using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baytak.Mcp.WhatsApp;

/// <summary>A tool the model asked for: its name, and its arguments as the JSON text the model wrote.</summary>
public sealed record ToolCall(string Id, string Name, string Arguments);

/// <summary>One answer of the model: text for the person, or tools to run first. Refused: the content filter stopped it.</summary>
public sealed record ModelReply(string? Text, IReadOnlyList<ToolCall> ToolCalls, bool Refused = false);

/// <summary>The model could not be reached or answered with an error.</summary>
public sealed class ChatModelException(string message) : Exception(message);

/// <summary>The language model behind the assistant. It only ever proposes: the app decides what is run.</summary>
public interface IChatModel
{
    /// <param name="messages">The conversation as a list of system, user, assistant (with tool_calls) and tool messages.</param>
    /// <param name="tools">The functions the model may ask for, as {"type":"function","function":{name,description,parameters}}.</param>
    /// <param name="user">A stable, anonymous id of the person writing (a keyed hash of their number), sent to the model's provider so
    /// one abusive person can be told apart from everyone else instead of the whole account being flagged.</param>
    Task<ModelReply> CompleteAsync(JsonArray messages, JsonArray tools, string user, CancellationToken ct);
}

/// <summary>
/// OpenAI's Responses API: POST https://api.openai.com/v1/responses, the key as a bearer token and the model by name.
/// No SDK: the request is small and its shape is stable.
///
/// The app keeps the whole conversation itself, so every call is stateless ("store": false): OpenAI keeps no conversation
/// for later calls, and no response id has to be remembered. The conversation the bot builds is a plain list of messages;
/// <see cref="ToInput"/> turns it into the Responses format.
/// </summary>
public sealed class OpenAiResponsesModel : IChatModel
{
    public const string HttpClientName = "openai";

    private readonly IHttpClientFactory _http;
    private readonly WhatsAppOptions _options;
    private readonly ILogger<OpenAiResponsesModel> _logger;

    public OpenAiResponsesModel(IHttpClientFactory http, IOptions<WhatsAppOptions> options, ILogger<OpenAiResponsesModel> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>The Responses URL for a base URL given with or without its /v1 (https://api.openai.com or https://api.openai.com/v1).</summary>
    public static Uri ResponsesUrl(string baseUrl)
    {
        var root = baseUrl.Trim().TrimEnd('/');
        if (!root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) root += "/v1";
        return new Uri(root + "/responses");
    }

    /// <summary>
    /// The conversation in Responses format: the system message becomes the instructions; a tool the model asked for becomes a
    /// function_call item and its answer a function_call_output item, joined by the call id. Items are sent without ids of their
    /// own, so none of them points at something the service would have to remember.
    /// </summary>
    public static (string Instructions, JsonArray Input) ToInput(JsonArray messages)
    {
        var instructions = new List<string>();
        var input = new JsonArray();
        foreach (var node in messages)
        {
            if (node is not JsonObject message) continue;
            var content = message["content"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
            switch (message["role"]?.GetValue<string>())
            {
                case "system":
                    if (!string.IsNullOrEmpty(content)) instructions.Add(content);
                    break;
                case "user":
                    input.Add(new JsonObject { ["role"] = "user", ["content"] = content ?? "" });
                    break;
                case "assistant":
                    if (!string.IsNullOrEmpty(content)) input.Add(new JsonObject { ["role"] = "assistant", ["content"] = content });
                    if (message["tool_calls"] is JsonArray calls)
                    {
                        foreach (var call in calls)
                        {
                            input.Add(new JsonObject
                            {
                                ["type"] = "function_call",
                                ["call_id"] = call?["id"]?.GetValue<string>(),
                                ["name"] = call?["function"]?["name"]?.GetValue<string>(),
                                ["arguments"] = call?["function"]?["arguments"]?.GetValue<string>() ?? "{}",
                            });
                        }
                    }
                    break;
                case "tool":
                    input.Add(new JsonObject { ["type"] = "function_call_output", ["call_id"] = message["tool_call_id"]?.GetValue<string>(), ["output"] = content ?? "" });
                    break;
            }
        }
        return (string.Join("\n\n", instructions), input);
    }

    /// <summary>
    /// The tools in Responses format (name and description beside the type, not under "function"). "strict": false is
    /// stated because the Responses API otherwise defaults it to true, and strict mode needs every property required and no
    /// open objects, which these tool schemas (optional fields, amounts by field name) are not.
    /// </summary>
    public static JsonArray ToFunctions(JsonArray tools) => new(tools.Select(tool => (JsonNode?)new JsonObject
    {
        ["type"] = "function",
        ["name"] = tool?["function"]?["name"]?.GetValue<string>(),
        ["description"] = tool?["function"]?["description"]?.GetValue<string>(),
        ["parameters"] = tool?["function"]?["parameters"]?.DeepClone(),
        ["strict"] = false,
    }).ToArray());

    public async Task<ModelReply> CompleteAsync(JsonArray messages, JsonArray tools, string user, CancellationToken ct)
    {
        var (instructions, input) = ToInput(messages);
        var body = new JsonObject
        {
            ["model"] = _options.OpenAiModel,
            ["input"] = input,
            ["max_output_tokens"] = _options.MaxReplyTokens,
            ["store"] = false,
            ["safety_identifier"] = user,
        };
        if (instructions.Length > 0) body["instructions"] = instructions;
        if (!string.IsNullOrWhiteSpace(_options.ReasoningEffort)) body["reasoning"] = new JsonObject { ["effort"] = _options.ReasoningEffort.Trim() };
        if (tools.Count > 0)
        {
            body["tools"] = ToFunctions(tools);
            body["tool_choice"] = "auto";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, ResponsesUrl(_options.OpenAiBaseUrl));
        request.Headers.Authorization = new("Bearer", _options.OpenAiKey);
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        string text;
        int status;
        try
        {
            using var response = await _http.CreateClient(HttpClientName).SendAsync(request, ct);
            status = (int)response.StatusCode;
            text = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw new ChatModelException("The model could not be reached.");
        }

        if (status is < 200 or >= 300)
        {
            var code = ErrorCode(text);
            // A prompt the provider's safety checks refuse comes back as 400 with one of these codes: an answer, not an outage.
            if (status == 400 && code is "content_filter" or "invalid_prompt") return new ModelReply(null, [], Refused: true);
            _logger.LogWarning("OpenAI answered {Status} ({Code})", status, code);
            throw new ChatModelException($"The model answered {status}.");
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            return Read(doc.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            _logger.LogWarning("OpenAI sent a reply that could not be read");
            throw new ChatModelException("The model's reply could not be read.");
        }
    }

    /// <summary>The text and the function calls of a response. Reasoning items are left out: only what the person or the app needs is read.</summary>
    private ModelReply Read(JsonElement root)
    {
        // A response can be accepted (200) and still have failed.
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            var code = error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            if (code is "content_filter" or "invalid_prompt") return new ModelReply(null, [], Refused: true);
            _logger.LogWarning("OpenAI response failed ({Code})", code);
            throw new ChatModelException("The model's response failed.");
        }

        var calls = new List<ToolCall>();
        var texts = new List<string>();
        var refusal = false;
        foreach (var item in root.GetProperty("output").EnumerateArray())
        {
            switch (Text(item, "type"))
            {
                case "function_call":
                    var id = Text(item, "call_id");
                    calls.Add(new ToolCall(id.Length > 0 ? id : Text(item, "id"), Text(item, "name"), Text(item, "arguments") is { Length: > 0 } a ? a : "{}"));
                    break;
                case "message":
                    if (!item.TryGetProperty("content", out var parts) || parts.ValueKind != JsonValueKind.Array) break;
                    foreach (var part in parts.EnumerateArray())
                    {
                        var kind = Text(part, "type");
                        if (kind == "output_text") texts.Add(Text(part, "text"));
                        else if (kind == "refusal") refusal = true;
                    }
                    break;
            }
        }

        var answer = string.Concat(texts);
        var reason = root.TryGetProperty("incomplete_details", out var details) && details.ValueKind == JsonValueKind.Object ? Text(details, "reason") : "";
        var nothing = calls.Count == 0 && string.IsNullOrWhiteSpace(answer);

        if (nothing && (refusal || reason == "content_filter")) return new ModelReply(null, [], Refused: true);
        // Reasoning and the answer share the token budget: a reply cut off before any answer is a failure, not an empty answer.
        if (nothing && reason == "max_output_tokens") throw new ChatModelException("The model ran out of room before it answered.");
        return new ModelReply(string.IsNullOrWhiteSpace(answer) ? null : answer, calls);
    }

    private static string Text(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string? ErrorCode(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object &&
                   error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String
                ? code.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
