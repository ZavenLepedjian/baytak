namespace Baytak.Mcp.WhatsApp;

/// <summary>Settings for the WhatsApp assistant (configuration section "WhatsApp", env vars WhatsApp__*).</summary>
public sealed class WhatsAppOptions
{
    /// <summary>The token typed into Meta's webhook form. Meta sends it back once, to prove it reached this app.</summary>
    public string VerifyToken { get; set; } = string.Empty;

    /// <summary>The Meta app's secret. Meta signs every webhook body with it (X-Hub-Signature-256).</summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>A system-user access token allowed to send messages for the business number.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>The id of the business phone number in Meta (not the number itself). Messages for any other number are ignored.</summary>
    public string PhoneNumberId { get; set; } = string.Empty;

    public string GraphVersion { get; set; } = "v27.0";

    /// <summary>OpenAI's API. Only changed to reach an OpenAI-compatible service.</summary>
    public string OpenAiBaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>The model that reads what people write and picks the tools.</summary>
    public string OpenAiModel { get; set; } = "gpt-6-luna";

    /// <summary>An OpenAI API key (a project key that may use that model).</summary>
    public string OpenAiKey { get; set; } = string.Empty;

    /// <summary>The most one reply may cost in output tokens (reasoning included, for models that reason).</summary>
    public int MaxReplyTokens { get; set; } = 2000;

    /// <summary>
    /// Sent as reasoning.effort. "none" (the default) answers fastest and costs least; the person confirms every change with
    /// exact facts in front of them, so a misread request is caught. Try "low" or "medium" for harder requests. Empty: not
    /// sent (for a model that does not reason and rejects the field).
    /// </summary>
    public string ReasoningEffort { get; set; } = "none";

    /// <summary>The key shared with the API (its Service__Key), which lets this app sign in the person who wrote.</summary>
    public string ApiServiceKey { get; set; } = string.Empty;

    /// <summary>Where someone without an account is sent to get the app.</summary>
    public string AppUrl { get; set; } = "https://baytakapi8803.z16.web.core.windows.net";

    /// <summary>Limits per phone number, so one person (or a stolen number) cannot run up the model and message bills.</summary>
    public int MessagesPerHour { get; set; } = 40;

    public int MessagesPerDay { get; set; } = 200;

    /// <summary>A conversation is forgotten after this long without a message.</summary>
    public int HistoryMinutes { get; set; } = 120;

    /// <summary>How long a Confirm button stays valid.</summary>
    public int ConfirmMinutes { get; set; } = 10;

    /// <summary>The stored (encrypted) conversation of someone who stopped writing is deleted after this many days.</summary>
    public int KeepDays { get; set; } = 2;

    public bool Enabled =>
        VerifyToken.Length > 0 && AppSecret.Length > 0 && AccessToken.Length > 0 && PhoneNumberId.Length > 0 &&
        OpenAiBaseUrl.Length > 0 && OpenAiModel.Length > 0 && OpenAiKey.Length > 0 && ApiServiceKey.Length > 0;
}
