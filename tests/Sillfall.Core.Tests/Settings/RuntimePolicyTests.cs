using Sillfall.Core.Settings;

namespace Sillfall.Core.Tests.Settings;

public class RuntimePolicyTests
{
    [Theory]
    [InlineData(Smoothness.Auto, false, 60)]
    [InlineData(Smoothness.Auto, true, 30)]
    [InlineData(Smoothness.LeastEnergy, false, 30)]
    [InlineData(Smoothness.Balanced, false, 60)]
    [InlineData(Smoothness.Smoothest, false, 0)]
    public void Smoothness_SetsTheFrameRate(Smoothness smoothness, bool onBattery, int expected)
    {
        var settings = new AppSettings { Performance = new PerformanceConfig { Smoothness = smoothness, Battery = BatteryBehavior.Nothing } };

        RuntimePolicy.Decide(settings, SystemState.Default with { OnBattery = onBattery }).FrameRateCap.Should().Be(expected);
    }

    [Fact]
    public void Battery_Reduce_HalvesTheIntensityAndCapsTheFrameRate()
    {
        var settings = new AppSettings { Performance = new PerformanceConfig { Smoothness = Smoothness.Smoothest, Battery = BatteryBehavior.Reduce } };

        RuntimeDecision onMains = RuntimePolicy.Decide(settings, SystemState.Default);
        RuntimeDecision onBattery = RuntimePolicy.Decide(settings, SystemState.Default with { OnBattery = true });

        onMains.Should().Be(new RuntimeDecision(false, null, 0, 1f));
        onBattery.Should().Be(new RuntimeDecision(false, null, 30, 0.5f));
    }

    [Fact]
    public void Battery_Pause_PausesOnlyOnBattery()
    {
        var settings = new AppSettings { Performance = new PerformanceConfig { Battery = BatteryBehavior.Pause } };

        RuntimePolicy.Decide(settings, SystemState.Default).Paused.Should().BeFalse();
        RuntimeDecision decision = RuntimePolicy.Decide(settings, SystemState.Default with { OnBattery = true });
        decision.Paused.Should().BeTrue();
        decision.Reason.Should().Contain("battery");
    }

    [Theory]
    [InlineData(ReducedMotion.FollowSystem, false, true)]
    [InlineData(ReducedMotion.FollowSystem, true, false)]
    [InlineData(ReducedMotion.Always, true, true)]
    [InlineData(ReducedMotion.Never, false, false)]
    public void ReducedMotion_PausesAsConfigured(ReducedMotion mode, bool animationsEnabled, bool paused)
    {
        var settings = new AppSettings { Advanced = new AdvancedConfig { ReducedMotion = mode } };

        RuntimePolicy.Decide(settings, SystemState.Default with { AnimationsEnabled = animationsEnabled }).Paused.Should().Be(paused);
    }

    [Fact]
    public void AppRule_PausesWhileTheProcessIsInTheForeground_IgnoringCase()
    {
        var settings = new AppSettings
        {
            AppRules =
            [
                new AppRule { ProcessName = "Game.exe", Action = AppRuleAction.PauseWhenForeground },
                new AppRule { ProcessName = "obs64.exe", Action = AppRuleAction.HideOverWindows },
            ],
        };

        RuntimePolicy.Decide(settings, SystemState.Default with { ForegroundProcess = "game.EXE" }).Reason.Should().Be("Paused while Game.exe is in the foreground");
        RuntimePolicy.Decide(settings, SystemState.Default with { ForegroundProcess = "obs64.exe" }).Paused.Should().BeFalse("hiding over windows is not pausing");
        RuntimePolicy.Decide(settings, SystemState.Default with { ForegroundProcess = "notepad.exe" }).Paused.Should().BeFalse();
    }

    [Fact]
    public void MasterSwitch_PausesFirst()
    {
        var settings = new AppSettings { Enabled = false };

        RuntimePolicy.Decide(settings, SystemState.Default).Should().Be(new RuntimeDecision(true, "Paused", 60, 1f));
    }

    [Fact]
    public void Decide_HighContrast_PausesUnlessSwitchedOff()
    {
        var state = SystemState.Default with { HighContrast = true };

        RuntimePolicy.Decide(new AppSettings(), state).Reason.Should().Be("Paused: high contrast is on");
        RuntimePolicy.Decide(new AppSettings { Advanced = new AdvancedConfig { PauseInHighContrast = false } }, state).Paused.Should().BeFalse();
    }

    [Theory]
    [InlineData("Locked", "Paused while the PC is locked or asleep")]
    [InlineData("FullScreen", "Paused while an app runs full screen")]
    [InlineData("Presentation", "Paused during a presentation")]
    [InlineData("RemoteSession", "Paused in a Remote Desktop session")]
    public void Decide_SystemStates_Pause(string what, string reason)
    {
        SystemState state = what switch
        {
            "Locked" => SystemState.Default with { Locked = true },
            "FullScreen" => SystemState.Default with { FullScreen = true },
            "Presentation" => SystemState.Default with { Presentation = true },
            _ => SystemState.Default with { RemoteSession = true },
        };

        RuntimeDecision decision = RuntimePolicy.Decide(new AppSettings(), state);

        decision.Paused.Should().BeTrue();
        decision.Reason.Should().Be(reason);
    }

    [Fact]
    public void Decide_LockedOrFullScreen_PauseEvenWithReducedMotionNever()
    {
        var settings = new AppSettings { Advanced = new AdvancedConfig { ReducedMotion = ReducedMotion.Never } };

        RuntimePolicy.Decide(settings, SystemState.Default with { FullScreen = true }).Paused.Should().BeTrue();
        RuntimePolicy.Decide(settings, SystemState.Default with { Locked = true }).Paused.Should().BeTrue();
    }

    [Fact]
    public void Decide_RemoteSession_RunsWhenAllowed()
    {
        var settings = new AppSettings { Advanced = new AdvancedConfig { PauseInRemoteSession = false } };

        RuntimePolicy.Decide(settings, SystemState.Default with { RemoteSession = true }).Paused.Should().BeFalse();
    }

    [Theory]
    [InlineData("StartMenuExperienceHost.exe", true)]
    [InlineData("searchhost.EXE", true)]
    [InlineData("ShellExperienceHost.exe", true)]
    [InlineData("explorer.exe", false)]
    [InlineData("notepad.exe", false)]
    public void Decide_StartOrSearchInFront_StepsAside(string process, bool paused)
    {
        RuntimeDecision decision = RuntimePolicy.Decide(new AppSettings(), SystemState.Default with { ForegroundProcess = process });

        decision.Paused.Should().Be(paused);
        if (paused)
        {
            decision.Reason.Should().Be("Hidden while Start, search or notifications are open");
        }
    }

    [Fact]
    public void Decide_StartInFront_StaysWhenSwitchedOff()
    {
        var settings = new AppSettings { Advanced = new AdvancedConfig { HideOverShell = false } };

        RuntimePolicy.Decide(settings, SystemState.Default with { ForegroundProcess = "SearchHost.exe" }).Paused.Should().BeFalse();
    }
}
