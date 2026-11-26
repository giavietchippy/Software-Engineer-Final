namespace SaigonBite.Core.Orders;

public sealed record OrderLineInfo(int Quantity, bool IsAvailable, bool BrandOperatedByHub);

/// <summary>BR12: 1 to 30 lines, quantity 1 to 20, only Available items of brands the hub operates.</summary>
public static class OrderLineValidator
{
    public const int MinQuantity = 1, MaxQuantity = 20, MinLines = 1, MaxLines = 30;

    /// <returns>List of error messages. Empty means the lines are valid.</returns>
    public static IReadOnlyList<string> Validate(IReadOnlyList<OrderLineInfo> lines)
    {
        var errors = new List<string>();
        if (lines.Count < MinLines || lines.Count > MaxLines)
            errors.Add($"An order must have {MinLines} to {MaxLines} lines.");

        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            if (l.Quantity < MinQuantity || l.Quantity > MaxQuantity)
                errors.Add($"Line {i + 1}: quantity must be {MinQuantity} to {MaxQuantity}.");
            if (!l.IsAvailable)
                errors.Add($"Line {i + 1}: item is not available.");
            if (!l.BrandOperatedByHub)
                errors.Add($"Line {i + 1}: item does not belong to a brand of this hub.");
        }
        return errors;
    }
}
