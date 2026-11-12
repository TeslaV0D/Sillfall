using Sillfall.Core.Overlay;
using Sillfall.Core.Settings;

namespace Sillfall.Core.Tests.Settings;

public class RuntimeOptionsTests
{
    private static readonly float[] s_intensities = [0.8f, 0.4f, 0f, 0f, 0f, 1f];

    [Fact]
    public void Build_AppliesTheDecision()
    {
        var settings = new AppSettings
        {
            AppRules =
            [
                new AppRule { ProcessName = "obs64.exe", Action = AppRuleAction.HideOverWindows },
                new AppRule { ProcessName = "game.exe", Action = AppRuleAction.PauseWhenForeground },
            ],
            Displays = [new DisplayConfig { DeviceName = "A", Intensity = 0.5f }],
        };

        OverlayOptions options = RuntimeOptions.Build(settings, new OverlayOptions(), s_intensities, new RuntimeDecision(false, null, 30, 0.5f));

        options.Enabled.Should().BeTrue();
        options.Rain.Intensity.Should().Be(0.4f);
        options.Grass.Intensity.Should().Be(0.5f);
        options.FrameRateCap.Should().Be(30);
        options.HideOverProcesses.Should().Equal("obs64.exe");
        options.Display("A").Intensity.Should().Be(0.5f);
    }

    [Fact]
    public void Build_PausedOrSwitchedOff_DisablesTheOverlay()
    {
        var running = new RuntimeDecision(false, null, 60, 1f);
        var paused = new RuntimeDecision(true, "Paused while on battery", 60, 1f);

        RuntimeOptions.Build(new AppSettings(), new OverlayOptions(), s_intensities, paused).Enabled.Should().BeFalse();
        RuntimeOptions.Build(new AppSettings { Enabled = false }, new OverlayOptions(), s_intensities, running).Enabled.Should().BeFalse();
        RuntimeOptions.Build(new AppSettings(), new OverlayOptions(), s_intensities, running).Enabled.Should().BeTrue();
    }

    [Fact]
    public void Same_ComparesListsByContent()
    {
        var settings = new AppSettings { AppRules = [new AppRule { ProcessName = "obs64.exe", Action = AppRuleAction.HideOverWindows }] };
        var decision = new RuntimeDecision(false, null, 60, 1f);

        OverlayOptions first = RuntimeOptions.Build(settings, new OverlayOptions(), s_intensities, decision);
        OverlayOptions second = RuntimeOptions.Build(settings, new OverlayOptions(), s_intensities, decision);

        (first == second).Should().BeFalse("records compare the lists by reference");
        RuntimeOptions.Same(first, second).Should().BeTrue();
        RuntimeOptions.Same(first, second with { FrameRateCap = 30 }).Should().BeFalse();
        RuntimeOptions.Same(first, second with { HideOverProcesses = ["other.exe"] }).Should().BeFalse();
    }
}
