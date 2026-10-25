using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baytak.Api.Services;

public sealed class PhotoOptions
{
    /// <summary>Storage account for the photos. Empty means the Functions storage account (AzureWebJobsStorage).</summary>
    public string ConnectionString { get; set; } = string.Empty;
    /// <summary>A private container (no public access); created on first use.</summary>
    public string Container { get; set; } = "receipt-photos";
    /// <summary>The app shrinks photos to about 300 KB; this is the ceiling the server accepts.</summary>
    public int MaxBytes { get; set; } = 4 * 1024 * 1024;
    public int MaxPerReceipt { get; set; } = 5;
}

/// <summary>
/// Where receipt photos are kept. An upload is tagged "pending" until a receipt claims it, so a lifecycle rule on the
/// storage account can delete uploads that were never attached (see backend/deploy/setup-photo-storage.ps1).
/// </summary>
public interface IReceiptPhotoStore
{
    Task SaveAsync(string name, Stream content, CancellationToken ct);
    Task<bool> ExistsAsync(string name, CancellationToken ct);
    /// <summary>Tags the blob as belonging to a receipt, so it is no longer cleaned up.</summary>
    Task MarkAttachedAsync(string name, CancellationToken ct);
    /// <summary>The blob's bytes, or null when it does not exist.</summary>
    Task<Stream?> OpenReadAsync(string name, CancellationToken ct);
    Task DeleteAsync(string name, CancellationToken ct);
}

public sealed class BlobReceiptPhotoStore : IReceiptPhotoStore
{
    public const string TagName = "status";
    public const string Pending = "pending";
    public const string Attached = "attached";

    private readonly BlobContainerClient _container;
    private readonly ILogger<BlobReceiptPhotoStore> _logger;
    private readonly SemaphoreSlim _ready = new(1, 1);
    private bool _created;

    public BlobReceiptPhotoStore(IOptions<PhotoOptions> options, string connectionString, ILogger<BlobReceiptPhotoStore> logger)
    {
        _container = new BlobContainerClient(connectionString, options.Value.Container);
        _logger = logger;
    }

    private async Task EnsureContainerAsync(CancellationToken ct)
    {
        if (_created) return;
        await _ready.WaitAsync(ct);
        try
        {
            if (_created) return;
            await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
            _created = true;
        }
        finally
        {
            _ready.Release();
        }
    }

    public async Task SaveAsync(string name, Stream content, CancellationToken ct)
    {
        await EnsureContainerAsync(ct);
        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "image/jpeg", CacheControl = "private, max-age=300" },
            Tags = new Dictionary<string, string> { [TagName] = Pending },
        };
        await _container.GetBlobClient(name).UploadAsync(content, options, ct);
    }

    public async Task<bool> ExistsAsync(string name, CancellationToken ct)
    {
        await EnsureContainerAsync(ct);
        return (await _container.GetBlobClient(name).ExistsAsync(ct)).Value;
    }

    public async Task MarkAttachedAsync(string name, CancellationToken ct)
    {
        await _container.GetBlobClient(name).SetTagsAsync(new Dictionary<string, string> { [TagName] = Attached }, cancellationToken: ct);
    }

    public async Task<Stream?> OpenReadAsync(string name, CancellationToken ct)
    {
        await EnsureContainerAsync(ct);
        try
        {
            return await _container.GetBlobClient(name).OpenReadAsync(cancellationToken: ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Receipt photo {Name} is recorded but missing from storage", name);
            return null;
        }
    }

    public async Task DeleteAsync(string name, CancellationToken ct)
    {
        await EnsureContainerAsync(ct);
        await _container.GetBlobClient(name).DeleteIfExistsAsync(cancellationToken: ct);
    }
}

/// <summary>Used when no storage account is configured: photos are refused with a clear message instead of failing later.</summary>
public sealed class UnavailableReceiptPhotoStore : IReceiptPhotoStore
{
    private static InvalidOperationException Off() => new("Photo storage is not configured.");
    public Task SaveAsync(string name, Stream content, CancellationToken ct) => throw Off();
    public Task<bool> ExistsAsync(string name, CancellationToken ct) => throw Off();
    public Task MarkAttachedAsync(string name, CancellationToken ct) => throw Off();
    public Task<Stream?> OpenReadAsync(string name, CancellationToken ct) => throw Off();
    public Task DeleteAsync(string name, CancellationToken ct) => throw Off();
}
