using Weatherly.Core.Overlay;
using Weatherly.Core.Tray;

namespace Weatherly.Core.Tests.Tray;

public class TrayMenuVisitorTests
{
    [Fact]
    public void Build_WithAVisitorOnScreen_OffersToCollectIt()
    {
        bool collected = false;
        TrayCommands commands = Commands(() => collected = true, visitors: 1);

        TrayMenuItem item = TrayMenu.Build(new OverlayOptions(), soundOn: false, commands).Single(i => i.Text?.StartsWith("Collect", StringComparison.Ordinal) == true);
        item.Invoke!();

        item.Text.Should().Contain("Ctrl+Alt+V");
        collected.Should().BeTrue();
    }

    [Fact]
    public void Build_WithoutVisitors_HasNoCollectItem()
    {
        TrayMenu.Build(new OverlayOptions(), soundOn: false, Commands(() => { }, visitors: 0))
            .Should().NotContain(i => i.Text != null && i.Text.StartsWith("Collect", StringComparison.Ordinal));
    }

    private static TrayCommands Commands(Action collect, int visitors) =>
        new(() => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, () => { }, collect, visitors);
}
