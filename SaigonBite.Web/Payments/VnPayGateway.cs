using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace SaigonBite.Web.Payments;

public sealed class VnPayGateway
{
    private readonly PaymentGatewayOptions _root;
    private readonly VnPayOptions _vn;

    public VnPayGateway(IOptions<PaymentGatewayOptions> options)
    {
        _root = options.Value;
        _vn = options.Value.VnPay;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_vn.TmnCode) && !string.IsNullOrWhiteSpace(_vn.HashSecret);

    public string BuildPaymentUrl(string transactionRef, decimal amountVnd, DateTime createTimeLocal)
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                "VNPay chua co TmnCode/HashSecret. Dang ky mien phi: https://sandbox.vnpayment.vn/devreg/ (email nhan key trong vai phut).");

        var amount = (long)(amountVnd * 100m);
        var create = createTimeLocal.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var expire = createTimeLocal.AddMinutes(15).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var orderInfo = $"Thanh toan don hang {transactionRef}";
        orderInfo = Regex.Replace(orderInfo, @"[^\w\s\-]", string.Empty);

        var data = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Amount"] = amount.ToString(CultureInfo.InvariantCulture),
            ["vnp_Command"] = _vn.Command,
            ["vnp_CreateDate"] = create,
            ["vnp_CurrCode"] = "VND",
            ["vnp_ExpireDate"] = expire,
            ["vnp_IpAddr"] = "127.0.0.1",
            ["vnp_Locale"] = _vn.Locale,
            ["vnp_OrderInfo"] = orderInfo,
            ["vnp_OrderType"] = "other",
            ["vnp_ReturnUrl"] = ReturnUrl(),
            ["vnp_TmnCode"] = _vn.TmnCode.Trim(),
            ["vnp_TxnRef"] = transactionRef,
            ["vnp_Version"] = _vn.Version
        };

        var hashData = string.Join('&',
            data.Select(kv => $"{WebUtility.UrlEncode(kv.Key)}={WebUtility.UrlEncode(kv.Value)}"));
        var secureHash = HmacSha512(_vn.HashSecret.Trim(), hashData);
        return $"{_vn.PaymentUrl.TrimEnd('/')}?{hashData}&vnp_SecureHash={secureHash}";
    }

    public VnPayReturnValidation ValidateReturn(IQueryCollection query)
    {
        if (!IsConfigured)
            return VnPayReturnValidation.Fail("VNPay is not configured.");

        var dict = query.Where(kv => kv.Key.StartsWith("vnp_", StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToString(), StringComparer.Ordinal);
        if (!dict.TryGetValue("vnp_SecureHash", out var receivedHash) || string.IsNullOrEmpty(receivedHash))
            return VnPayReturnValidation.Fail("Missing VNPay signature.");

        dict.Remove("vnp_SecureHash");
        dict.Remove("vnp_SecureHashType");

        var signData = string.Join('&',
            dict.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{WebUtility.UrlEncode(kv.Key)}={WebUtility.UrlEncode(kv.Value)}"));
        var expected = HmacSha512(_vn.HashSecret.Trim(), signData);
        if (!string.Equals(expected, receivedHash, StringComparison.OrdinalIgnoreCase))
            return VnPayReturnValidation.Fail("Invalid VNPay signature.");

        if (!dict.TryGetValue("vnp_ResponseCode", out var code) || code != "00")
            return VnPayReturnValidation.Fail($"VNPay declined (response {code ?? "?"}).");

        if (!dict.TryGetValue("vnp_TxnRef", out var txnRef) || string.IsNullOrWhiteSpace(txnRef))
            return VnPayReturnValidation.Fail("Missing VNPay transaction reference.");

        if (!dict.TryGetValue("vnp_Amount", out var amountRaw)
            || !long.TryParse(amountRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amountCents))
            return VnPayReturnValidation.Fail("Invalid VNPay amount.");

        return VnPayReturnValidation.Ok(txnRef, amountCents / 100m);
    }

    private string ReturnUrl() =>
        $"{_root.PublicBaseUrl.TrimEnd('/')}{_vn.ReturnPath}";

    private static string HmacSha512(string key, string data)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
    }
}

public sealed record VnPayReturnValidation(bool Success, string? Error, string? TransactionRef, decimal Amount)
{
    public static VnPayReturnValidation Fail(string error) => new(false, error, null, 0m);
    public static VnPayReturnValidation Ok(string txnRef, decimal amount) => new(true, null, txnRef, amount);
}