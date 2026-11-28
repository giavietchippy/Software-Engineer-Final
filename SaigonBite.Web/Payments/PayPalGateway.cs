using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SaigonBite.Web.Payments;

public sealed class PayPalGateway
{
    private readonly HttpClient _http;
    private readonly PayPalOptions _opt;
    private readonly PaymentGatewayOptions _root;

    public PayPalGateway(HttpClient http, IOptions<PaymentGatewayOptions> options)
    {
        _http = http;
        _root = options.Value;
        _opt = options.Value.PayPal;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_opt.ClientId) && !string.IsNullOrWhiteSpace(_opt.ClientSecret);

    public async Task<PayPalCreateResult> CreateCheckoutAsync(
        string transactionRef, decimal amountVnd, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("PayPal sandbox credentials are not configured.");

        var token = await GetAccessTokenAsync(ct);
        var value = amountVnd.ToString("0", CultureInfo.InvariantCulture);
        var body = new
        {
            intent = "CAPTURE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = transactionRef,
                    amount = new { currency_code = "VND", value }
                }
            },
            application_context = new
            {
                return_url = $"{_root.PublicBaseUrl.TrimEnd('/')}{_opt.ReturnPath}",
                cancel_url = $"{_root.PublicBaseUrl.TrimEnd('/')}{_opt.CancelPath}",
                brand_name = "SaigonBite",
                user_action = "PAY_NOW"
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_opt.ApiBaseUrl.TrimEnd('/')}/v2/checkout/orders")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var res = await _http.SendAsync(req, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"PayPal create order failed: {json}");

        using var doc = JsonDocument.Parse(json);
        var orderId = doc.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("PayPal response missing order id.");
        var approve = doc.RootElement.GetProperty("links").EnumerateArray()
            .FirstOrDefault(l => l.GetProperty("rel").GetString() == "approve")
            .GetProperty("href").GetString()
            ?? throw new InvalidOperationException("PayPal response missing approve link.");

        return new PayPalCreateResult(orderId, approve);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string orderId, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"{_opt.ApiBaseUrl.TrimEnd('/')}/v2/checkout/orders/{orderId}/capture");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var res = await _http.SendAsync(req, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            return PayPalCaptureResult.Fail($"PayPal capture failed: {json}");

        using var doc = JsonDocument.Parse(json);
        var status = doc.RootElement.GetProperty("status").GetString();
        if (!string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
            return PayPalCaptureResult.Fail($"PayPal status {status}");

        var captureId = doc.RootElement
            .GetProperty("purchase_units")[0]
            .GetProperty("payments")
            .GetProperty("captures")[0]
            .GetProperty("id").GetString() ?? orderId;

        return PayPalCaptureResult.Ok(captureId);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        var basic = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_opt.ClientId}:{_opt.ClientSecret}"));
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_opt.ApiBaseUrl.TrimEnd('/')}/v1/oauth2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        using var res = await _http.SendAsync(req, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"PayPal auth failed: {json}");

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("PayPal auth missing token.");
    }
}

public sealed record PayPalCreateResult(string OrderId, string ApproveUrl);

public sealed record PayPalCaptureResult(bool Success, string? Reference, string? Error)
{
    public static PayPalCaptureResult Ok(string reference) => new(true, reference, null);
    public static PayPalCaptureResult Fail(string error) => new(false, null, error);
}