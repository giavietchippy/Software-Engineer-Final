using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SaigonBite.Web.Payments;

public sealed class MomoGateway
{
    private readonly HttpClient _http;
    private readonly MomoOptions _opt;
    private readonly PaymentGatewayOptions _root;

    public MomoGateway(HttpClient http, IOptions<PaymentGatewayOptions> options)
    {
        _http = http;
        _root = options.Value;
        _opt = options.Value.Momo;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_opt.PartnerCode)
        && !string.IsNullOrWhiteSpace(_opt.AccessKey)
        && !string.IsNullOrWhiteSpace(_opt.SecretKey);

    public async Task<MomoCreateResult> CreatePaymentAsync(
        string transactionRef, decimal amountVnd, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("MoMo sandbox credentials are not configured.");

        var amount = (long)amountVnd;
        var redirectUrl = !string.IsNullOrWhiteSpace(_opt.RedirectUrlOverride)
            ? _opt.RedirectUrlOverride.Trim()
            : $"{_root.PublicBaseUrl.TrimEnd('/')}{_opt.ReturnPath}";
        var ipnUrl = !string.IsNullOrWhiteSpace(_opt.IpnUrlOverride)
            ? _opt.IpnUrlOverride.Trim()
            : $"{_root.PublicBaseUrl.TrimEnd('/')}{_opt.IpnPath}";
        if (Uri.TryCreate(redirectUrl, UriKind.Absolute, out var returnUri) && returnUri.IsLoopback)
        {
            throw new InvalidOperationException(
                "MoMo needs public HTTPS callback URLs. Set Payments:PublicBaseUrl to your ngrok URL, " +
                "or set Payments:Momo:RedirectUrlOverride and IpnUrlOverride. See scripts/LIVE-PAYMENTS.txt");
        }

        var orderId = $"{_opt.PartnerCode}{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var requestId = orderId;
        const string extraData = "";
        var orderInfo = $"SaigonBite {transactionRef}";
        const string requestType = "captureWallet";

        var raw = new StringBuilder()
            .Append($"accessKey={_opt.AccessKey}")
            .Append($"&amount={amount}")
            .Append($"&extraData={extraData}")
            .Append($"&ipnUrl={ipnUrl}")
            .Append($"&orderId={orderId}")
            .Append($"&orderInfo={orderInfo}")
            .Append($"&partnerCode={_opt.PartnerCode}")
            .Append($"&redirectUrl={redirectUrl}")
            .Append($"&requestId={requestId}")
            .Append($"&requestType={requestType}");

        var signature = HmacSha256(_opt.SecretKey, raw.ToString());

        var body = new
        {
            partnerCode = _opt.PartnerCode,
            partnerName = "SaigonBite",
            storeId = "SaigonBiteStore",
            accessKey = _opt.AccessKey,
            requestId,
            amount,
            orderId,
            orderInfo,
            redirectUrl,
            ipnUrl,
            extraData,
            requestType,
            signature,
            lang = "vi"
        };

        using var res = await _http.PostAsJsonAsync(_opt.CreateUrl, body, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            var hint = res.StatusCode == System.Net.HttpStatusCode.Forbidden
                ? " HTTP 403: wrong Test keys, account not verified on developers.momo.vn, or redirectUrl/ipnUrl not public HTTPS."
                : string.Empty;
            throw new InvalidOperationException(
                $"MoMo create payment failed ({(int)res.StatusCode} {res.StatusCode}).{hint} Body: {json}");
        }

        using var doc = JsonDocument.Parse(json);
        var resultCode = doc.RootElement.GetProperty("resultCode").GetInt32();
        if (resultCode != 0)
        {
            var message = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
            throw new InvalidOperationException(message ?? $"MoMo declined (code {resultCode}).");
        }

        var payUrl = doc.RootElement.GetProperty("payUrl").GetString()
            ?? throw new InvalidOperationException("MoMo response missing payUrl.");
        return new MomoCreateResult(orderId, payUrl);
    }

    public MomoReturnValidation ValidateReturn(IQueryCollection query)
    {
        if (!query.TryGetValue("orderId", out var orderId) || string.IsNullOrWhiteSpace(orderId))
            return MomoReturnValidation.Fail("Missing MoMo order id.");
        if (!query.TryGetValue("resultCode", out var code) || code != "0")
            return MomoReturnValidation.Fail($"MoMo payment failed (code {code}).");

        if (!query.TryGetValue("amount", out var amountRaw)
            || !long.TryParse(amountRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
            return MomoReturnValidation.Fail("Invalid MoMo amount.");

        if (!query.TryGetValue("signature", out var signature) || string.IsNullOrEmpty(signature))
            return MomoReturnValidation.Fail("Missing MoMo signature.");

        var raw = new StringBuilder()
            .Append($"accessKey={_opt.AccessKey}")
            .Append($"&amount={amount}")
            .Append($"&extraData=")
            .Append($"&message={query["message"]}")
            .Append($"&orderId={orderId}")
            .Append($"&orderInfo={query["orderInfo"]}")
            .Append($"&orderType={query["orderType"]}")
            .Append($"&partnerCode={_opt.PartnerCode}")
            .Append($"&payType={query["payType"]}")
            .Append($"&requestId={query["requestId"]}")
            .Append($"&responseTime={query["responseTime"]}")
            .Append($"&resultCode={code}")
            .Append($"&transId={query["transId"]}");

        var expected = HmacSha256(_opt.SecretKey, raw.ToString());
        if (!string.Equals(expected, signature.ToString(), StringComparison.OrdinalIgnoreCase))
            return MomoReturnValidation.Fail("Invalid MoMo signature.");

        return MomoReturnValidation.Ok(orderId!, amount);
    }

    private static string HmacSha256(string key, string data)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
    }
}

public sealed record MomoCreateResult(string OrderId, string PayUrl);

public sealed record MomoReturnValidation(bool Success, string? Error, string? TransactionRef, decimal Amount)
{
    public static MomoReturnValidation Fail(string error) => new(false, error, null, 0m);
    public static MomoReturnValidation Ok(string txnRef, long amount) => new(true, null, txnRef, amount);
}