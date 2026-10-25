using Baytak.Api.Functions;

namespace Baytak.Api.Tests;

/// <summary>Shared test setup: an approved administrator with a building and three approved residents.</summary>
internal static class SeedHelpers
{
    public static readonly object BuildingBody = new
    {
        buildingNumber = "1", buildingName = "Tower", street = "Main", city = "Beirut", country = "LB",
        numberOfFloors = 6, latitude = 33.9, longitude = 35.5,
    };

    /// <summary>1A (floor 1), 2A and 2B (floor 2). Returns the admin token, the apartment ids and the residents' tokens.</summary>
    public static async Task<(string Admin, int[] Ids, string[] Residents)> BuildingWithResidentsAsync(TestApp app, string adminPhone = "03 111 111", string phonePrefix = "70 000 00")
    {
        var admin = await app.LoginAsApprovedAdminAsync(adminPhone);
        await app.Call<BuildingFunctions>((f, r, ct) => f.Save(r, ct), BuildingBody, admin);

        var specs = new[] { ($"{phonePrefix}1", "1A", 1), ($"{phonePrefix}2", "2A", 2), ($"{phonePrefix}3", "2B", 2) };
        var residents = new List<string>();
        foreach (var (phone, number, floor) in specs)
        {
            var token = await app.LoginAsync(phone);
            residents.Add(token);
            var buildingId = (await app.Call<BuildingFunctions>((f, r, ct) => f.List(r, ct), token: token)).Json
                .EnumerateArray().Last().GetProperty("value").GetInt32();
            await app.Call<ApartmentFunctions>((f, r, ct) => f.RequestJoin(r, ct), new { appartmentNumber = number, appartmentDescription = "d", floor, buildingId }, token);
            await app.Call<UserFunctions>((f, r, ct) => f.SaveProfile(r, ct), new { firstName = $"Res{number}", lastName = "Test" }, token);
        }

        var ids = new List<int>();
        foreach (var a in (await app.Call<ApartmentFunctions>((f, r, ct) => f.Pending(r, ct), token: admin)).Json.EnumerateArray().ToList())
        {
            var id = a.GetProperty("id").GetInt32();
            ids.Add(id);
            await app.Call<ApartmentFunctions>((f, r, ct) => f.Approve(r, ct), new { id }, admin);
        }
        return (admin, ids.ToArray(), residents.ToArray());
    }

    /// <summary>A small but valid-looking JPEG: the signature the server checks, padded past the minimum size.</summary>
    public static byte[] Jpeg(int size = 2048, byte fill = 7)
    {
        var data = new byte[size];
        Array.Fill(data, fill);
        data[0] = 0xFF; data[1] = 0xD8; data[2] = 0xFF; data[3] = 0xE0;
        return data;
    }
}
