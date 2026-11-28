namespace SaigonBite.Web.Payments;

public sealed class PaymentGatewayOptions
{
    public const string Section = "Payments";

    /// <summary>Mock = instant in-process gateway (default, tests). Live = real sandbox HTTP.</summary>
    public string Mode { get; set; } = "Mock";

  /// <summary>Public site root used in provider return URLs (e.g. https://localhost:7122).</summary>
    public string PublicBaseUrl { get; set; } = "https://localhost:7122";

    public VnPayOptions VnPay { get; set; } = new();
    public MomoOptions Momo { get; set; } = new();
    public PayPalOptions PayPal { get; set; } = new();
}

public sealed class VnPayOptions
{
    public string TmnCode { get; set; } = string.Empty;
    public string HashSecret { get; set; } = string.Empty;
    public string PaymentUrl { get; set; } = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";
    public string ReturnPath { get; set; } = "/Payment/VnPayReturn";
    public string Version { get; set; } = "2.1.0";
    public string Command { get; set; } = "pay";
    public string Locale { get; set; } = "vn";
}

public sealed class MomoOptions
{
    public string PartnerCode { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string CreateUrl { get; set; } = "https://test-payment.momo.vn/v2/gateway/api/create";
    public string ReturnPath { get; set; } = "/Payment/MomoReturn";
    public string IpnPath { get; set; } = "/Payment/MomoIpn";
    /// <summary>Optional full HTTPS URLs (e.g. ngrok). When set, overrides PublicBaseUrl + path.</summary>
    public string? RedirectUrlOverride { get; set; }
    public string? IpnUrlOverride { get; set; }
}

public sealed class PayPalOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = "https://api-m.sandbox.paypal.com";
    public string ReturnPath { get; set; } = "/Payment/PayPalReturn";
    public string CancelPath { get; set; } = "/Payment/PayPalCancel";
    public string Currency { get; set; } = "USD";
}