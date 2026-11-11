using Weatherly.Core.Effects;
using Weatherly.Core.Overlay;
using Weatherly.Core.Settings;

namespace Weatherly.Core.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"weatherly-settings-{Guid.NewGuid():N}");

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
        var store = new SettingsStore(Path.Combine(_directory, "settings.json"));

        store.Load().Should().Be(new UserSettings());
        store.LastError.Should().BeNull();
    }

    [Fact]
    public void SaveThenLoad_GivesTheSameSettings()
    {
        var store = new SettingsStore(Path.Combine(_directory, "nested", "settings.json"));
        var settings = new UserSettings { Enabled = false, Rain = 0.6f, RainDepth = 0.8f, Snow = 0.3f, Sound = true };

        store.Save(settings);

        store.Load().Should().Be(settings);
        File.Exists(store.Path + ".tmp").Should().BeFalse("the temporary file was swapped in");
    }

    [Fact]
    public void Load_ADamagedFile_GivesTheDefaultsAndSaysWhy()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "{ \"rain\": 0.5, ");
        var store = new SettingsStore(path);

        store.Load().Should().Be(new UserSettings());
        store.LastError.Should().NotBeNull();
    }

    [Fact]
    public void Load_ClampsValuesOutOfRange()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "{ \"version\": 1, \"rain\": 7, \"snow\": -2, \"rainDepth\": 3 }");

        UserSettings settings = new SettingsStore(path).Load();

        settings.Rain.Should().Be(1f);
        settings.Snow.Should().Be(0f);
        settings.RainDepth.Should().Be(1f);
    }

    [Fact]
    public void Load_AFileWithUnknownAndMissingFields_KeepsTheRest()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "{ \"snow\": 0.4, \"somethingFromTheFuture\": true }");

        UserSettings settings = new SettingsStore(path).Load();

        settings.Snow.Should().Be(0.4f);
        settings.RainDepth.Should().Be(0.5f);
        settings.Enabled.Should().BeTrue();
    }

    [Fact]
    public void FromAndApplyTo_CarryTheEffectsBetweenSettingsAndOverlayOptions()
    {
        var options = new OverlayOptions
        {
            Enabled = false,
            Rain = new EffectSettings { Intensity = 0.3f, Brightness = 1.5f },
            RainDepth = 0.7f,
            Snow = new EffectSettings { Intensity = 1f },
            ShowSurfaces = true,
        };

        UserSettings settings = UserSettings.From(options, sound: true);
        OverlayOptions restored = settings.ApplyTo(new OverlayOptions { ShowSurfaces = true, Rain = new EffectSettings { Brightness = 1.5f } });

        settings.Sound.Should().BeTrue();
        restored.Should().Be(options);
    }

    [Fact]
    public void DefaultPath_IsInTheUsersLocalAppData()
    {
        SettingsStore.DefaultPath.Should().EndWith(Path.Combine(AppInfo.Name, "settings.json"));
    }
}
