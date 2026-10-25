namespace Baytak.Api.Data;

public static class Roles
{
    public const string User = "User";
    public const string Admin = "Admin";
}

/// <summary>Account approval state. Resident accounts are approved immediately; administrators wait for review.</summary>
public static class UserStatuses
{
    public const string Approved = "Approved";
    public const string Pending = "Pending";
    public const string Rejected = "Rejected";
}

public static class ApartmentStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

public static class PaymentStatuses
{
    public const string Pending = "Pending";
    public const string Paid = "Paid";
}

public class User
{
    public Guid Id { get; set; }
    /// <summary>Normalised (E.164 when possible) phone number; the login identity.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
    public string Role { get; set; } = Roles.User;
    public string Status { get; set; } = UserStatuses.Approved;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string EmergencyContactName { get; set; } = string.Empty;
    public string EmergencyContactPhone { get; set; } = string.Empty;
    public string? ExpoPushToken { get; set; }
    /// <summary>Incremented to revoke every token issued so far (sign-out). Tokens carry it as the "sv" claim.</summary>
    public int SessionVersion { get; set; }
    public double? LastLatitude { get; set; }
    public double? LastLongitude { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastLoginAt { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();
}

/// <summary>A one-time SMS login code. Only a keyed hash of the code is stored.</summary>
public class OtpCode
{
    public Guid Id { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string CodeHash { get; set; } = string.Empty;
    /// <summary>"" or "request-admin": applied to the account once the code is verified.</summary>
    public string RequestedStatus { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}

public class Building
{
    public int Id { get; set; }
    /// <summary>The administrator who manages (and created) the building.</summary>
    public Guid AdminUserId { get; set; }
    public User? Admin { get; set; }
    public string BuildingNumber { get; set; } = string.Empty;
    public string BuildingName { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public int NumberOfFloors { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<Apartment> Apartments { get; set; } = new();
}

public class Apartment
{
    public int Id { get; set; }
    /// <summary>The resident. A resident has at most one apartment.</summary>
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public int BuildingId { get; set; }
    public Building? Building { get; set; }
    public string ApartmentNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Floor { get; set; }
    public string Status { get; set; } = ApartmentStatuses.Pending;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>The fields (and currency) a building's receipts are made of.</summary>
public class ReceiptFormat
{
    public int Id { get; set; }
    public int BuildingId { get; set; }
    public Building? Building { get; set; }
    public string Currency { get; set; } = "USD";
    /// <summary>JSON array of { fieldName, type, value }.</summary>
    public string FieldsJson { get; set; } = "[]";
    public DateTime UpdatedAt { get; set; }
}

public class Receipt
{
    public int Id { get; set; }
    public int BuildingId { get; set; }
    public Building? Building { get; set; }
    public Guid CreatedByUserId { get; set; }
    public User? CreatedBy { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal Total { get; set; }
    public DateOnly Date { get; set; }
    /// <summary>The receipt as shown to residents: { Currency, Date, CustomFields[] }.</summary>
    public string BodyJson { get; set; } = "{}";
    public string Version { get; set; } = "v2.0";
    public DateTime CreatedAt { get; set; }
    /// <summary>When the administrator last pushed a payment reminder for this receipt (limited to once a day).</summary>
    public DateTime? LastReminderAt { get; set; }
    public List<ReceiptAssignment> Assignments { get; set; } = new();
    /// <summary>Photos of the bill or invoice the receipt is based on, attached when the receipt is issued (at most five).</summary>
    public List<ReceiptPhoto> Photos { get; set; } = new();
}

/// <summary>
/// A photo attached to a receipt. The image itself lives in private Blob storage under <see cref="BlobName"/>;
/// only the building's administrator and the residents the receipt was issued to can read it.
/// </summary>
public class ReceiptPhoto
{
    /// <summary>Also the blob's file name, and what the app asks for.</summary>
    public Guid Id { get; set; }
    public int ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }
    /// <summary>Order the administrator attached them in, from 0.</summary>
    public int Position { get; set; }
    /// <summary>Path inside the photos container: "{buildingId}/{id}.jpg".</summary>
    public string BlobName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/// <summary>A named set of receipt lines an administrator saved to start new receipts from (e.g. "Monthly fees").</summary>
public class ReceiptTemplate
{
    public int Id { get; set; }
    public int BuildingId { get; set; }
    public Building? Building { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    /// <summary>JSON array of { fieldName, type }. Amounts are not saved: they are typed per receipt.</summary>
    public string FieldsJson { get; set; } = "[]";
    public DateTime UpdatedAt { get; set; }
}

/// <summary>A receipt issued to one apartment, with that apartment's payment status.</summary>
public class ReceiptAssignment
{
    public int Id { get; set; }
    public int ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }
    public int ApartmentId { get; set; }
    public Apartment? Apartment { get; set; }
    public string Status { get; set; } = PaymentStatuses.Pending;
    public DateTime? PaidAt { get; set; }
}

/// <summary>One line in a user's personal wallet ledger. Amounts are stored in minor units (x100).</summary>
public class WalletEntry
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string Description { get; set; } = string.Empty;
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "USD";
    /// <summary>"income" or "expense".</summary>
    public string Type { get; set; } = "expense";
    public DateOnly Date { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public decimal Amount => AmountMinor / 100m;
}
