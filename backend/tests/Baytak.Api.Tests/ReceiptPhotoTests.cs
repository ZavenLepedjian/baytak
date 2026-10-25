using Baytak.Api.Functions;

namespace Baytak.Api.Tests;

public class ReceiptPhotoTests
{
    private static object ReceiptBody(string[]? photoIds = null, string appartmentIds = "") => new
    {
        currency = "USD", appartmentIds, photoIds,
        receipt = new { currency = "USD", date = "2025-03-01", customFields = new[] { new { fieldName = "Maintenance", type = "Amount", value = "100" } } },
    };

    private static async Task<string> UploadAsync(TestApp app, string admin, byte[]? data = null)
    {
        var response = await app.Call<ReceiptPhotoFunctions>((f, r, ct) => f.Upload(r, ct), bytes: data ?? SeedHelpers.Jpeg(), token: admin);
        Assert.True(response.Ok, response.Body);
        return response.Json.GetProperty("photoId").GetString()!;
    }

    [Fact]
    public async Task Only_administrators_with_a_building_can_upload_and_only_real_photos_are_kept()
    {
        using var app = new TestApp();
        var resident = await app.LoginAsync("70 000 009");
        Assert.Equal(403, (await app.Call<ReceiptPhotoFunctions>((f, r, ct) => f.Upload(r, ct), bytes: SeedHelpers.Jpeg(), token: resident)).Status);

        var admin = await app.LoginAsApprovedAdminAsync("03 111 111");
        var noBuilding = await app.Call<ReceiptPhotoFunctions>((f, r, ct) => f.Upload(r, ct), bytes: SeedHelpers.Jpeg(), token: admin);
        Assert.Equal(400, noBuilding.Status);

        await app.Call<BuildingFunctions>((f, r, ct) => f.Save(r, ct), SeedHelpers.BuildingBody, admin);
        var notJpeg = new byte[4096];
        Assert.Equal(400, (await app.Call<ReceiptPhotoFunctions>((f, r, ct) => f.Upload(r, ct), bytes: notJpeg, token: admin)).Status);
        Assert.Equal(400, (await app.Call<ReceiptPhotoFunctions>((f, r, ct) => f.Upload(r, ct), bytes: SeedHelpers.Jpeg(size: 100), token: admin)).Status);
        Assert.Equal(413, (await app.Call<ReceiptPhotoFunctions>((f, r, ct) => f.Upload(r, ct), bytes: SeedHelpers.Jpeg(size: 4 * 1024 * 1024 + 1), token: admin)).Status);
        Assert.Empty(app.Photos.Blobs);

        var id = await UploadAsync(app, admin);
        var blob = Assert.Single(app.Photos.Blobs);
        Assert.EndsWith($"{Guid.Parse(id):N}.jpg", blob.Key);
        Assert.Equal("pending", blob.Value.Status); // waits for a receipt to claim it
    }

