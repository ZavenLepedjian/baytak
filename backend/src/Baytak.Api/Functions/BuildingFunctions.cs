using Baytak.Api.Common;
using Baytak.Api.Data;
using Baytak.Api.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Baytak.Api.Functions;

public sealed class BuildingRequest
{
    public string? BuildingNumber { get; set; }
    public string? BuildingName { get; set; }
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public int? NumberOfFloors { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public sealed class BuildingFunctions : ApiFunction
{
    private readonly BaytakDbContext _db;
    private readonly TimeProvider _time;

    public BuildingFunctions(IAccessGuard guard, BaytakDbContext db, TimeProvider time, ILogger<BuildingFunctions> logger)
        : base(guard, logger)
    {
        _db = db;
        _time = time;
    }

    /// <summary>Every registered building, for residents choosing where to join.</summary>
    [Function("BuildingsList")]
    public Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "buildings/get")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Signed, ct, async _ =>
        {
            var rows = await _db.Buildings.AsNoTracking()
                .OrderBy(b => b.BuildingName)
                .Take(1000)
                .Select(b => new { b.Id, b.BuildingName, b.Street, b.City, b.Latitude, b.Longitude })
                .ToListAsync(ct);

            return Reply.Ok(rows.Select(b => new
            {
                label = string.Join(", ", new[] { b.BuildingName, b.Street, b.City }.Where(s => !string.IsNullOrWhiteSpace(s))),
                value = b.Id,
                latitude = b.Latitude,
                longitude = b.Longitude,
            }));
        });

    /// <summary>The administrator's own building (empty defaults before it has been set up).</summary>
    [Function("BuildingGet")]
    public Task<IActionResult> Get(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "building/get")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Admin, ct, async admin =>
        {
            var building = await _db.Buildings.AsNoTracking().FirstOrDefaultAsync(b => b.AdminUserId == admin.Id, ct);
            if (building is null)
            {
                return Reply.Ok(new
                {
                    buildingNumber = "", buildingName = "", city = "", country = "", street = "",
                    numberOfFloors = 0, linkedAppartments = 0, longitude = 0.0, latitude = 0.0,
                });
            }

            var linked = await _db.Apartments.CountAsync(a => a.BuildingId == building.Id && a.Status != ApartmentStatuses.Rejected, ct);
            return Reply.Ok(new
            {
                buildingNumber = building.BuildingNumber,
                buildingName = building.BuildingName,
                city = building.City,
                country = building.Country,
                street = building.Street,
                numberOfFloors = building.NumberOfFloors,
                linkedAppartments = linked,
                longitude = building.Longitude,
                latitude = building.Latitude,
            });
        });

    [Function("BuildingSave")]
    public Task<IActionResult> Save(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "building/save")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Admin, ct, async admin =>
        {
            var (body, error) = await Reply.ReadJsonAsync<BuildingRequest>(req, ct);
            if (body is null) return Reply.BadRequest(error!);

            var number = Text.Clean(body.BuildingNumber);
            var name = Text.Clean(body.BuildingName);
            var street = Text.Clean(body.Street);
            var city = Text.Clean(body.City);
            var country = Text.Clean(body.Country);

            if (number.Length == 0 || name.Length == 0 || street.Length == 0 || city.Length == 0 || country.Length == 0)
                return Reply.BadRequest("Building number, name, street, city and country are required");
            if (number.Length > 60 || name.Length > 120 || street.Length > 160 || city.Length > 100 || country.Length > 100)
                return Reply.BadRequest("One of the fields is too long");
            if (body.NumberOfFloors is not (>= 1 and <= 200)) return Reply.BadRequest("Number of floors must be between 1 and 200");
            if (body.Latitude is not { } lat || body.Longitude is not { } lon || lat is < -90 or > 90 || lon is < -180 or > 180 ||
                (lat == 0 && lon == 0))
                return Reply.BadRequest("Please set the building location on the map");

            var building = await _db.Buildings.FirstOrDefaultAsync(b => b.AdminUserId == admin.Id, ct);
            if (building is not null)
            {
                var linked = await _db.Apartments.CountAsync(a => a.BuildingId == building.Id && a.Status != ApartmentStatuses.Rejected, ct);
                if (linked > 0) return Reply.BadRequest("Apartments have already joined this building, so its details can no longer be changed.");
            }
            else
            {
                building = new Building { AdminUserId = admin.Id, CreatedAt = _time.GetUtcNow().UtcDateTime };
                _db.Buildings.Add(building);
            }

            building.BuildingNumber = number;
            building.BuildingName = name;
            building.Street = street;
            building.City = city;
            building.Country = country;
            building.NumberOfFloors = body.NumberOfFloors.Value;
            building.Latitude = lat;
            building.Longitude = lon;
            await _db.SaveChangesAsync(ct);
            return Reply.Message("Building saved successfully");
        });
}
