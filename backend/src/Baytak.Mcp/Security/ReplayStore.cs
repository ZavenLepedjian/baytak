using System.Collections.Concurrent;
using Azure;
using Azure.Data.Tables;

namespace Baytak.Mcp.Security;

/// <summary>
/// The only state the app keeps: which authorization codes and refresh tokens have already been used, and which refresh-token
/// families were revoked after a reuse. That is what makes codes single-use and refresh tokens rotate safely (OAuth 2.1).
/// </summary>
public interface IReplayStore
{
    /// <summary>Records the first use of an id. Returns false when it was already used.</summary>
    Task<bool> TryUseAsync(string kind, string id, DateTimeOffset expiresAt, CancellationToken ct);

    Task<bool> IsRevokedAsync(string family, CancellationToken ct);

    Task RevokeAsync(string family, DateTimeOffset expiresAt, CancellationToken ct);
}

/// <summary>Azure Table Storage: one small row per used code or refresh token, in the app's own storage account.</summary>
public sealed class TableReplayStore : IReplayStore
{
    public const string TableName = "mcpgrants";
    private const string RevokedPartition = "revoked";

    private readonly TableClient _table;
    private int _created;

    public TableReplayStore(TableServiceClient service) => _table = service.GetTableClient(TableName);

    public async Task<bool> TryUseAsync(string kind, string id, DateTimeOffset expiresAt, CancellationToken ct)
    {
        await EnsureTableAsync(ct);
        try
        {
            await _table.AddEntityAsync(new TableEntity(kind, id) { ["ExpiresAt"] = expiresAt }, ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            return false;
        }
    }

    public async Task<bool> IsRevokedAsync(string family, CancellationToken ct)
    {
        await EnsureTableAsync(ct);
        var row = await _table.GetEntityIfExistsAsync<TableEntity>(RevokedPartition, family, cancellationToken: ct);
        return row.HasValue;
    }

    public async Task RevokeAsync(string family, DateTimeOffset expiresAt, CancellationToken ct)
    {
        await EnsureTableAsync(ct);
        await _table.UpsertEntityAsync(new TableEntity(RevokedPartition, family) { ["ExpiresAt"] = expiresAt }, cancellationToken: ct);
    }

    private async Task EnsureTableAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _created) == 1) return;
        await _table.CreateIfNotExistsAsync(ct);
        Volatile.Write(ref _created, 1);
    }
}

/// <summary>In-process store for local development and tests. Not shared between instances, so never used in production.</summary>
public sealed class MemoryReplayStore : IReplayStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _used = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _revoked = new();

    public Task<bool> TryUseAsync(string kind, string id, DateTimeOffset expiresAt, CancellationToken ct) =>
        Task.FromResult(_used.TryAdd(kind + ":" + id, expiresAt));

    public Task<bool> IsRevokedAsync(string family, CancellationToken ct) => Task.FromResult(_revoked.ContainsKey(family));

    public Task RevokeAsync(string family, DateTimeOffset expiresAt, CancellationToken ct)
    {
        _revoked[family] = expiresAt;
        return Task.CompletedTask;
    }
}