    [Fact]
    public async Task A_receipt_claims_its_photos_in_order_and_residents_get_them_back()
    {
        using var app = new TestApp();
        var (admin, ids, _) = await SeedHelpers.BuildingWithResidentsAsync(app);
        var first = await UploadAsync(app, admin, SeedHelpers.Jpeg(fill: 1));
        var second = await UploadAsync(app, admin, SeedHelpers.Jpeg(fill: 2));

        var add = await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), ReceiptBody(new[] { second, first }), admin);
        Assert.True(add.Ok, add.Body);
        Assert.All(app.Photos.Blobs.Values, b => Assert.Equal("attached", b.Status));

        var receipts = await app.Call<ReceiptFunctions>((f, id, ct) => f.ForApartment(id, ids[0], ct), token: admin);
        var photoIds = receipts.Json[0].GetProperty("photoIds").EnumerateArray().Select(p => p.GetString()).ToArray();
        Assert.Equal(new[] { second, first }, photoIds);

        // A receipt without photos says so with an empty list.
        await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), ReceiptBody(), admin);
        var again = await app.Call<ReceiptFunctions>((f, id, ct) => f.ForApartment(id, ids[0], ct), token: admin);
        Assert.Equal(new[] { 0, 2 }, again.Json.EnumerateArray().Select(r => r.GetProperty("photoIds").GetArrayLength()).OrderBy(n => n).ToArray());
    }

    [Fact]
    public async Task Photos_are_limited_to_five_and_cannot_be_reused_invented_or_borrowed()
    {
        using var app = new TestApp();
        var (admin, _, _) = await SeedHelpers.BuildingWithResidentsAsync(app);

        var six = new List<string>();
        for (var i = 0; i < 6; i++) six.Add(await UploadAsync(app, admin, SeedHelpers.Jpeg(fill: (byte)i)));
        Assert.Equal(400, (await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), ReceiptBody(six.ToArray()), admin)).Status);
        Assert.Equal(200, (await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), ReceiptBody(six.Take(5).ToArray()), admin)).Status);

        // The sixth is still unclaimed and can be used once; the five already claimed cannot be used again.
        Assert.Equal(400, (await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), ReceiptBody(new[] { six[0] }), admin)).Status);
        Assert.Equal(400, (await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), ReceiptBody(new[] { six[5], six[5] }), admin)).Status);
        Assert.Equal(200, (await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), ReceiptBody(new[] { six[5] }), admin)).Status);

        // Not a GUID, or a GUID nobody uploaded (for example one the cleanup already removed).
        Assert.Equal(400, (await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), ReceiptBody(new[] { "../../etc" }), admin)).Status);
        Assert.Equal(400, (await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), ReceiptBody(new[] { Guid.NewGuid().ToString() }), admin)).Status);

        // Another building's administrator cannot attach the first one's upload: the stored name starts with the building.
        var other = await app.LoginAsApprovedAdminAsync("03 444 444");
        await app.Call<BuildingFunctions>((f, r, ct) => f.Save(r, ct), SeedHelpers.BuildingBody, other);
        var mine = await UploadAsync(app, admin);
        var response = await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), ReceiptBody(new[] { mine }), other);
        Assert.Equal(400, response.Status);
    }

    [Fact]
    public async Task Only_the_administrator_and_the_residents_billed_can_open_a_photo()
    {
        using var app = new TestApp();
        var (admin, ids, residents) = await SeedHelpers.BuildingWithResidentsAsync(app);
        var jpeg = SeedHelpers.Jpeg(fill: 9);
        var photo = await UploadAsync(app, admin, jpeg);

        // Billed to apartments 1A and 2A only.
        var add = await app.Call<ReceiptFunctions>((f, r, ct) => f.Add(r, ct), ReceiptBody(new[] { photo }, appartmentIds: $"{ids[0]},{ids[1]}"), admin);
        Assert.True(add.Ok, add.Body);
        var id = Guid.Parse(photo);

        var asAdmin = await app.Call<ReceiptPhotoFunctions>((f, r, ct) => f.Get(r, id, ct), token: admin);
        Assert.Equal(200, asAdmin.Status);
        Assert.Equal(jpeg, asAdmin.Bytes);
        Assert.Equal(200, (await app.Call<ReceiptPhotoFunctions>((f, r, ct) => f.Get(r, id, ct), token: residents[0])).Status);
        Assert.Equal(200, (await app.Call<ReceiptPhotoFunctions>((f, r, ct) => f.Get(r, id, ct), token: residents[1])).Status);

        // 2B was not billed, a stranger is not in the building, another building's administrator is not this one, and anonymous has no token.
        Assert.Equal(404, (await app.Call<ReceiptPhotoFunctions>((f, r, ct) => f.Get(r, id, ct), token: residents[2])).Status);
        var stranger = await app.LoginAsync("70 000 099");
        Assert.Equal(404, (await app.Call<ReceiptPhotoFunctions>((f, r, ct) => f.Get(r, id, ct), token: stranger)).Status);
        var other = await app.LoginAsApprovedAdminAsync("03 444 444");
        Assert.Equal(404, (await app.Call<ReceiptPhotoFunctions>((f, r, ct) => f.Get(r, id, ct), token: other)).Status);
        Assert.Equal(401, (await app.Call<ReceiptPhotoFunctions>((f, r, ct) => f.Get(r, id, ct))).Status);
        Assert.Equal(404, (await app.Call<ReceiptPhotoFunctions>((f, r, ct) => f.Get(r, Guid.NewGuid(), ct), token: admin)).Status);

        // A photo recorded but missing from storage answers 404 rather than failing.
        app.Photos.Blobs.Clear();
        Assert.Equal(404, (await app.Call<ReceiptPhotoFunctions>((f, r, ct) => f.Get(r, id, ct), token: admin)).Status);
    }
}
