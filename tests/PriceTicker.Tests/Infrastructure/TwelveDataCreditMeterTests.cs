using Microsoft.Extensions.Time.Testing;
using PriceTicker.Infrastructure.TwelveData;

namespace PriceTicker.Tests.Infrastructure;

public sealed class TwelveDataCreditMeterTests
{
    private static readonly DateTimeOffset Morning = new(2026, 11, 26, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryReserve_BeyondEightInAMinute_IsRefusedUntilTheMinutePasses()
    {
        var clock = new FakeTimeProvider(Morning);
        var meter = new TwelveDataCreditMeter(clock);

        Assert.Equal(CreditCheck.Reserved, meter.TryReserve(8));
        Assert.Equal(CreditCheck.MinuteLimit, meter.TryReserve(1));

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(CreditCheck.Reserved, meter.TryReserve(1));
    }

    [Fact]
    public void TryReserve_WhenTheDayIsSpent_IsRefused()
    {
        var meter = new TwelveDataCreditMeter(new FakeTimeProvider(Morning));
        meter.Synchronise(usedToday: 800, dailyLimit: 800);

        Assert.Equal(CreditCheck.DailyLimit, meter.TryReserve(1));
    }

    [Fact]
    public void Snapshot_CountsReservedCredits_AndStartsAgainAtMidnightUtc()
    {
        var clock = new FakeTimeProvider(Morning);
        var meter = new TwelveDataCreditMeter(clock);
        meter.Synchronise(usedToday: 100, dailyLimit: 800);
        Assert.Equal(CreditCheck.Reserved, meter.TryReserve(2));

        var today = meter.Snapshot();
        Assert.Equal(102, today.Used);
        Assert.Equal(698, today.Left);
        Assert.Equal(new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.Zero), today.ResetsAt);

        clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, meter.Snapshot().Used);
    }

    [Fact]
    public void NeedsSynchronising_AfterAnHourOrOnANewDay()
    {
        var clock = new FakeTimeProvider(Morning);
        var meter = new TwelveDataCreditMeter(clock);
        Assert.True(meter.NeedsSynchronising(TimeSpan.FromHours(1)));

        meter.Synchronise(10, 800);
        clock.Advance(TimeSpan.FromMinutes(59));
        Assert.False(meter.NeedsSynchronising(TimeSpan.FromHours(1)));

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(meter.NeedsSynchronising(TimeSpan.FromHours(1)));
    }
}
