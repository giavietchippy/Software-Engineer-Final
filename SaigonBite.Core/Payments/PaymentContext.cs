namespace SaigonBite.Core.Payments;

public sealed class PaymentNotAllowedException : Exception
{
    public PaymentNotAllowedException(string message) : base(message) { }
}

/// <summary>
/// Picks the strategy for the chosen method and customer group.
/// New methods are added by registering one more IPaymentStrategy: this class is not edited.
/// </summary>
public sealed class PaymentContext
{
    private readonly Dictionary<PaymentMethod, IPaymentStrategy> _strategies;

    public PaymentContext(IEnumerable<IPaymentStrategy> strategies)
    {
        _strategies = strategies.ToDictionary(s => s.Method);
    }

    /// <summary>Methods to show at checkout for this payer and total.</summary>
    public IReadOnlyList<PaymentMethod> AllowedMethods(Role payer, decimal roundedTotal) =>
        _strategies.Values
            .Where(s => s.IsAllowedFor(payer) && s.CheckAmount(roundedTotal) is null)
            .Select(s => s.Method)
            .OrderBy(m => m)
            .ToList();

    public IPaymentStrategy Resolve(PaymentMethod method, Role payer, decimal roundedTotal)
    {
        if (!_strategies.TryGetValue(method, out var strategy))
            throw new PaymentNotAllowedException($"{method} is not available.");
        if (!strategy.IsAllowedFor(payer))
            throw new PaymentNotAllowedException($"{method} cannot be used by this customer type (BR13).");
        var error = strategy.CheckAmount(roundedTotal);
        if (error is not null)
            throw new PaymentNotAllowedException(error);
        return strategy;
    }
}
