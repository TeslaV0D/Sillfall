using Weatherly.Core.Effects;
using Weatherly.Core.Overlay;
using Weatherly.Core.Tray;

namespace Weatherly.Core.Tests.Tray;

public class TrayMenuTests
{
    [Fact]
    public void Build_ChecksTheCurrentStepOfEveryEffect()
    {
        var options = new OverlayOptions { Rain = new EffectSettings { Intensity = 0.6f }, Snow = new EffectSettings { Intensity = 0f } };

        IReadOnlyList<TrayMenuItem> menu = TrayMenu.Build(options, soundOn: false, Commands(new List<string>()));

        TrayMenuItem rain = menu.Single(item => item.Text.StartsWith("Rain", StringComparison.Ordinal));
        rain.Text.Should().Be("Rain (60 %)");
        rain.Children!.Single(item => item.IsChecked).Text.Should().Be("Medium");
        rain.Children!.Should().OnlyContain(item => item.IsRadio);
        TrayMenuItem snow = menu.Single(item => item.Text.StartsWith("Snow", StringComparison.Ordinal));
        snow.Text.Should().Be("Snow");
        snow.Children!.Single(item => item.IsChecked).Text.Should().Be("Off");
    }

    [Fact]
    public void Build_AValueBetweenTheSteps_ChecksNoneButNamesIt()
    {
        var options = new OverlayOptions { Snow = new EffectSettings { Intensity = 0.45f } };

        TrayMenuItem snow = TrayMenu.Build(options, soundOn: false, Commands(new List<string>())).Single(item => item.Text.StartsWith("Snow", StringComparison.Ordinal));

        snow.Text.Should().Be("Snow (45 %)");
        snow.Children!.Should().NotContain(item => item.IsChecked);
    }

    [Fact]
    public void Items_RunTheirCommands()
    {
        var calls = new List<string>();
        IReadOnlyList<TrayMenuItem> menu = TrayMenu.Build(new OverlayOptions(), soundOn: true, Commands(calls));

        menu[0].Invoke!();
        menu.Single(item => item.Text.StartsWith("Rain", StringComparison.Ordinal)).Children!.Single(item => item.Text == "Heavy").Invoke!();
        menu.Single(item => item.Text.StartsWith("Snow", StringComparison.Ordinal)).Children!.Single(item => item.Text == "Light").Invoke!();
        menu.Single(item => item.Text.StartsWith("Leaves", StringComparison.Ordinal)).Children!.Single(item => item.Text == "Medium").Invoke!();
        menu.Single(item => item.Text == "Sound").Invoke!();
        menu.Single(item => item.Text == "Pause all effects").Invoke!();
        menu[^1].Invoke!();

        calls.Should().Equal("open", "rain 1", "snow 0.3", "leaves 0.6", "sound False", "paused True", "quit");
    }

    [Fact]
    public void Build_OpenIsTheDefaultAndQuitIsLast()
    {
        IReadOnlyList<TrayMenuItem> menu = TrayMenu.Build(new OverlayOptions(), soundOn: false, Commands(new List<string>()));

        menu[0].IsDefault.Should().BeTrue();
        menu[0].Text.Should().Be($"Open {AppInfo.Name}");
        menu[^1].Text.Should().Be("Quit");
        menu.Count(item => item.IsDefault).Should().Be(1);
    }

    [Fact]
    public void Build_WhilePaused_DisablesTheEffectsAndChecksPause()
    {
        IReadOnlyList<TrayMenuItem> menu = TrayMenu.Build(new OverlayOptions { Enabled = false }, soundOn: false, Commands(new List<string>()));

        menu.Single(item => item.Text == "Pause all effects").IsChecked.Should().BeTrue();
        menu.Where(item => item.Children is not null).Should().OnlyContain(item => !item.IsEnabled);
    }

    [Theory]
    [InlineData(true, 0f, 0f, "clear sky")]
    [InlineData(true, 0.3f, 0f, "rain 30 %")]
    [InlineData(true, 0.3f, 1f, "rain 30 %, snow 100 %")]
    [InlineData(false, 0.3f, 1f, "paused")]
    public void Tooltip_SaysWhatIsOn(bool enabled, float rain, float snow, string expected)
    {
        var options = new OverlayOptions { Enabled = enabled, Rain = new EffectSettings { Intensity = rain }, Snow = new EffectSettings { Intensity = snow } };

        TrayMenu.Tooltip(options).Should().Be($"{AppInfo.Name}: {expected}");
    }

    [Fact]
    public void Tooltip_NamesTheLeaves()
    {
        var options = new OverlayOptions { Leaves = new EffectSettings { Intensity = 0.6f } };

        TrayMenu.Tooltip(options).Should().Be($"{AppInfo.Name}: leaves 60 %");
    }

    private static TrayCommands Commands(List<string> calls) => new(
        Open: () => calls.Add("open"),
        SetRain: value => calls.Add($"rain {value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"),
        SetSnow: value => calls.Add($"snow {value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"),
        SetLeaves: value => calls.Add($"leaves {value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"),
        SetSound: on => calls.Add($"sound {on}"),
        SetPaused: paused => calls.Add($"paused {paused}"),
        Quit: () => calls.Add("quit"));
}
