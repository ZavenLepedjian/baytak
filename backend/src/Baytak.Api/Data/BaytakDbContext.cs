using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Baytak.Api.Data;

public class BaytakDbContext : DbContext
{
    public BaytakDbContext(DbContextOptions<BaytakDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<Apartment> Apartments => Set<Apartment>();
    public DbSet<ReceiptFormat> ReceiptFormats => Set<ReceiptFormat>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<ReceiptAssignment> ReceiptAssignments => Set<ReceiptAssignment>();
    public DbSet<WalletEntry> WalletEntries => Set<WalletEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.PhoneNumber).IsUnique();
            e.Property(x => x.PhoneNumber).HasMaxLength(32).IsRequired();
            e.Property(x => x.Role).HasMaxLength(16).IsRequired();
            e.Property(x => x.Status).HasMaxLength(16).IsRequired();
            e.Property(x => x.FirstName).HasMaxLength(80);
            e.Property(x => x.LastName).HasMaxLength(80);
            e.Property(x => x.EmergencyContactName).HasMaxLength(120);
            e.Property(x => x.EmergencyContactPhone).HasMaxLength(32);
            e.Property(x => x.ExpoPushToken).HasMaxLength(200);
            e.Ignore(x => x.FullName);
        });

        b.Entity<OtpCode>(e =>
        {
            e.HasIndex(x => new { x.PhoneNumber, x.CreatedAt });
            e.Property(x => x.PhoneNumber).HasMaxLength(32).IsRequired();
            e.Property(x => x.CodeHash).HasMaxLength(128).IsRequired();
            e.Property(x => x.RequestedStatus).HasMaxLength(32);
        });

        b.Entity<Building>(e =>
        {
            e.HasIndex(x => x.AdminUserId).IsUnique();
            e.HasOne(x => x.Admin).WithMany().HasForeignKey(x => x.AdminUserId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.BuildingNumber).HasMaxLength(60).IsRequired();
            e.Property(x => x.BuildingName).HasMaxLength(120).IsRequired();
            e.Property(x => x.Street).HasMaxLength(160).IsRequired();
            e.Property(x => x.City).HasMaxLength(100).IsRequired();
            e.Property(x => x.Country).HasMaxLength(100).IsRequired();
        });

        b.Entity<Apartment>(e =>
        {
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasIndex(x => new { x.BuildingId, x.ApartmentNumber }).IsUnique();
            e.HasIndex(x => new { x.BuildingId, x.Status });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Building).WithMany(x => x.Apartments).HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.ApartmentNumber).HasMaxLength(60).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.Status).HasMaxLength(16).IsRequired();
        });

        b.Entity<ReceiptFormat>(e =>
        {
            e.HasIndex(x => x.BuildingId).IsUnique();
            e.HasOne(x => x.Building).WithMany().HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Currency).HasMaxLength(8).IsRequired();
        });

        b.Entity<Receipt>(e =>
        {
            e.HasIndex(x => new { x.BuildingId, x.Date });
            e.HasOne(x => x.Building).WithMany().HasForeignKey(x => x.BuildingId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Currency).HasMaxLength(8).IsRequired();
            e.Property(x => x.Total).HasPrecision(18, 2);
            e.Property(x => x.Version).HasMaxLength(16);
        });

        b.Entity<ReceiptAssignment>(e =>
        {
            e.HasIndex(x => new { x.ReceiptId, x.ApartmentId }).IsUnique();
            e.HasIndex(x => x.ApartmentId);
            e.HasOne(x => x.Receipt).WithMany(x => x.Assignments).HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Apartment).WithMany().HasForeignKey(x => x.ApartmentId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Status).HasMaxLength(16).IsRequired();
        });

        b.Entity<WalletEntry>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Date });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Description).HasMaxLength(200).IsRequired();
            e.Property(x => x.Currency).HasMaxLength(8).IsRequired();
            e.Property(x => x.Type).HasMaxLength(8).IsRequired();
            e.Ignore(x => x.Amount);
        });

        // All timestamps are UTC. Providers return them with an unspecified kind, which would serialise without a "Z".
        var utc = new ValueConverter<DateTime, DateTime>(v => v.ToUniversalTime(), v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var utcNullable = new ValueConverter<DateTime?, DateTime?>(
            v => v.HasValue ? v.Value.ToUniversalTime() : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
        foreach (var property in b.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
        {
            if (property.ClrType == typeof(DateTime)) property.SetValueConverter(utc);
            else if (property.ClrType == typeof(DateTime?)) property.SetValueConverter(utcNullable);
        }
    }
}

/// <summary>Lets `dotnet ef migrations add` build the model (always against SQL Server, the production provider).</summary>
public class DesignTimeFactory : IDesignTimeDbContextFactory<BaytakDbContext>
{
    public BaytakDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BaytakDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=Baytak;Trusted_Connection=True;")
            .Options;
        return new BaytakDbContext(options);
    }
}
