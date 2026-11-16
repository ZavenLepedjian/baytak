using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Baytak.Mcp.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Baytak.Mcp.Tests;

/// <summary>The two clients that leave the app: OpenAI and Meta's WhatsApp Cloud API. What they send, and what they make of the answers.</summary>
public class WhatsAppClientTests
{
    private static readonly WhatsAppOptions Options = new()
    {
        AccessToken = "wa-access-token",
        PhoneNumberId = "106540352242922",
        GraphVersion = "v27.0",
        OpenAiBaseUrl = "https://api.openai.com/v1",
        OpenAiModel = "gpt-6-luna",
        OpenAiKey = "ai-key",
        MaxReplyTokens = 900,
    };

    private static GraphWhatsAppSender Sender(Wire wire) => new(wire, Microsoft.Extensions.Options.Options.Create(Options), NullLogger<GraphWhatsAppSender>.Instance);

    /// <summary>Answers every request from a script and keeps what was asked.</summary>
    private sealed class Wire : HttpMessageHandler, IHttpClientFactory
    {
        public List<(HttpMethod Method, string Url, string? Authorization, string? ApiKey, JsonElement Body)> Requests { get; } = new();
        public Queue<(HttpStatusCode Status, string Body)> Answers { get; } = new();
        public bool Unreachable { get; set; }

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Unreachable) throw new HttpRequestException("No connection could be made");
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement;
            Requests.Add((request.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("api-key", out var keys) ? keys.Single() : null, body));
            var (status, answer) = Answers.Count > 0 ? Answers.Dequeue() : (HttpStatusCode.OK, "{}");
            return new HttpResponseMessage(status) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
        }
    }

    private static OpenAiResponsesModel Model(Wire wire) => new(wire, Microsoft.Extensions.Options.Options.Create(Options), NullLogger<OpenAiResponsesModel>.Instance);

    private static JsonArray Conversation() => new(new JsonObject { ["role"] = "system", ["content"] = "rules" }, new JsonObject { ["role"] = "user", ["content"] = "Do I owe anything?" });

    private static JsonArray Functions() => new(new JsonObject
    {
        ["type"] = "function",
        ["function"] = new JsonObject { ["name"] = "get_my_balance", ["description"] = "What I owe", ["parameters"] = new JsonObject { ["type"] = "object" } },
    });

    // ==================================================================== OpenAI (Responses API)

    private static JsonArray Dialogue() => new(
        new JsonObject { ["role"] = "system", ["content"] = "rules" },
        new JsonObject { ["role"] = "user", ["content"] = "Do I owe anything?" },
        new JsonObject { ["role"] = "assistant", ["content"] = "Let me look." },
        new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = null,
            ["tool_calls"] = new JsonArray(new JsonObject
            {
                ["id"] = "call_1",
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = "get_my_balance", ["arguments"] = "{}" },
            }),
        },
        new JsonObject { ["role"] = "tool", ["tool_call_id"] = "call_1", ["content"] = "You owe 150.50 USD." },
        new JsonObject { ["role"] = "user", ["content"] = "and next month?" });

    [Fact]
    public async Task The_model_is_asked_through_the_responses_api_without_storing_anything()
    {
        var wire = new Wire();
        wire.Answers.Enqueue((HttpStatusCode.OK, """{"id":"resp_1","status":"completed","output":[{"type":"message","role":"assistant","content":[{"type":"output_text","text":"You owe 150.50 USD."}]}]}"""));

        var reply = await Model(wire).CompleteAsync(Conversation(), Functions(), "user-1", CancellationToken.None);

        var sent = Assert.Single(wire.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://api.openai.com/v1/responses", sent.Url);
        Assert.Equal("Bearer ai-key", sent.Authorization);
        Assert.Null(sent.ApiKey); // not Azure's header
        Assert.Equal("gpt-6-luna", sent.Body.GetProperty("model").GetString());
        Assert.Equal("user-1", sent.Body.GetProperty("safety_identifier").GetString());
        Assert.Equal(900, sent.Body.GetProperty("max_output_tokens").GetInt32());
        Assert.False(sent.Body.GetProperty("store").GetBoolean()); // the conversation is ours; nothing is kept at Microsoft for later calls
        Assert.Equal("none", sent.Body.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal("auto", sent.Body.GetProperty("tool_choice").GetString());
        Assert.False(sent.Body.TryGetProperty("temperature", out _));
        Assert.False(sent.Body.TryGetProperty("messages", out _)); // not the Chat Completions shape

        Assert.Equal("rules", sent.Body.GetProperty("instructions").GetString());
        var input = sent.Body.GetProperty("input").EnumerateArray().ToList();
        Assert.Equal("user", input[0].GetProperty("role").GetString());
        Assert.Equal("Do I owe anything?", input[0].GetProperty("content").GetString());

        // Function tools are flat, and not strict: the Responses API would otherwise default to strict, which these schemas cannot meet.
        var tool = sent.Body.GetProperty("tools")[0];
        Assert.Equal("function", tool.GetProperty("type").GetString());
        Assert.Equal("get_my_balance", tool.GetProperty("name").GetString());
        Assert.Equal("What I owe", tool.GetProperty("description").GetString());
        Assert.False(tool.GetProperty("strict").GetBoolean());
        Assert.Equal("object", tool.GetProperty("parameters").GetProperty("type").GetString());
        Assert.False(tool.TryGetProperty("function", out _));

        Assert.Equal("You owe 150.50 USD.", reply.Text);
        Assert.Empty(reply.ToolCalls);
        Assert.False(reply.Refused);
    }

    [Fact]
    public async Task A_conversation_with_tool_rounds_is_sent_as_function_call_items_joined_by_call_id()
    {
        var wire = new Wire();
        wire.Answers.Enqueue((HttpStatusCode.OK, """{"status":"completed","output":[{"type":"message","role":"assistant","content":[{"type":"output_text","text":"Nothing is due next month."}]}]}"""));

        await Model(wire).CompleteAsync(Dialogue(), Functions(), "user-1", CancellationToken.None);

        var input = wire.Requests[0].Body.GetProperty("input").EnumerateArray().ToList();
        Assert.Equal(5, input.Count); // the system message went to "instructions"
        Assert.Equal(("user", "Do I owe anything?"), (input[0].GetProperty("role").GetString(), input[0].GetProperty("content").GetString()));
        Assert.Equal(("assistant", "Let me look."), (input[1].GetProperty("role").GetString(), input[1].GetProperty("content").GetString()));

        Assert.Equal("function_call", input[2].GetProperty("type").GetString());
        Assert.Equal("call_1", input[2].GetProperty("call_id").GetString());
        Assert.Equal("get_my_balance", input[2].GetProperty("name").GetString());
        Assert.Equal("{}", input[2].GetProperty("arguments").GetString());
        Assert.False(input[2].TryGetProperty("id", out _)); // an item id would point at something the service no longer has

        Assert.Equal("function_call_output", input[3].GetProperty("type").GetString());
        Assert.Equal("call_1", input[3].GetProperty("call_id").GetString());
        Assert.Equal("You owe 150.50 USD.", input[3].GetProperty("output").GetString());
        Assert.Equal("and next month?", input[4].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Function_calls_are_read_and_reasoning_items_are_ignored()
    {
        var wire = new Wire();
        wire.Answers.Enqueue((HttpStatusCode.OK, """
            {"id":"resp_2","status":"completed","output":[
              {"type":"reasoning","id":"rs_1","summary":[]},
              {"type":"function_call","id":"fc_1","call_id":"call_1","name":"get_my_balance","arguments":"{}"},
              {"type":"function_call","id":"fc_2","call_id":"call_2","name":"list_my_receipts","arguments":"{\"status\":\"unpaid\"}"}]}
            """));

        var reply = await Model(wire).CompleteAsync(Conversation(), Functions(), "user-1", CancellationToken.None);

        Assert.Null(reply.Text);
        Assert.False(reply.Refused);
        Assert.Equal([("call_1", "get_my_balance", "{}"), ("call_2", "list_my_receipts", "{\"status\":\"unpaid\"}")], reply.ToolCalls.Select(c => (c.Id, c.Name, c.Arguments)));
    }

    [Theory]
    [InlineData("low", "low")]
    [InlineData(" medium ", "medium")]
    [InlineData("", null)]
    public async Task Reasoning_effort_can_be_changed_or_left_out_for_models_that_reject_it(string configured, string? sent)
    {
        var wire = new Wire();
        wire.Answers.Enqueue((HttpStatusCode.OK, """{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"Hello"}]}]}"""));
        var options = new WhatsAppOptions
        {
            OpenAiBaseUrl = Options.OpenAiBaseUrl, OpenAiModel = Options.OpenAiModel, OpenAiKey = Options.OpenAiKey, ReasoningEffort = configured,
        };

        await new OpenAiResponsesModel(wire, Microsoft.Extensions.Options.Options.Create(options), NullLogger<OpenAiResponsesModel>.Instance)
            .CompleteAsync(Conversation(), Functions(), "user-1", CancellationToken.None);

        var body = wire.Requests[0].Body;
        if (sent is null) Assert.False(body.TryGetProperty("reasoning", out _));
        else Assert.Equal(sent, body.GetProperty("reasoning").GetProperty("effort").GetString());
    }

    [Fact]
    public async Task No_tools_means_no_tool_fields()
    {
        var wire = new Wire();
        wire.Answers.Enqueue((HttpStatusCode.OK, """{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"Hello"}]}]}"""));

        await Model(wire).CompleteAsync(Conversation(), new JsonArray(), "user-1", CancellationToken.None);

        Assert.False(wire.Requests[0].Body.TryGetProperty("tools", out _));
        Assert.False(wire.Requests[0].Body.TryGetProperty("tool_choice", out _));
    }

    [Fact]
    public async Task The_content_filter_is_an_answer_and_other_errors_are_failures()
    {
        var wire = new Wire();
        wire.Answers.Enqueue((HttpStatusCode.BadRequest, """{"error":{"code":"content_filter","message":"The prompt was filtered"}}"""));
        Assert.True((await Model(wire).CompleteAsync(Conversation(), Functions(), "user-1", CancellationToken.None)).Refused);

        wire.Answers.Enqueue((HttpStatusCode.BadRequest, """{"error":{"code":"invalid_prompt","message":"Invalid prompt: your prompt was flagged as potentially violating our usage policy."}}"""));
        Assert.True((await Model(wire).CompleteAsync(Conversation(), Functions(), "user-1", CancellationToken.None)).Refused);

        wire.Answers.Enqueue((HttpStatusCode.OK, """{"status":"incomplete","incomplete_details":{"reason":"content_filter"},"output":[]}"""));
        Assert.True((await Model(wire).CompleteAsync(Conversation(), Functions(), "user-1", CancellationToken.None)).Refused);

        wire.Answers.Enqueue((HttpStatusCode.OK, """{"status":"completed","output":[{"type":"message","content":[{"type":"refusal","refusal":"I can't help with that."}]}]}"""));
        Assert.True((await Model(wire).CompleteAsync(Conversation(), Functions(), "user-1", CancellationToken.None)).Refused);

        wire.Answers.Enqueue((HttpStatusCode.OK, """{"status":"failed","error":{"code":"content_filter","message":"filtered"},"output":[]}"""));
        Assert.True((await Model(wire).CompleteAsync(Conversation(), Functions(), "user-1", CancellationToken.None)).Refused);

        // Used up the whole budget on reasoning: a failure, not an empty answer.
        wire.Answers.Enqueue((HttpStatusCode.OK, """{"status":"incomplete","incomplete_details":{"reason":"max_output_tokens"},"output":[{"type":"reasoning","summary":[]}]}"""));
        await Assert.ThrowsAsync<ChatModelException>(() => Model(wire).CompleteAsync(Conversation(), Functions(), "user-1", CancellationToken.None));

        wire.Answers.Enqueue((HttpStatusCode.OK, """{"status":"failed","error":{"code":"server_error","message":"x"},"output":[]}"""));
        await Assert.ThrowsAsync<ChatModelException>(() => Model(wire).CompleteAsync(Conversation(), Functions(), "user-1", CancellationToken.None));

        wire.Answers.Enqueue((HttpStatusCode.TooManyRequests, """{"error":{"code":"429","message":"Rate limit"}}"""));
        await Assert.ThrowsAsync<ChatModelException>(() => Model(wire).CompleteAsync(Conversation(), Functions(), "user-1", CancellationToken.None));

        wire.Answers.Enqueue((HttpStatusCode.OK, """{"unexpected":true}"""));
        await Assert.ThrowsAsync<ChatModelException>(() => Model(wire).CompleteAsync(Conversation(), Functions(), "user-1", CancellationToken.None));

        wire.Unreachable = true;
        await Assert.ThrowsAsync<ChatModelException>(() => Model(wire).CompleteAsync(Conversation(), Functions(), "user-1", CancellationToken.None));
    }

    // ==================================================================== WhatsApp Cloud API

    [Fact]
    public async Task A_text_goes_to_the_business_number_with_the_access_token()
    {
        var wire = new Wire();

        Assert.True(await Sender(wire).SendTextAsync("96170222222", "You owe 150.50 USD.", CancellationToken.None));

        var sent = Assert.Single(wire.Requests);
        Assert.Equal("https://graph.facebook.com/v27.0/106540352242922/messages", sent.Url);
        Assert.Equal("Bearer wa-access-token", sent.Authorization);
        Assert.Equal("whatsapp", sent.Body.GetProperty("messaging_product").GetString());
        Assert.Equal("96170222222", sent.Body.GetProperty("to").GetString());
        Assert.Equal("text", sent.Body.GetProperty("type").GetString());
        Assert.Equal("You owe 150.50 USD.", sent.Body.GetProperty("text").GetProperty("body").GetString());
        Assert.False(sent.Body.GetProperty("text").GetProperty("preview_url").GetBoolean());
    }

    [Fact]
    public async Task Buttons_carry_their_ids_and_a_long_text_is_sent_before_them()
    {
        var wire = new Wire();
        IReadOnlyList<ReplyButton> buttons = [new("ok:abc", "Confirm"), new("no:abc", "Cancel")];

        Assert.True(await Sender(wire).SendButtonsAsync("96170222222", "Mark apartment 3A as paid?", "Do you confirm?", buttons, CancellationToken.None));

        var interactive = Assert.Single(wire.Requests).Body.GetProperty("interactive");
        Assert.Equal("button", interactive.GetProperty("type").GetString());
        Assert.Equal("Mark apartment 3A as paid?", interactive.GetProperty("body").GetProperty("text").GetString());
        var replies = interactive.GetProperty("action").GetProperty("buttons").EnumerateArray().Select(b => b.GetProperty("reply")).ToList();
        Assert.Equal(["ok:abc", "no:abc"], replies.Select(r => r.GetProperty("id").GetString()));
        Assert.Equal(["Confirm", "Cancel"], replies.Select(r => r.GetProperty("title").GetString()));

        // WhatsApp allows 1024 characters beside buttons.
        wire.Requests.Clear();
        var longText = "Issue these 5 receipts?\n" + new string('x', 1500);
        Assert.True(await Sender(wire).SendButtonsAsync("96170222222", longText, "Do you confirm?", buttons, CancellationToken.None));
        Assert.Equal(2, wire.Requests.Count);
        Assert.Equal(longText, wire.Requests[0].Body.GetProperty("text").GetProperty("body").GetString());
        Assert.Equal("Do you confirm?", wire.Requests[1].Body.GetProperty("interactive").GetProperty("body").GetProperty("text").GetString());
    }

    [Fact]
    public async Task Reading_a_message_also_shows_typing()
    {
        var wire = new Wire();

        await Sender(wire).MarkReadAsync("wamid.abc", CancellationToken.None);

        var body = Assert.Single(wire.Requests).Body;
        Assert.Equal("read", body.GetProperty("status").GetString());
        Assert.Equal("wamid.abc", body.GetProperty("message_id").GetString());
        Assert.Equal("text", body.GetProperty("typing_indicator").GetProperty("type").GetString());
    }

    [Fact]
    public async Task A_message_whatsapp_refuses_is_reported_not_thrown()
    {
        var wire = new Wire();
        wire.Answers.Enqueue((HttpStatusCode.BadRequest, """{"error":{"message":"Re-engagement message","code":131047}}"""));
        Assert.False(await Sender(wire).SendTextAsync("96170222222", "hello", CancellationToken.None));

        wire.Unreachable = true;
        Assert.False(await Sender(wire).SendTextAsync("96170222222", "hello", CancellationToken.None));
    }
}
