using SaigonBite.Core;
using SaigonBite.Core.Payments;
using SaigonBite.Core.Pricing;

namespace SaigonBite.Web.Data;

// BR18: one Users table with Role + one profile table per customer type.
public class User
{
    public int Id { get; set; }
    public Role Role { get; set; }
    public string Identifier { get; set; } = string.Empty; // phone or passport/email; unique across all users (A-09)
    public string PasswordHash { get; set; } = string.Empty; // PBKDF2 hash of a >= 8-char password (BR18)
    public bool IsActive { get; set; } = true; // false = cannot log in, but history stays (A-09)
    public bool IsDeleted { get; set; } // global soft-delete filter
    public int? HubId { get; set; }
    public Hub? Hub { get; set; }

    public LocalCustomerProfile? LocalProfile { get; set; }
    public TouristProfile? TouristProfile { get; set; }
}

public class LocalCustomerProfile
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string Mobile { get; set; } = string.Empty; // 10 digits starting 03/05/07/08/09 (BR18)
}

public class TouristProfile
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string Passport { get; set; } = string.Empty; // 6-9 letters/digits (BR18)
    public string Nationality { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class Hub
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Capacity { get; set; } // BR8 denominator
    public bool IsDeleted { get; set; }

    public ICollection<HubBrand> HubBrands { get; set; } = new List<HubBrand>();
}

public class Brand
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }

    public ICollection<HubBrand> HubBrands { get; set; } = new List<HubBrand>();
    public ICollection<MenuItem> MenuItems { get; set; } = new List<MenuItem>();
}

public class HubBrand
{
    public int HubId { get; set; }
    public Hub Hub { get; set; } = null!;
    public int BrandId { get; set; }
    public Brand Brand { get; set; } = null!;
}

public class MenuItem
{
    public int Id { get; set; }
    public int BrandId { get; set; }
    public Brand Brand { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // R1: Noodles, Rice, Drinks, Dessert
    public decimal Price { get; set; } // VND
    public bool IsAvailable { get; set; } = true; // BR12
    public bool IsDeleted { get; set; }
}

public class Voucher
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public VoucherType Type { get; set; }
    public decimal Value { get; set; } // percent or fixed VND (BR9)
    public decimal? MaxDiscount { get; set; }
    public decimal MinSubtotal { get; set; } // checked against S (BR9)
    public DateOnly ValidFrom { get; set; }
    public DateOnly ValidTo { get; set; }
    public bool IsActive { get; set; } = true; // BR9
    public bool IsDeleted { get; set; }
}

// Orders are never deleted (no IsDeleted here).
public class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public User Customer { get; set; } = null!;
    public int HubId { get; set; }
    public Hub Hub { get; set; } = null!;
    public OrderStatus Status { get; set; } = OrderStatus.Placed; // changed only via OrderStateMachine (BR15)

    // Delivery destination for the shipper.
    public string DeliveryAddress { get; set; } = string.Empty;
    public string HotelName { get; set; } = string.Empty; // empty for local customers
    public string RoomNumber { get; set; } = string.Empty; // empty for local customers

    // BR17 snapshot: everything pricing depends on is frozen at placement.
    public DateTime PlacedAt { get; set; } // Vietnam local time (UTC+7)
    public decimal SnapshotHubLoad { get; set; } // BR8 fraction, e.g. 0.29
    public bool SnapshotHeavyRain { get; set; }
    public decimal SnapshotDistanceKm { get; set; }

    public int? VoucherId { get; set; }
    public Voucher? Voucher { get; set; }
    public string? VoucherNote { get; set; } // A-11: why an invalid voucher was ignored

    // BR10 price parts; Total is rounded once (BR11).
    public decimal Subtotal { get; set; }
    public decimal QuietDiscount { get; set; }
    public decimal VoucherDiscount { get; set; }
    public decimal FoodTotal { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal RushSurcharge { get; set; }
    public decimal RainSurcharge { get; set; }
    public decimal RawTotal { get; set; }
    public decimal Total { get; set; }

    public bool CashConfirmed { get; set; } // BR14: COD cash must be confirmed before Delivered
    public int? AssignedShipperId { get; set; } // mirrors the active ShipperAssignment (A-01)
    public User? AssignedShipper { get; set; }

    public ICollection<OrderLine> Lines { get; set; } = new List<OrderLine>();
    public ICollection<OrderStatusHistory> StatusHistory { get; set; } = new List<OrderStatusHistory>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<ShipperAssignment> ShipperAssignments { get; set; } = new List<ShipperAssignment>();
}

public class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int MenuItemId { get; set; }
    public MenuItem MenuItem { get; set; } = null!;
    public int Quantity { get; set; } // 1..20 (BR12)
    public decimal UnitPrice { get; set; } // price at order time, not current price (BR17)
}

public class OrderStatusHistory
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public OrderStatus? FromStatus { get; set; } // null for the initial null -> Placed row (BR15)
    public OrderStatus ToStatus { get; set; }
    public int ActorUserId { get; set; }
    public User Actor { get; set; } = null!;
    public DateTime ChangedAt { get; set; } // R2: Confirmed -> Ready timing
    public string? Reason { get; set; } // BR16 cancellations and Failed need a reason
}

public class Payment
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public PaymentMethod Method { get; set; } // BR13
    public decimal Amount { get; set; } // rounded order total (A-13)
    public PaymentStatus Status { get; set; }
    public bool CashConfirmed { get; set; } // BR14: mirrors Order.CashConfirmed for COD
    public string Reference { get; set; } = string.Empty;
    public string? Message { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RefundedAt { get; set; } // BR16 full refund simulation
    public decimal RefundedAmount { get; set; } // A-02: online keeps delivery fee; COD refunds 0
}

public class ShipperAssignment
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int ShipperId { get; set; }
    public User Shipper { get; set; } = null!;
    public DateTime AssignedAt { get; set; }
    public DateTime? UnassignedAt { get; set; } // null = current assignment (A-01)
}

// A-08/A-19: staged registration. No User/profile row exists until the simulated code is verified.
public class PendingRegistration
{
    public int Id { get; set; }
    public string Identifier { get; set; } = string.Empty; // normalised, unique across all users
    public Role Role { get; set; }                         // Local or Tourist only
    public string ProfileJson { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty; // random-salt ASP.NET Core hash
    public string Code { get; set; } = string.Empty;         // exposed in the result for the demo
    public DateTime ExpiresAt { get; set; }                  // Vietnam local time (UTC+7)
    public int Attempts { get; set; }
    public bool IsCancelled { get; set; }                    // 3 failures or expiry
    public DateTime CreatedAt { get; set; }
}

// FR-D14 / BR5: Admin-controlled heavy-rain flag, read at placement and stored in the BR17 snapshot.
public class SystemSetting
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty; // unique, e.g. "HeavyRain"
    public string Value { get; set; } = string.Empty; // "true" / "false"
    public DateTime UpdatedAt { get; set; } // Vietnam local time (UTC+7)
    public int UpdatedByUserId { get; set; }
    public User UpdatedByUser { get; set; } = null!;
}
