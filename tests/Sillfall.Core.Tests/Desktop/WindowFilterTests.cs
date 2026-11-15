using Sillfall.Core.Desktop;

namespace Sillfall.Core.Tests.Desktop;

public class WindowFilterTests
{
    [Fact]
    public void NormalWindow_IsTracked()
    {
        WindowFilter.IsTracked(new WindowFacts(Visible: true, Minimized: false, Cloaked: false, ExtendedStyle: 0x0000_0100)).Should().BeTrue();
    }

    [Fact]
    public void LayeredWindowOfAModernApp_IsTracked()
    {
        // The Claude desktop app's main window: WS_EX_LAYERED | WS_EX_NOREDIRECTIONBITMAP | WS_EX_WINDOWEDGE.
        WindowFilter.IsTracked(new WindowFacts(true, false, false, 0x0028_0100)).Should().BeTrue();
    }

    [Theory]
    [InlineData(0x0828_0028u)] // F-21: another app's screen layer, layered | transparent | topmost | no-activate | no redirection bitmap
    [InlineData(0x0828_00A8u)] // Sillfall's own overlay (also a tool window)
    [InlineData(0x0008_0020u)] // plain layered | transparent
    public void ClickThroughOverlay_IsNotTracked(uint style)
    {
        WindowFilter.IsTracked(new WindowFacts(true, false, false, style)).Should().BeFalse();
    }

    [Fact]
    public void FullyTransparentLayeredWindow_IsNotTracked()
    {
        WindowFilter.IsTracked(new WindowFacts(true, false, false, WindowFilter.Layered, LayeredAlpha: 0)).Should().BeFalse();
        WindowFilter.IsTracked(new WindowFacts(true, false, false, WindowFilter.Layered, LayeredAlpha: 40)).Should().BeTrue();
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void HiddenMinimizedOrCloaked_IsNotTracked(bool visible, bool minimized, bool cloaked)
    {
        WindowFilter.IsTracked(new WindowFacts(visible, minimized, cloaked, 0)).Should().BeFalse();
    }

    [Fact]
    public void ToolWindow_IsNotTracked()
    {
        WindowFilter.IsTracked(new WindowFacts(true, false, false, WindowFilter.ToolWindow)).Should().BeFalse();
    }
}
