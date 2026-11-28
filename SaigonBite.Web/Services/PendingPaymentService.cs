using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SaigonBite.Core;
using SaigonBite.Core.Payments;
using SaigonBite.Web.Data;

namespace SaigonBite.Web.Services;

public sealed class PendingPaymentService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly SaigonBiteDbContext _db;
    private readonly IClock _clock;

    public PendingPaymentService(SaigonBiteDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task StageAsync(
        string transactionRef,
        PaymentMethod method,
        int customerId,
        PendingCheckoutPayload payload,
        string? providerReference = null,
        CancellationToken ct = default)
    {
        var now = VietnamTime.FromUtc(_clock.UtcNow);
        _db.PendingOnlinePayments.Add(new PendingOnlinePayment
        {
            TransactionRef = transactionRef,
            Method = method,
            CustomerId = customerId,
            PayloadJson = JsonSerializer.Serialize(payload, Json),
            ProviderReference = providerReference,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(30),
            Completed = false
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<PendingOnlinePayment?> FindOpenAsync(string transactionRef, CancellationToken ct = default) =>
        await _db.PendingOnlinePayments.AsNoTracking()
            .SingleOrDefaultAsync(p => p.TransactionRef == transactionRef && !p.Completed, ct);

    public async Task<PendingCheckoutPayload?> ReadPayloadAsync(PendingOnlinePayment pending) =>
        JsonSerializer.Deserialize<PendingCheckoutPayload>(pending.PayloadJson, Json);

    public async Task MarkCompletedAsync(string transactionRef, CancellationToken ct = default)
    {
        var row = await _db.PendingOnlinePayments
            .SingleOrDefaultAsync(p => p.TransactionRef == transactionRef, ct);
        if (row is null) return;
        row.Completed = true;
        await _db.SaveChangesAsync(ct);
    }
}