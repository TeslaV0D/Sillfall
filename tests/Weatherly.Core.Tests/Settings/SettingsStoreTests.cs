using Weatherly.Core.Effects;
using Weatherly.Core.Effects.Sun;
using Weatherly.Core.Overlay;
using Weatherly.Core.Settings;

namespace Weatherly.Core.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"weatherly-settings-{Guid.NewGuid():N}");

    private string FilePath => Path.Combine(_directory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Load_WithoutAFile_GivesTheDefaults()
    {
        var store = new SettingsStore(FilePath);

        AppSettings settings = store.Load();

        settings.Should().BeEquivalentTo(new AppSettings());
        settings.Sound.Muted.Should().BeTrue("sound is off by default (PLAN.md, section 8)");
        store.LastError.Should().BeNull();
        store.LoadedVersion.Should().Be(0);
    }

    [Fact]
    public void SaveThenLoad_GivesTheSameSettings()
    {
        var store = new SettingsStore(Path.Combine(_directory, "nested", "settings.json"));
        AppSettings settings = new AppSettings
        {
            Enabled = false,
            Rain = new EffectConfig { Intensity = 0.6f, Color = "#FF8800", Brightness = 1.5f, Mode = IntensityMode.Random, RandomMin = 0.2f, RandomMax = 0.9f, RandomMinutes = 30f },
            Snow = new EffectConfig { Intensity = 0.3f },
            RainDepth = 0.8f,
            SunSide = SunSide.Left,
            Wind = new WindConfig { Speed = -120f, Gustiness = 0.2f },
            Sound = new SoundConfig { Muted = false, Master = 0.5f },
            Displays = [new DisplayConfig { DeviceName = @"\\.\DISPLAY2", Enabled = false, Intensity = 0.4f }],
            AppRules = [new AppRule { ProcessName = "obs64.exe", Action = AppRuleAction.HideOverWindows }],
            Performance = new PerformanceConfig { Smoothness = Smoothness.Smoothest, Battery = BatteryBehavior.Pause },
            Advanced = new AdvancedConfig { ExcludeFromCapture = true, Autostart = true, ReducedMotion = ReducedMotion.Never },
            Templates = [new Weatherly.Core.Scenes.Scene { Name = "Mine", Emoji = "🌧️", Rain = new EffectConfig { Intensity = 0.5f } }],
        }.Sanitized();

        store.Save(settings);

        store.Load().Should().BeEquivalentTo(settings);
        File.ReadAllText(store.Path).Should().Contain("\"smoothness\": \"Smoothest\"", "enums are stored by name");
        File.Exists(store.Path + ".tmp").Should().BeFalse("the temporary file was swapped in");
    }

    [Fact]
    public void Load_AVersion1File_IsMigrated()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """
            { "version": 1, "enabled": false, "rain": 0.6, "rainDepth": 0.8, "snow": 0.3, "leaves": 1, "fog": 0.4, "sun": 0.7, "grass": 0.5, "sound": true }
            """);
        var store = new SettingsStore(FilePath);

        AppSettings settings = store.Load();

        store.LoadedVersion.Should().Be(1);
        settings.Version.Should().Be(AppSettings.CurrentVersion);
        settings.Enabled.Should().BeFalse();
        settings.Rain.Intensity.Should().Be(0.6f);
        settings.RainDepth.Should().Be(0.8f);
        settings.Snow.Intensity.Should().Be(0.3f);
        settings.Leaves.Intensity.Should().Be(1f);
        settings.Fog.Intensity.Should().Be(0.4f);
        settings.Sun.Intensity.Should().Be(0.7f);
        settings.Grass.Intensity.Should().Be(0.5f);
        settings.Sound.Muted.Should().BeFalse();
        settings.Rain.Mode.Should().Be(IntensityMode.Fixed);
    }

    [Fact]
    public void Load_AFileWithoutVersion_IsReadAsVersion1()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """{ "snow": 0.4 }""");

        new SettingsStore(FilePath).Load().Snow.Intensity.Should().Be(0.4f);
    }

    [Fact]
    public void Load_ADamagedFile_GivesTheDefaultsAndSaysWhy()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "{ \"version\": 2, \"rain\": { \"intensity\": 0.5, ");
        var store = new SettingsStore(FilePath);

        store.Load().Should().BeEquivalentTo(new AppSettings());
        store.LastError.Should().NotBeNull();
    }

    [Fact]
    public void Load_ClampsValuesAndDropsBrokenEntries()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """
            {
              "version": 2,
              "rain": { "intensity": 7, "brightness": 9, "color": "not a colour", "randomMin": 0.9, "randomMax": 0.1, "randomMinutes": 0 },
              "wind": { "speed": 5000, "gustiness": -1 },
              "displays": [ { "deviceName": "", "intensity": 1 }, { "deviceName": "A", "intensity": 0 }, { "deviceName": "a", "intensity": 1 } ],
              "appRules": [ { "processName": "  ", "action": "PauseWhenForeground" }, { "processName": " game.exe ", "action": "PauseWhenForeground" } ],
              "rainDepth": -3
            }
            """);

        AppSettings settings = new SettingsStore(FilePath).Load();

        settings.Rain.Intensity.Should().Be(1f);
        settings.Rain.Brightness.Should().Be(2f);
        settings.Rain.Color.Should().BeNull();
        settings.Rain.RandomMin.Should().Be(0.1f);
        settings.Rain.RandomMax.Should().Be(0.9f);
        settings.Rain.RandomMinutes.Should().Be(EffectConfig.MinRandomMinutes);
        settings.Wind.Speed.Should().Be(400f);
        settings.Wind.Gustiness.Should().Be(0f);
        settings.Displays.Should().ContainSingle().Which.Intensity.Should().Be(0.1f);
        settings.AppRules.Should().ContainSingle().Which.ProcessName.Should().Be("game.exe");
        settings.RainDepth.Should().Be(0f);
    }

    [Fact]
    public void Load_AFileFromANewerVersion_ReadsWhatItKnowsAndSaysSo()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """{ "version": 99, "snow": { "intensity": 0.5 }, "visitorsFromTheFuture": [] }""");
        var store = new SettingsStore(FilePath);

        store.Load().Snow.Intensity.Should().Be(0.5f);
        store.LastError.Should().Contain("newer");
    }

    [Fact]
    public void ToOverlayOptions_AndWithOverlay_CarryTheEffects()
    {
        AppSettings settings = new AppSettings
        {
            Rain = new EffectConfig { Intensity = 0.3f, Color = "#FF0000", Brightness = 1.5f },
            Fog = new EffectConfig { Intensity = 0.8f },
            RainDepth = 0.7f,
            SunSide = SunSide.Left,
            Wind = new WindConfig { Speed = 100f, Gustiness = 0.2f },
        }.Sanitized();

        OverlayOptions options = settings.ToOverlayOptions(new OverlayOptions { ShowSurfaces = true });

        options.ShowSurfaces.Should().BeTrue("kept from the current options");
        options.Rain.Should().Be(new EffectSettings { Intensity = 0.3f, Color = new System.Numerics.Vector3(1f, 0f, 0f), Brightness = 1.5f });
        options.Fog.Intensity.Should().Be(0.8f);
        options.RainDepth.Should().Be(0.7f);
        options.SunSide.Should().Be(SunSide.Left);
        options.Wind.Should().Be(new System.Numerics.Vector2(100f, 0f));
        options.Gustiness.Should().Be(0.2f);

        AppSettings changed = settings.WithOverlay(options with { Rain = options.Rain with { Intensity = 0.9f }, Enabled = false }, soundOn: true);
        changed.Rain.Intensity.Should().Be(0.9f);
        changed.Rain.Color.Should().Be("#FF0000", "the colour is not part of the overlay switches and stays");
        changed.Enabled.Should().BeFalse();
        changed.Sound.Muted.Should().BeFalse();
    }

    [Fact]
    public void DefaultPath_IsInTheUsersLocalAppData()
    {
        SettingsStore.DefaultPath.Should().EndWith(Path.Combine(AppInfo.Name, "settings.json"));
    }
}
