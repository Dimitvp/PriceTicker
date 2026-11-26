using System.Text.Json;
using static PriceTicker.Infrastructure.TwelveData.TwelveDataJson;

namespace PriceTicker.Infrastructure.TwelveData;

/// <summary>
/// Reads today's credits from an <c>/api_usage</c> payload. <c>daily_usage</c> and
/// <c>plan_daily_limit</c> are only sent on the free (Basic) plan; paid plans have no daily cap.
/// </summary>
internal static class TwelveDataUsageParser
{
    public static (int Used, int Limit)? Parse(JsonElement root)
    {
        if (IsError(root, out _)
            || !TryGetInt64(root, "daily_usage", out var used)
            || !TryGetInt64(root, "plan_daily_limit", out var limit)
            || used < 0
            || limit <= 0
            || limit > int.MaxValue)
        {
            return null;
        }

        return ((int)Math.Min(used, limit), (int)limit);
    }
}
