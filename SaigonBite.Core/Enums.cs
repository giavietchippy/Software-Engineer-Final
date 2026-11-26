namespace SaigonBite.Core;

/// <summary>Actor roles. Local and Tourist are the two customer types (one schema, BR18).</summary>
public enum Role { Local, Tourist, KitchenStaff, Shipper, Admin }

/// <summary>Order lifecycle states (BR15, section C of the specification).</summary>
public enum OrderStatus { Placed, Confirmed, Preparing, Ready, OutForDelivery, Delivered, Cancelled, Failed }

/// <summary>Payment methods (BR13).</summary>
public enum PaymentMethod { Momo, VnPay, ApplePay, PayPal, Cod }

public static class RoleExtensions
{
    public static bool IsCustomer(this Role role) => role is Role.Local or Role.Tourist;
}
