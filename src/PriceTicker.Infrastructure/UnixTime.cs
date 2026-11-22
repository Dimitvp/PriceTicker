namespace PriceTicker.Infrastructure;

internal static class UnixTime
{
    // DateTimeOffset.MaxValue in Unix seconds; anything outside [0, max] is not a real quote time.
    private const long MaxUnixSeconds = 253_402_300_799;

    public static bool TryFromSeconds(long unixSeconds, out DateTimeOffset value)
    {
        if (unixSeconds is < 0 or > MaxUnixSeconds)
        {
            value = default;
            return false;
        }

        value = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        return true;
    }
}
