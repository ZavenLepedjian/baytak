using Azure.Storage.Blobs;
using Baytak.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Baytak.Api.Tests;

/// <summary>
/// Runs the real Blob store against a real storage account, in a throw-away container that is deleted afterwards.
/// Set BAYTAK_TEST_BLOB to a storage connection string to run it; without one it does nothing.
/// </summary>
public class BlobPhotoStoreLiveTests
{
    private static readonly string? Connection = Environment.GetEnvironmentVariable("BAYTAK_TEST_BLOB");

    [Fact]
    public async Task A_photo_round_trips_and_its_pending_tag_flips_to_attached()
    {
        if (string.IsNullOrWhiteSpace(Connection)) return;

        var container = "selftest-" + Guid.NewGuid().ToString("N")[..12];
        var store = new BlobReceiptPhotoStore(Options.Create(new PhotoOptions { Container = container }), Connection, NullLogger<BlobReceiptPhotoStore>.Instance);
        var name = $"1/{Guid.NewGuid():N}.jpg";
        var bytes = SeedHelpers.Jpeg(size: 5000, fill: 3);
        try
        {
            Assert.False(await store.ExistsAsync(name, default));
            await store.SaveAsync(name, new MemoryStream(bytes), default);
            Assert.True(await store.ExistsAsync(name, default));

            var client = new BlobContainerClient(Connection, container).GetBlobClient(name);
            Assert.Equal("pending", (await client.GetTagsAsync()).Value.Tags["status"]);
            Assert.Equal("image/jpeg", (await client.GetPropertiesAsync()).Value.ContentType);

            await store.MarkAttachedAsync(name, default);
            Assert.Equal("attached", (await client.GetTagsAsync()).Value.Tags["status"]);

            await using (var read = await store.OpenReadAsync(name, default))
            {
                Assert.NotNull(read);
                using var copy = new MemoryStream();
                await read!.CopyToAsync(copy);
                Assert.Equal(bytes, copy.ToArray());
            }

            // The container is private: an anonymous request for the blob is refused.
            using var http = new HttpClient();
            Assert.False((await http.GetAsync(client.Uri)).IsSuccessStatusCode);

            await store.DeleteAsync(name, default);
            Assert.False(await store.ExistsAsync(name, default));
            Assert.Null(await store.OpenReadAsync(name, default));
        }
        finally
        {
            await new BlobContainerClient(Connection, container).DeleteIfExistsAsync();
        }
    }
}
