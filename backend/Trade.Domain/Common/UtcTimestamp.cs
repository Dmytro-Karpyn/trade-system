namespace Trade.Domain.Common;

public static class UtcTimestamp
{
    // PostgreSQL timestamptz stores microseconds; normalize before returning the first API response.
    public static DateTimeOffset Normalize(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}
