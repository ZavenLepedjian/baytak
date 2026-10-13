using System.Globalization;

namespace Baytak.Api.Domain;

/// <summary>Body of POST wallet/add and POST wallet/update/{id}.</summary>
public sealed class EntryRequest
{
    /// <summary>Optional client-generated GUID. Re-sending the same id is safe (idempotent add).</summary>
    public string? Id { get; set; }
    public string? Description { get; set; }
    public decimal? Amount { get; set; }
    /// <summary>"USD" (default) or "LBP".</summary>
    public string? Currency { get; set; }
    /// <summary>"income" or "expense".</summary>
    public string? Type { get; set; }
    /// <summary>Calendar date, "yyyy-MM-dd". Defaults to today.</summary>
    public string? Date { get; set; }
}

/// <summary>Validated, normalised input for creating or updating a wallet entry.</summary>
public sealed record EntryInput(
    Guid? Id,
    string Description,
    long AmountMinor,
    string Currency,
    string Type,
    DateOnly Date);

public readonly record struct ValidationResult<T>(T? Value, string? Error) where T : class
{
    public bool IsValid => Error is null;
}

public static class Money
{
    public static readonly string[] Currencies = ["USD", "LBP"];

    /// <summary>Returns "USD" / "LBP" for a case-insensitive match, otherwise null.</summary>
    public static string? NormalizeCurrency(string? value)
    {
        var text = value?.Trim();
        return Currencies.FirstOrDefault(c => string.Equals(c, text, StringComparison.OrdinalIgnoreCase));
    }
}

public static class EntryValidator
{
    public const int MaxDescriptionLength = 200;
    public const decimal MaxAmount = 1_000_000_000_000m; // 1e12
    private static readonly DateOnly MinDate = new(2000, 1, 1);
    private static readonly DateOnly MaxDate = new(2100, 12, 31);

    public static ValidationResult<EntryInput> Validate(EntryRequest? request, DateOnly today)
    {
        if (request is null) return Fail("Request body is required");

        Guid? id = null;
        if (!string.IsNullOrWhiteSpace(request.Id))
        {
            if (!Guid.TryParse(request.Id, out var parsed) || parsed == Guid.Empty) return Fail("Id must be a valid GUID");
            id = parsed;
        }

        var description = Text.Clean(request.Description);
        if (description.Length == 0) return Fail("Description is required");
        if (description.Length > MaxDescriptionLength)
            return Fail($"Description must be at most {MaxDescriptionLength} characters");

        if (request.Amount is not { } amount) return Fail("Amount is required");
        if (amount <= 0) return Fail("Amount must be greater than 0");
        if (amount > MaxAmount) return Fail("Amount is too large");
        if (decimal.Round(amount, 2) != amount) return Fail("Amount can have at most 2 decimal places");

        var currency = "USD";
        if (!string.IsNullOrWhiteSpace(request.Currency))
        {
            var normalized = Money.NormalizeCurrency(request.Currency);
            if (normalized is null) return Fail("Currency must be USD or LBP");
            currency = normalized;
        }

        var typeText = request.Type?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(typeText)) return Fail("Type is required (income or expense)");
        if (typeText is not ("income" or "expense")) return Fail("Type must be income or expense");

        var date = today;
        if (!string.IsNullOrWhiteSpace(request.Date) && !TryParseDate(request.Date, out date))
            return Fail("Date must look like 2025-01-31");
        if (date < MinDate || date > MaxDate) return Fail("Date is out of range");

        var minor = (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
        return new ValidationResult<EntryInput>(new EntryInput(id, description, minor, currency, typeText, date), null);
    }

    /// <summary>
    /// Accepts "yyyy-MM-dd" or a full ISO timestamp. For timestamps the calendar date as written
    /// (in the sender's offset) is used, so a phone in UTC+3 never shifts the day.
    /// </summary>
    public static bool TryParseDate(string value, out DateOnly date)
    {
        var text = value.Trim();
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return true;
        if (text.Length > 10 && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
        {
            date = DateOnly.FromDateTime(dto.DateTime);
            return true;
        }
        date = default;
        return false;
    }

    private static ValidationResult<EntryInput> Fail(string error) => new(null, error);
}

