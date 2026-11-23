using Microsoft.Extensions.Time.Testing;
using PriceTicker.App.Configuration;
using PriceTicker.Tests.TestDoubles;

namespace PriceTicker.Tests.App;

public sealed class TickerViewModelGroupTests
{
    [Fact]
    public void Groups_FollowTheConfiguredOrderWithUngroupedRowsLastAndUntitled()
    {
        GroupOptions[] groups =
        [
            new() { Title = "Core", Note = "monthly DCA", Instruments = ["b", "A"] },
            new() { Title = "Themes", Instruments = ["d"] },
        ];

        var viewModel = Create(groups, "a", "b", "c", "d");

        Assert.Collection(
            viewModel.Groups,
            core =>
            {
                Assert.Equal(("Core", "monthly DCA", true), (core.Title, core.Note, core.HasNote));
                Assert.Equal(["b", "a"], core.Items.Select(item => item.InstrumentId));
            },
            themes =>
            {
                Assert.False(themes.HasNote);
                Assert.Equal(["d"], themes.Items.Select(item => item.InstrumentId));
            },
            rest =>
            {
                Assert.False(rest.HasHeading);
                Assert.Equal(["c"], rest.Items.Select(item => item.InstrumentId));
            });
    }

    [Fact]
    public void Groups_WhenNoneConfigured_ShowAllRowsInOneUntitledGroup()
    {
        var viewModel = Create([], "a", "b");

        var only = Assert.Single(viewModel.Groups);
        Assert.False(only.HasHeading);
        Assert.Same(viewModel.Items[0], only.Items[0]);
        Assert.Equal(2, only.Items.Count);
    }

    private static PriceTicker.App.ViewModels.TickerViewModel Create(IList<GroupOptions> groups, params string[] ids) =>
        TestViewModels.Create(FakeQuoteService.PricingEverythingAt(1m), new FakeTimeProvider(), groups: groups, ids: ids);
}
