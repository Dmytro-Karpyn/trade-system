namespace Trade.Domain.Common;

public sealed class DomainException(string message) : Exception(message);

public static class Guard
{
    public static void Against(bool condition, string message)
    {
        if (condition) throw new DomainException(message);
    }

    public static void RequiredId(Guid value, string name) => Against(value == Guid.Empty, $"{name} is required.");

    public static void Numeric(decimal value, int scale, decimal exclusiveLimit, string name)
    {
        Against(decimal.Round(value, scale) != value || Math.Abs(value) >= exclusiveLimit,
            $"{name} exceeds the supported precision or scale.");
    }
}
