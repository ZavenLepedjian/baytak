using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Data.Tables;
using Baytak.Mcp.Security;

namespace Baytak.Mcp.WhatsApp;

/// <summary>One thing said in a conversation, by the person ("user") or the assistant.</summary>
public sealed class Turn
{
    public string Role { get; set; } = "user";
    public string Text { get; set; } = string.Empty;
}

/// <summary>A change the person was asked to confirm. Nothing happens until they do, and only what is stored here is carried out.</summary>
public sealed class PendingAction
{
    public string Id { get; set; } = string.Empty;
    public string Tool { get; set; } = string.Empty;

    /// <summary>The tool's arguments as JSON, exactly as they were previewed.</summary>
    public string Arguments { get; set; } = "{}";

    /// <summary>Unix seconds after which the Confirm button no longer works.</summary>
    public long Expires { get; set; }
}

/// <summary>What the assistant remembers about one phone number between messages.</summary>
public sealed class ChatState
{
    public List<Turn> History { get; set; } = [];
    public PendingAction? Pending { get; set; }

    /// <summary>"en" or "ar": the language of the person's last written message, used for buttons and fixed texts.</summary>
    public string Language { get; set; } = "en";

    public long LastSeen { get; set; }
    public long HourStart { get; set; }
    public int HourCount { get; set; }
    public long DayStart { get; set; }
    public int DayCount { get; set; }

    /// <summary>When a number without an account was last told how to get one, so it is not told with every message.</summary>
    public long IntroducedAt { get; set; }

    public long LimitNoticeAt { get; set; }
}

/// <summary>
/// The assistant's short memory, one record per phone number. Records are keyed by a fingerprint of the number, never the
/// number, and their content is sealed, so the storage account holds nothing readable.
/// </summary>
public interface IChatStore
{
    Task<ChatState> LoadAsync(string key, CancellationToken ct);

    Task SaveAsync(string key, ChatState state, CancellationToken ct);

    /// <summary>Deletes the records of people who have not written since <paramref name="before"/>. Returns how many.</summary>
    Task<int> PurgeAsync(DateTimeOffset before, CancellationToken ct);
}

/// <summary>Azure Table Storage, in the app's own storage account.</summary>
public sealed class TableChatStore : IChatStore
{
    public const string TableName = "whatsappchats";
    public const string Purpose = "wa-chat";

    /// <summary>A Table Storage text value holds 32K characters; the assistant trims the conversation to stay well under.</summary>
    public const int MaxSealedLength = 30_000;

    private const string Partition = "chat";

    private readonly TableClient _table;
    private readonly Sealer _sealer;
    private int _created;

    public TableChatStore(TableServiceClient service, Sealer sealer)
    {
        _table = service.GetTableClient(TableName);
        _sealer = sealer;
    }

    public async Task<ChatState> LoadAsync(string key, CancellationToken ct)
    {
        await EnsureTableAsync(ct);
        var row = await _table.GetEntityIfExistsAsync<TableEntity>(Partition, key, cancellationToken: ct);
        // Unreadable (the secret key was rotated, or the row was tampered with): start again from nothing.
        return row.HasValue ? _sealer.Open<ChatState>(Purpose, row.Value!.GetString("State"), MaxSealedLength) ?? new ChatState() : new ChatState();
    }

    public async Task SaveAsync(string key, ChatState state, CancellationToken ct)
    {
        await EnsureTableAsync(ct);
        var entity = new TableEntity(Partition, key)
        {
            ["State"] = _sealer.Seal(Purpose, state),
            ["SeenAt"] = DateTimeOffset.FromUnixTimeSeconds(state.LastSeen),
        };
        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
    }

    public async Task<int> PurgeAsync(DateTimeOffset before, CancellationToken ct)
    {
        await EnsureTableAsync(ct);
        var removed = 0;
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {Partition} and SeenAt lt {before}");
        await foreach (var row in _table.QueryAsync<TableEntity>(filter, select: ["PartitionKey", "RowKey"], cancellationToken: ct))
        {
            await _table.DeleteEntityAsync(row.PartitionKey, row.RowKey, cancellationToken: ct);
            removed++;
        }
        return removed;
    }

    private async Task EnsureTableAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _created) == 1) return;
        await _table.CreateIfNotExistsAsync(ct);
        Volatile.Write(ref _created, 1);
    }
}

/// <summary>In-process store for local development and tests.</summary>
public sealed class MemoryChatStore : IChatStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, string> _rows = new();

    public Task<ChatState> LoadAsync(string key, CancellationToken ct) =>
        Task.FromResult(_rows.TryGetValue(key, out var json) ? JsonSerializer.Deserialize<ChatState>(json, Json)! : new ChatState());

    public Task SaveAsync(string key, ChatState state, CancellationToken ct)
    {
        _rows[key] = JsonSerializer.Serialize(state, Json);
        return Task.CompletedTask;
    }

    public Task<int> PurgeAsync(DateTimeOffset before, CancellationToken ct)
    {
        var removed = 0;
        foreach (var (key, json) in _rows)
        {
            if (JsonSerializer.Deserialize<ChatState>(json, Json)!.LastSeen < before.ToUnixTimeSeconds() && _rows.TryRemove(key, out _)) removed++;
        }
        return Task.FromResult(removed);
    }

    public int Count => _rows.Count;
}
