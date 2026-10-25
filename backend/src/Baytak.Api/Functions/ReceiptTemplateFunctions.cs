using System.Globalization;
using System.Text.Json;
using Baytak.Api.Common;
using Baytak.Api.Data;
using Baytak.Api.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Baytak.Api.Functions;

public sealed class SaveTemplateRequest
{
    public string? Name { get; set; }
    public string? Currency { get; set; }
    public List<ReceiptFieldDto>? CustomFields { get; set; }
}

/// <summary>
/// Starting points for a new receipt: the administrator's saved templates and a copy of the last receipt issued.
///   GET  receipt/templates                 { templates, last }
///   POST receipt/templates/save            create or replace a template by name
///   POST receipt/templates/delete/{id}
/// A template keeps line names and types, not amounts: amounts are typed for each receipt.
/// </summary>
public sealed class ReceiptTemplateFunctions : ApiFunction
{
    public const int MaxTemplates = 20;
    private const int MaxNameLength = 60;

    private readonly BaytakDbContext _db;
    private readonly TimeProvider _time;

    public ReceiptTemplateFunctions(IAccessGuard guard, BaytakDbContext db, TimeProvider time, ILogger<ReceiptTemplateFunctions> logger)
        : base(guard, logger)
    {
        _db = db;
        _time = time;
    }

    [Function("ReceiptTemplates")]
    public Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "receipt/templates")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Admin, ct, async admin =>
        {
            var building = await _db.Buildings.AsNoTracking().FirstOrDefaultAsync(b => b.AdminUserId == admin.Id, ct);
            if (building is null) return Reply.Ok(new { templates = Array.Empty<object>(), last = (object?)null });

            var stored = await _db.ReceiptTemplates.AsNoTracking().Where(t => t.BuildingId == building.Id).OrderBy(t => t.Name).ToListAsync(ct);
            var templates = stored.Select(t => new
            {
                id = t.Id,
                name = t.Name,
                currency = t.Currency,
                customFields = (JsonSerializer.Deserialize<List<ReceiptFunctions.StoredField>>(t.FieldsJson, Reply.Json) ?? [])
                    .Select(f => new { fieldName = f.FieldName, type = f.Type, value = "" }),
            }).ToList();

            // The newest receipt's lines and amounts, so "same as last time" is one tap.
            var latest = await _db.Receipts.AsNoTracking()
                .Where(r => r.BuildingId == building.Id)
                .OrderByDescending(r => r.Date).ThenByDescending(r => r.Id)
                .Select(r => new { r.Currency, r.Date, r.BodyJson })
                .FirstOrDefaultAsync(ct);
            object? last = latest is null ? null : new
            {
                currency = latest.Currency,
                date = latest.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                customFields = ReceiptFunctions.ReadFields(latest.BodyJson)
                    .Select(f => new { fieldName = f.Name, type = f.IsAmount ? "Amount" : "Text", value = f.Value }),
            };

            return Reply.Ok(new { templates, last });
        });

    [Function("ReceiptTemplateSave")]
    public Task<IActionResult> Save(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "receipt/templates/save")] HttpRequest req, CancellationToken ct) =>
        Run(req, AccessLevel.Admin, ct, async admin =>
        {
            var (body, error) = await Reply.ReadJsonAsync<SaveTemplateRequest>(req, ct);
            if (body is null) return Reply.BadRequest(error!);

            var building = await _db.Buildings.AsNoTracking().FirstOrDefaultAsync(b => b.AdminUserId == admin.Id, ct);
            if (building is null) return Reply.BadRequest("Please set up your building first");

            var name = Text.Clean(body.Name);
            if (name.Length == 0) return Reply.BadRequest("Give the template a name");
            if (name.Length > MaxNameLength) return Reply.BadRequest($"Template names can have at most {MaxNameLength} characters");

            var currency = Money.NormalizeCurrency(body.Currency);
            if (currency is null) return Reply.BadRequest("Please select a currency (USD or LBP)");

            var (fields, fieldError) = ReceiptFunctions.NormalizeFields(body.CustomFields, requireAmount: true);
            if (fields is null) return Reply.BadRequest(fieldError!);

            var fieldsJson = JsonSerializer.Serialize(fields.Select(f => new ReceiptFunctions.StoredField(f.Name, f.IsAmount ? "Amount" : "Text")), Reply.Json);

            var existing = await _db.ReceiptTemplates.Where(t => t.BuildingId == building.Id).ToListAsync(ct);
            var match = existing.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                if (existing.Count >= MaxTemplates) return Reply.BadRequest($"You can keep at most {MaxTemplates} templates. Delete one first.");
                match = new ReceiptTemplate { BuildingId = building.Id };
                _db.ReceiptTemplates.Add(match);
            }
            match.Name = name;
            match.Currency = currency;
            match.FieldsJson = fieldsJson;
            match.UpdatedAt = _time.GetUtcNow().UtcDateTime;
            await _db.SaveChangesAsync(ct);
            return Reply.Message("Template saved");
        });

    [Function("ReceiptTemplateDelete")]
    public Task<IActionResult> Delete(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "receipt/templates/delete/{id:int}")] HttpRequest req, int id, CancellationToken ct) =>
        Run(req, AccessLevel.Admin, ct, async admin =>
        {
            var template = await _db.ReceiptTemplates.FirstOrDefaultAsync(t => t.Id == id && t.Building!.AdminUserId == admin.Id, ct);
            if (template is null) return Reply.NotFound("Template not found");
            _db.ReceiptTemplates.Remove(template);
            await _db.SaveChangesAsync(ct);
            return Reply.Message("Template deleted");
        });
}
