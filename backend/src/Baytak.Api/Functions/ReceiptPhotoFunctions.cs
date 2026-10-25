using Baytak.Api.Common;
using Baytak.Api.Data;
using Baytak.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baytak.Api.Functions;

public static class ReceiptPhotoNames
{
    /// <summary>Path inside the photos container. It starts with the building, so one building can never name another's photo.</summary>
    public static string BlobName(int buildingId, Guid photoId) => $"{buildingId}/{photoId:N}.jpg";
}

/// <summary>
/// Photos of the bill a receipt is based on.
///   POST receipt/photo       the administrator uploads one JPEG (the app shrinks it first); it waits, tagged pending, until
///                            receipt/add claims it
///   GET  receipt/photo/{id}  the image, for the building's administrator and the residents the receipt was issued to
/// </summary>
public sealed class ReceiptPhotoFunctions : ApiFunction
{
    private readonly BaytakDbContext _db;
    private readonly IReceiptPhotoStore _store;
    private readonly PhotoOptions _options;

    public ReceiptPhotoFunctions(IAccessGuard guard, BaytakDbContext db, IReceiptPhotoStore store, IOptions<PhotoOptions> options, ILogger<ReceiptPhotoFunctions> logger)
        : base(guard, logger)
    {
        _db = db;
        _store = store;
        _options = options.Value;
    }

    [Function("ReceiptPhotoUpload")]
    public Task<IActionResult> Upload(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "receipt/photo")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Admin, ct, async admin =>
        {
            var building = await _db.Buildings.AsNoTracking().FirstOrDefaultAsync(b => b.AdminUserId == admin.Id, ct);
            if (building is null) return Reply.BadRequest("Please set up your building first");

            if (req.ContentLength is { } declared && declared > _options.MaxBytes) return TooLarge();

            // Read at most the limit plus one byte: never trust the declared length alone.
            using var data = new MemoryStream();
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await req.Body.ReadAsync(buffer, ct)) > 0)
            {
                if (data.Length + read > _options.MaxBytes) return TooLarge();
                data.Write(buffer, 0, read);
            }

            if (!LooksLikeJpeg(data)) return Reply.BadRequest("Please choose a photo (JPEG).");

            var id = Guid.NewGuid();
            data.Position = 0;
            await _store.SaveAsync(ReceiptPhotoNames.BlobName(building.Id, id), data, ct);
            return Reply.Ok(new { photoId = id.ToString() });
        });

    [Function("ReceiptPhotoGet")]
    public Task<IActionResult> Get(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "receipt/photo/{id:guid}")] HttpRequest req, Guid id, CancellationToken ct) =>
        Run(req, AccessLevel.Signed, ct, async user =>
        {
            var photo = await _db.ReceiptPhotos.AsNoTracking()
                .Where(p => p.Id == id)
                .Select(p => new { p.BlobName, p.ReceiptId, AdminId = p.Receipt!.Building!.AdminUserId })
                .FirstOrDefaultAsync(ct);
            if (photo is null) return Reply.NotFound("Photo not found");

            // The building's administrator, or a resident of an approved apartment the receipt was issued to. Anyone else
            // gets the same answer as for a photo that does not exist.
            var allowed = photo.AdminId == user.Id
                || await _db.ReceiptAssignments.AnyAsync(a => a.ReceiptId == photo.ReceiptId && a.Apartment!.UserId == user.Id
                                                              && a.Apartment.Status == ApartmentStatuses.Approved, ct);
            if (!allowed) return Reply.NotFound("Photo not found");

            var content = await _store.OpenReadAsync(photo.BlobName, ct);
            if (content is null) return Reply.NotFound("Photo not found");

            req.HttpContext.Response.Headers.CacheControl = "private, max-age=300";
            return new FileStreamResult(content, "image/jpeg");
        });

    private static ContentResult TooLarge() => Reply.Text(StatusCodes.Status413PayloadTooLarge, "That photo is too large. Please choose a smaller one.");

    /// <summary>JPEG files start with FF D8 FF. The app re-encodes every photo, so anything else is not from the app.</summary>
    private static bool LooksLikeJpeg(MemoryStream data)
    {
        if (data.Length < 1024) return false;
        var head = data.GetBuffer();
        return head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF;
    }
}
