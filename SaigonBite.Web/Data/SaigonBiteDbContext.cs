using Microsoft.EntityFrameworkCore;
using SaigonBite.Core;
using SaigonBite.Core.Payments;
using SaigonBite.Core.Pricing;

namespace SaigonBite.Web.Data;

public class SaigonBiteDbContext : DbContext
{
    public SaigonBiteDbContext(DbContextOptions<SaigonBiteDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<LocalCustomerProfile> LocalCustomerProfiles => Set<LocalCustomerProfile>();
    public DbSet<TouristProfile> TouristProfiles => Set<TouristProfile>();
    public DbSet<Hub> Hubs => Set<Hub>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<HubBrand> HubBrands => Set<HubBrand>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<ShipperAssignment> ShipperAssignments => Set<ShipperAssignment>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<PendingRegistration> PendingRegistrations => Set<PendingRegistration>();
    public DbSet<PendingOnlinePayment> PendingOnlinePayments => Set<PendingOnlinePayment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Explicit table names keep the Code First schema easy to discuss in the Q&A.
        modelBuilder.Entity<User>().ToTable("Users");
        modelBuilder.Entity<LocalCustomerProfile>().ToTable("LocalCustomerProfiles");
        modelBuilder.Entity<TouristProfile>().ToTable("TouristProfiles");
        modelBuilder.Entity<Hub>().ToTable("Hubs");
        modelBuilder.Entity<Brand>().ToTable("Brands");
        modelBuilder.Entity<HubBrand>().ToTable("HubBrands");
        modelBuilder.Entity<MenuItem>().ToTable("MenuItems");
        modelBuilder.Entity<Voucher>().ToTable("Vouchers");
        modelBuilder.Entity<Order>().ToTable("Orders");
        modelBuilder.Entity<OrderLine>().ToTable("OrderLines");
        modelBuilder.Entity<OrderStatusHistory>().ToTable("OrderStatusHistories");
        modelBuilder.Entity<Payment>().ToTable("Payments");
        modelBuilder.Entity<ShipperAssignment>().ToTable("ShipperAssignments");
        modelBuilder.Entity<SystemSetting>().ToTable("SystemSettings");
        modelBuilder.Entity<PendingRegistration>().ToTable("PendingRegistrations");
        modelBuilder.Entity<PendingOnlinePayment>().ToTable("PendingOnlinePayments");

        // Global soft-delete filters. IsActive is NOT filtered: deactivated users stay in history (A-09),
        // and inactive vouchers must still be read to produce the A-11 note (BR9).
        // Order detail/history and R1/R2 explicitly call IgnoreQueryFilters() in Services.
        modelBuilder.Entity<User>().HasQueryFilter(x => !x.IsDeleted);
        modelBuilder.Entity<Hub>().HasQueryFilter(x => !x.IsDeleted);
        modelBuilder.Entity<Brand>().HasQueryFilter(x => !x.IsDeleted);
        modelBuilder.Entity<MenuItem>().HasQueryFilter(x => !x.IsDeleted);
        modelBuilder.Entity<Voucher>().HasQueryFilter(x => !x.IsDeleted);

        // Money/distance/load are decimal, never double. Load keeps 4 decimals (0.295).
        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            property.SetPrecision(18);
            property.SetScale(property.Name.Contains("Load") ? 4 : 2);
        }

        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Identifier).IsUnique(); // A-09: unique across all users
            e.HasOne(x => x.Hub).WithMany().HasForeignKey(x => x.HubId)
                .OnDelete(DeleteBehavior.Restrict); // keep order history
        });

        modelBuilder.Entity<LocalCustomerProfile>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasOne(x => x.User).WithOne(x => x.LocalProfile)
                .HasForeignKey<LocalCustomerProfile>(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TouristProfile>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasOne(x => x.User).WithOne(x => x.TouristProfile)
                .HasForeignKey<TouristProfile>(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<HubBrand>(e =>
        {
            e.HasKey(x => new { x.HubId, x.BrandId }); // BR12: hub operates these brands
            e.HasOne(x => x.Hub).WithMany(x => x.HubBrands).HasForeignKey(x => x.HubId);
            e.HasOne(x => x.Brand).WithMany(x => x.HubBrands).HasForeignKey(x => x.BrandId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MenuItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Brand).WithMany(x => x.MenuItems).HasForeignKey(x => x.BrandId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Voucher>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<Order>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict); // orders are never deleted
            e.HasOne(x => x.Hub).WithMany().HasForeignKey(x => x.HubId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Voucher).WithMany().HasForeignKey(x => x.VoucherId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.AssignedShipper).WithMany().HasForeignKey(x => x.AssignedShipperId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrderLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Order).WithMany(x => x.Lines).HasForeignKey(x => x.OrderId);
            e.HasOne(x => x.MenuItem).WithMany().HasForeignKey(x => x.MenuItemId)
                .OnDelete(DeleteBehavior.Restrict); // UnitPrice is a snapshot (BR17)
        });

        modelBuilder.Entity<OrderStatusHistory>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Order).WithMany(x => x.StatusHistory).HasForeignKey(x => x.OrderId);
            e.HasOne(x => x.Actor).WithMany().HasForeignKey(x => x.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict); // history stays after deactivation (A-09)
        });

        modelBuilder.Entity<Payment>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Order).WithMany(x => x.Payments).HasForeignKey(x => x.OrderId);
        });

        modelBuilder.Entity<ShipperAssignment>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Order).WithMany(x => x.ShipperAssignments).HasForeignKey(x => x.OrderId);
            e.HasOne(x => x.Shipper).WithMany().HasForeignKey(x => x.ShipperId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SystemSetting>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Key).IsUnique();
            e.HasOne(x => x.UpdatedByUser).WithMany().HasForeignKey(x => x.UpdatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // A-09: non-unique index because cancelled/expired rows stay for the demo; services
        // reject a second active pending registration for the same normalised identifier.
        modelBuilder.Entity<PendingRegistration>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Identifier);
        });

        modelBuilder.Entity<PendingOnlinePayment>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TransactionRef).IsUnique();
        });

        Seed(modelBuilder);
    }

    /// <summary>Small, deterministic dev data. No orders are seeded (Step D creates them).</summary>
    private static void Seed(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Hub>().HasData(
            new Hub { Id = 1, Name = "Hub District 1", Capacity = 10 },
            new Hub { Id = 2, Name = "Hub Binh Thanh", Capacity = 10 },
            new Hub { Id = 3, Name = "Hub Thu Duc", Capacity = 10 });

        modelBuilder.Entity<Brand>().HasData(
            new Brand { Id = 1, Name = "Pho Saigon" },
            new Brand { Id = 2, Name = "Ca Phe Rang" },
            new Brand { Id = 3, Name = "Che Ngon" });

        modelBuilder.Entity<HubBrand>().HasData(
            new HubBrand { HubId = 1, BrandId = 1 },
            new HubBrand { HubId = 1, BrandId = 2 },
            new HubBrand { HubId = 1, BrandId = 3 },
            new HubBrand { HubId = 2, BrandId = 1 },
            new HubBrand { HubId = 2, BrandId = 2 },
            new HubBrand { HubId = 3, BrandId = 1 },
            new HubBrand { HubId = 3, BrandId = 3 });

        modelBuilder.Entity<MenuItem>().HasData(
            new MenuItem { Id = 1, BrandId = 1, Name = "Pho Bo", Category = "Noodles", Price = 55_000m },
            new MenuItem { Id = 2, BrandId = 1, Name = "Com Tam", Category = "Rice", Price = 60_000m },
            new MenuItem { Id = 3, BrandId = 2, Name = "Ca Phe Sua Da", Category = "Drinks", Price = 35_000m },
            new MenuItem { Id = 4, BrandId = 2, Name = "Ca Phe Den", Category = "Drinks", Price = 30_000m },
            new MenuItem { Id = 5, BrandId = 3, Name = "Che Ba Mau", Category = "Dessert", Price = 40_000m },
            new MenuItem { Id = 6, BrandId = 3, Name = "Che Dau Xanh", Category = "Dessert", Price = 45_000m });

        modelBuilder.Entity<Voucher>().HasData(
            new Voucher
            {
                Id = 1, Code = "P10", Type = VoucherType.Percent, Value = 10m, MaxDiscount = 30_000m,
                MinSubtotal = 100_000m, ValidFrom = new DateOnly(2026, 1, 1),
                ValidTo = new DateOnly(2026, 12, 31), IsActive = true
            },
            new Voucher
            {
                Id = 2, Code = "F60", Type = VoucherType.Fixed, Value = 60_000m, MaxDiscount = null,
                MinSubtotal = 0m, ValidFrom = new DateOnly(2026, 1, 1),
                ValidTo = new DateOnly(2026, 12, 31), IsActive = true
            },
            new Voucher
            {
                Id = 3, Code = "EXPIRED20", Type = VoucherType.Percent, Value = 20m, MaxDiscount = 50_000m,
                MinSubtotal = 50_000m, ValidFrom = new DateOnly(2025, 1, 1),
                ValidTo = new DateOnly(2025, 12, 31), IsActive = true
            },
            new Voucher
            {
                Id = 4, Code = "INACTIVE50", Type = VoucherType.Fixed, Value = 50_000m, MaxDiscount = null,
                MinSubtotal = 0m, ValidFrom = new DateOnly(2026, 1, 1),
                ValidTo = new DateOnly(2026, 12, 31), IsActive = false
            });

        // N-02: HasData fixes identities only; SeedUserInitializer sets random-salt hashes at startup.
        // Seed passwords come from configuration/environment variables and are at least 8 chars (BR18).
        modelBuilder.Entity<User>().HasData(
            new User { Id = 1, Role = Role.Admin, Identifier = "admin", PasswordHash = SeedCredential.PendingHash, IsActive = true },
            new User { Id = 2, Role = Role.KitchenStaff, Identifier = "kitchen-d1", PasswordHash = SeedCredential.PendingHash, IsActive = true, HubId = 1 },
            new User { Id = 3, Role = Role.KitchenStaff, Identifier = "kitchen-bt", PasswordHash = SeedCredential.PendingHash, IsActive = true, HubId = 2 },
            new User { Id = 4, Role = Role.KitchenStaff, Identifier = "kitchen-td", PasswordHash = SeedCredential.PendingHash, IsActive = true, HubId = 3 },
            new User { Id = 5, Role = Role.Shipper, Identifier = "shipper1", PasswordHash = SeedCredential.PendingHash, IsActive = true },
            new User { Id = 6, Role = Role.Shipper, Identifier = "shipper2", PasswordHash = SeedCredential.PendingHash, IsActive = true },
            new User { Id = 7, Role = Role.Local, Identifier = "0912345678", PasswordHash = SeedCredential.PendingHash, IsActive = true },
            new User { Id = 8, Role = Role.Tourist, Identifier = "A1234567", PasswordHash = SeedCredential.PendingHash, IsActive = true });

        modelBuilder.Entity<LocalCustomerProfile>().HasData(
            new LocalCustomerProfile { Id = 1, UserId = 7, Mobile = "0912345678" });

        modelBuilder.Entity<TouristProfile>().HasData(
            new TouristProfile
            {
                Id = 1, UserId = 8, Passport = "A1234567",
                Nationality = "USA", Email = "tourist@example.com"
            });

        // FR-D14 / BR5: default heavy-rain flag is false; Admin changes it through SettingsService.
        modelBuilder.Entity<SystemSetting>().HasData(
            new SystemSetting
            {
                Id = 1, Key = "HeavyRain", Value = "false",
                UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0), UpdatedByUserId = 1
            });
    }
}
