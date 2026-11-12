using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Weatherly.Core;
using Weatherly.Core.Displays;
using Weatherly.Core.Effects.Sun;
using Weatherly.Core.Overlay;
using Weatherly.Core.Scenes;
using Weatherly.Core.Settings;
using Weatherly.Core.Visitors;
using Weatherly.Core.Weather;
using Weatherly.Core.Weather.Cities;
using Weatherly.Platform.Displays;

namespace Weatherly.App;

/// <summary>
/// The settings window (PLAN.md, section 7): ten pages in a navigation view. Every control changes the settings
/// through the <see cref="AppController"/>; nothing here talks to the overlay directly. Closing only hides it.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>The pages in navigation order: key, title and Segoe Fluent Icons glyph.</summary>
    internal static readonly IReadOnlyList<(string Key, string Title, string Glyph)> Pages =
    [
        ("effects", "Effects", ""),
        ("templates", "Templates", ""),
        ("sync", "Weather sync", ""),
        ("visitors", "Visitors", ""),
        ("sound", "Sound", ""),
        ("displays", "Displays", ""),
        ("apps", "Apps", ""),
        ("performance", "Performance", ""),
        ("advanced", "Advanced", ""),
        ("about", "About", ""),
    ];

    /// <summary>The city list, read in the background on the first search (about 34,000 cities).</summary>
    private static readonly Lazy<Task<CityIndex>> s_cities = new(() => Task.Run(CityIndex.LoadBundled));

    private readonly AppController _controller;
    private readonly DispatcherQueueTimer _liveTimer;
    private string _page = "effects";
    private bool _quitting;
    private TextBlock? _liveText;
    private TextBlock? _syncStatus;
    private long _lastFrames = -1;
    private DateTime _lastSample;

    internal SettingsWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();
        _controller.Changed += (_, _) => UpdateSyncStatus();

        Title = $"{AppInfo.Name} settings";
        SettingsTitleBar.Title = Title;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(SettingsTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(Scaled(1040), Scaled(760)));

        foreach ((string key, string title, string glyph) in Pages)
        {
            Navigation.MenuItems.Add(new NavigationViewItem { Content = title, Tag = key, Icon = new FontIcon { Glyph = glyph } });
        }

        _liveTimer = DispatcherQueue.CreateTimer();
        _liveTimer.Interval = TimeSpan.FromSeconds(1);
        _liveTimer.Tick += (_, _) => UpdateLiveNumbers();

        // Shown again later (tray, flyout): the pages show what changed meanwhile.
        Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated)
            {
                BuildPage();
            }
        };
        AppWindow.Closing += (sender, e) =>
        {
            if (!_quitting && HideOnClose)
            {
                e.Cancel = true;
                _liveTimer.Stop();
                sender.Hide();
            }
        };

        Navigation.SelectedItem = Navigation.MenuItems[0];
    }

    /// <summary>Closing only hides the window (the app lives on in the tray). Without a tray icon it really closes.</summary>
    public bool HideOnClose { get; set; } = true;

    /// <summary>Shows the window on <paramref name="page"/> (a key of <see cref="Pages"/>) and brings it to the front.</summary>
    public void ShowPage(string page)
    {
        ArgumentNullException.ThrowIfNull(page);
        foreach (object item in Navigation.MenuItems)
        {
            if (item is NavigationViewItem navigationItem && string.Equals(navigationItem.Tag as string, page, StringComparison.Ordinal))
            {
                Navigation.SelectedItem = navigationItem;
            }
        }

        AppWindow.Show();
        Activate();
    }

    /// <summary>Writes the current page as a PNG (smoke test). Returns the rendered size.</summary>
    public Task<(int Width, int Height)> SavePageAsync(string path) => Ui.SavePngAsync(RootGrid, path);

    /// <summary>Closes the window for good (app quit).</summary>
    public void Quit()
    {
        _quitting = true;
        _liveTimer.Stop();
        Close();
    }

    /// <summary>Device-independent pixels to physical ones, by the primary monitor's scaling (the window opens there).</summary>
    private static int Scaled(int pixels)
    {
        float scale = MonitorService.GetMonitors().FirstOrDefault(monitor => monitor.IsPrimary)?.ScaleFactor ?? 1f;
        return (int)Math.Round(pixels * scale);
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string key)
        {
            _page = key;
            BuildPage();
            PageScroller.ChangeView(null, 0, null, disableAnimation: true);
        }
    }

    private void BuildPage()
    {
        _liveTimer.Stop();
        _liveText = null;
        _syncStatus = null;
        PageHost.Children.Clear();
        string title = Pages.FirstOrDefault(page => page.Key == _page).Title ?? string.Empty;
        PageHost.Children.Add(Ui.Title(title));
        if (_controller.Decision.Reason is { } reason)
        {
            PageHost.Children.Add(new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Informational, Title = reason });
        }

        switch (_page)
        {
            case "effects":
                BuildEffects();
                break;
            case "templates":
                BuildTemplates();
                break;
            case "sync":
                BuildSync();
                break;
            case "visitors":
                BuildVisitors();
                break;
            case "sound":
                BuildSound();
                break;
            case "displays":
                BuildDisplays();
                break;
            case "apps":
                BuildApps();
                break;
            case "performance":
                BuildPerformance();
                break;
            case "advanced":
                BuildAdvanced();
                break;
            default:
                BuildAbout();
                break;
        }
    }

    private void Add(UIElement element) => PageHost.Children.Add(element);

    private void UpdateEffect(EffectKind kind, Func<EffectConfig, EffectConfig> change) =>
        _controller.Update(settings => settings.WithEffect(kind, change(settings.Effect(kind))));

    private void BuildEffects()
    {
        Add(Ui.Note("Every effect has its own intensity, colour and brightness. In random mode the intensity wanders slowly between a minimum and a maximum."));
        AddSyncPausedBar();
        AppSettings settings = _controller.Settings;
        foreach (EffectKind kind in Enum.GetValues<EffectKind>())
        {
            EffectConfig config = settings.Effect(kind);
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(Ui.SliderRow("Intensity (%)", 0, 100, config.Intensity * 100, 5, value => _controller.SetIntensity(kind, (float)(value / 100))));

            StackPanel random = Ui.Stack(
                Ui.SliderRow("Lowest intensity (%)", 0, 100, config.RandomMin * 100, 5, value => UpdateEffect(kind, c => c with { RandomMin = (float)(value / 100) })),
                Ui.SliderRow("Highest intensity (%)", 0, 100, config.RandomMax * 100, 5, value => UpdateEffect(kind, c => c with { RandomMax = (float)(value / 100) })),
                Ui.SliderRow("Minutes between changes", EffectConfig.MinRandomMinutes, EffectConfig.MaxRandomMinutes, config.RandomMinutes, 1, value => UpdateEffect(kind, c => c with { RandomMinutes = (float)value })));
            random.Visibility = config.Mode == IntensityMode.Random ? Visibility.Visible : Visibility.Collapsed;
            panel.Children.Add(Ui.Choice("Mode", ["Fixed", "Random"], (int)config.Mode, index =>
            {
                UpdateEffect(kind, c => c with { Mode = (IntensityMode)index });
                random.Visibility = index == (int)IntensityMode.Random ? Visibility.Visible : Visibility.Collapsed;
            }));
            panel.Children.Add(random);

            panel.Children.Add(Ui.SliderRow("Brightness (%)", 25, 200, config.Brightness * 100, 5, value => UpdateEffect(kind, c => c with { Brightness = (float)(value / 100) })));

            System.Numerics.Vector3 color = EffectConfig.ParseColor(config.Color) ?? new System.Numerics.Vector3(1f, 1f, 1f);
            var picker = new ColorPicker
            {
                Color = ColorHelper.FromArgb(255, (byte)(color.X * 255), (byte)(color.Y * 255), (byte)(color.Z * 255)),
                IsAlphaEnabled = false,
                IsMoreButtonVisible = false,
                IsColorChannelTextInputVisible = false,
                IsHexInputVisible = true,
                Visibility = config.Color is null ? Visibility.Collapsed : Visibility.Visible,
            };
            picker.ColorChanged += (_, e) => UpdateEffect(kind, c => c with
            {
                Color = EffectConfig.FormatColor(new System.Numerics.Vector3(e.NewColor.R, e.NewColor.G, e.NewColor.B) / 255f),
            });
            panel.Children.Add(Ui.Toggle("Own colour", config.Color is not null, on =>
            {
                picker.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                UpdateEffect(kind, c => c with
                {
                    Color = on ? EffectConfig.FormatColor(new System.Numerics.Vector3(picker.Color.R, picker.Color.G, picker.Color.B) / 255f) : null,
                });
            }));
            panel.Children.Add(picker);

            if (kind == EffectKind.Rain)
            {
                panel.Children.Add(Ui.SliderRow("Depth: share of drops that hit windows (%)", 0, 100, settings.RainDepth * 100, 5, value => _controller.Update(s => s with { RainDepth = (float)(value / 100) })));
            }
            else if (kind == EffectKind.Sun)
            {
                panel.Children.Add(Ui.Choice("The sun stands above the", ["Top right corner", "Top left corner"], settings.SunSide == SunSide.Left ? 1 : 0, index =>
                    _controller.Update(s => s with { SunSide = index == 1 ? SunSide.Left : SunSide.Right })));
            }

            Add(new Expander
            {
                Header = Ui.Row(Ui.Icon(kind), new TextBlock { Text = Ui.Name(kind) }),
                Content = panel,
                IsExpanded = kind == EffectKind.Rain,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            });
        }

        Add(Ui.Heading("Wind"));
        Add(Ui.Card(Ui.Stack(
            Ui.SliderRow("Wind (px/s, negative blows to the left)", -400, 400, settings.Wind.Speed, 10, value => _controller.SetWind(w => w with { Speed = (float)value })),
            Ui.SliderRow("Gusts (%)", 0, 100, settings.Wind.Gustiness * 100, 5, value => _controller.SetWind(w => w with { Gustiness = (float)(value / 100) })))));
    }

    private void BuildTemplates()
    {
        AppSettings settings = _controller.Settings;
        var status = new InfoBar { IsOpen = false, IsClosable = true };

        Add(Ui.Heading("Apply"));
        var grid = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 200, ItemHeight = 44 };
        foreach (Scene scene in _controller.AllTemplates)
        {
            var button = Ui.ActionButton($"{scene.Emoji}  {scene.Name}", () => _controller.ApplyScene(scene));
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Margin = new Thickness(0, 0, 8, 8);
            grid.Children.Add(button);
        }

        Add(grid);

        Add(Ui.Heading("Your templates"));
        if (settings.Templates.Count == 0)
        {
            Add(Ui.Note("None yet. Save the current weather below, or open a link someone shared."));
        }

        foreach (Scene scene in settings.Templates)
        {
            Add(Ui.Card(Ui.Row(
                new TextBlock { Text = $"{scene.Emoji}  {scene.Name}", VerticalAlignment = VerticalAlignment.Center, MinWidth = 220 },
                Ui.ActionButton("Copy link", () =>
                {
                    var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    package.SetText(SceneCodec.ToLink(scene));
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
                    ShowStatus(status, InfoBarSeverity.Success, "Link copied. Anyone with the app can open it.");
                }),
                Ui.ActionButton("Export…", () => _ = ExportAsync(scene, status)),
                Ui.ActionButton("Delete", () =>
                {
                    _controller.Update(s => s with { Templates = [.. s.Templates.Where(t => t.Name != scene.Name)] });
                    BuildPage();
                }))));
        }

        Add(Ui.Heading("Save the current weather"));
        var name = new TextBox { Header = "Name", PlaceholderText = "Rainy afternoon", MaxLength = Scene.MaxNameLength, Width = 260 };
        var emoji = new TextBox { Header = "Emoji", Text = "🌦️", MaxLength = 8, Width = 80 };
        Add(Ui.Row(name, emoji, Ui.ActionButton("Save", () =>
        {
            string title = string.IsNullOrWhiteSpace(name.Text) ? "My weather" : name.Text.Trim();
            _controller.Update(s => s with { Templates = [.. s.Templates.Where(t => t.Name != title), Scene.From(s, title, emoji.Text)] });
            BuildPage();
        })));

        Add(Ui.Heading("Open a shared template"));
        var link = new TextBox { Header = "Link", PlaceholderText = SceneCodec.LinkPrefix + "…", Width = 420 };
        Add(Ui.Row(link, Ui.ActionButton("Apply", () =>
        {
            if (SceneCodec.TryFromLink(link.Text.Trim(), out Scene scene, out string? error))
            {
                _controller.Update(s => scene.ApplyTo(s) with { Templates = [.. s.Templates.Where(t => t.Name != scene.Name), scene] });
                BuildPage();
            }
            else
            {
                ShowStatus(status, InfoBarSeverity.Error, error ?? "This link cannot be opened.");
            }
        })));
        Add(Ui.ActionButton("Import a .wscene file…", () => _ = ImportAsync(status)));
        Add(status);
    }

    private static void ShowStatus(InfoBar bar, InfoBarSeverity severity, string message)
    {
        bar.Severity = severity;
        bar.Message = message;
        bar.IsOpen = true;
    }

    private async Task ExportAsync(Scene scene, InfoBar status)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = scene.Name };
        picker.FileTypeChoices.Add($"{AppInfo.Name} scene", [SceneCodec.FileExtension]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Win32Interop.GetWindowFromWindowId(AppWindow.Id));
        Windows.Storage.StorageFile? file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            await Windows.Storage.FileIO.WriteTextAsync(file, SceneCodec.ToJson(scene));
            ShowStatus(status, InfoBarSeverity.Success, $"Saved as {file.Name}.");
        }
    }

    private async Task ImportAsync(InfoBar status)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(SceneCodec.FileExtension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Win32Interop.GetWindowFromWindowId(AppWindow.Id));
        Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        string json = await Windows.Storage.FileIO.ReadTextAsync(file);
        if (SceneCodec.TryFromJson(json, out Scene scene, out string? error))
        {
            _controller.Update(s => s with { Templates = [.. s.Templates.Where(t => t.Name != scene.Name), scene] });
            BuildPage();
        }
        else
        {
            ShowStatus(status, InfoBarSeverity.Error, error ?? "This file cannot be read.");
        }
    }

    private void AddSyncPausedBar()
    {
        if (Ui.SyncPausedBar(_controller, BuildPage) is { } bar)
        {
            Add(bar);
        }
    }

    private void BuildSync()
    {
        WeatherSyncConfig sync = _controller.Settings.WeatherSync;
        Add(Ui.Note("The effects follow the real weather at your location or a city you choose. Moving a slider by hand pauses the sync for that effect."));
        AddSyncPausedBar();

        var details = new StackPanel { Spacing = 16, Visibility = sync.Enabled ? Visibility.Visible : Visibility.Collapsed };
        Add(Ui.Card(Ui.Stack(
            Ui.Toggle("Follow the real weather", sync.Enabled, on =>
            {
                details.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                UpdateSync(s => s with { Enabled = on });
            }),
            Ui.Note("Only a rounded location (about 11 km) is sent to the weather service, every 15 to 60 minutes. Nothing else."))));
        Add(details);

        _syncStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        details.Children.Add(Ui.Card(Ui.Stack(
            new TextBlock { Text = "Now", Style = Ui.Resource<Style>("BodyStrongTextBlockStyle") },
            _syncStatus,
            Ui.ActionButton("Update now", _controller.RefreshWeatherNow))));
        UpdateSyncStatus();

        // Location: a city from the bundled list (searched on this PC) or the Windows location service.
        var cityPanel = new StackPanel { Spacing = 8, Visibility = sync.Source == LocationSource.City ? Visibility.Visible : Visibility.Collapsed };
        var chosen = new TextBlock { Text = sync.City is { } city ? $"Weather for {city.Name} ({city.Country})" : "No city chosen yet." };
        cityPanel.Children.Add(chosen);
        cityPanel.Children.Add(CitySearch(chosen));
        details.Children.Add(Ui.Card(Ui.Stack(
            Ui.Choice("Location", ["A city I choose", "This PC's location (Windows location service)"], (int)sync.Source, index =>
            {
                cityPanel.Visibility = index == (int)LocationSource.City ? Visibility.Visible : Visibility.Collapsed;
                UpdateSync(s => s with { Source = (LocationSource)index });
            }),
            cityPanel)));

        // Custom sync: a weight per element, and the inverted world.
        var weights = Ui.Stack(
            Ui.Toggle("Invert: a clear sky rains, bad weather brings sunshine, heat brings snow", sync.Invert, on => UpdateSync(s => s with { Invert = on })),
            Ui.Note("How much each element follows the weather. 0 % keeps your own value, 100 % is the weather."));
        foreach (SyncElement element in Enum.GetValues<SyncElement>())
        {
            weights.Children.Add(Ui.SliderRow($"{Ui.Name(element)} (%)", 0, 100, sync.Weight(element) * 100, 5,
                value => UpdateSync(s => s.WithWeight(element, (float)(value / 100)))));
        }

        details.Children.Add(Ui.Card(weights));
        details.Children.Add(Ui.Note($"{_controller.Weather?.Attribution ?? "Weather data: MET Norway (CC BY 4.0)"}. {CityIndex.Attribution}."));
    }

    private void UpdateSync(Func<WeatherSyncConfig, WeatherSyncConfig> change) =>
        _controller.Update(settings => settings with { WeatherSync = change(settings.WeatherSync) });

    /// <summary>The city search: suggestions from the bundled list while typing, nothing leaves the PC.</summary>
    private AutoSuggestBox CitySearch(TextBlock chosen)
    {
        var box = new AutoSuggestBox
        {
            Header = "City",
            PlaceholderText = "Search for a city, for example Hamburg or Paris, FR",
            QueryIcon = new SymbolIcon(Symbol.Find),
            Width = 420,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        IReadOnlyList<City> results = [];

        void Choose(City city)
        {
            chosen.Text = $"Weather for {city.DisplayName}";
            box.Text = string.Empty;
            UpdateSync(s => s with
            {
                Source = LocationSource.City,
                City = new CityChoice { Name = city.Name, Country = city.Country, Latitude = city.Location.Latitude, Longitude = city.Location.Longitude },
            });
        }

        box.TextChanged += async (sender, e) =>
        {
            if (e.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            {
                return;
            }

            string text = sender.Text;
            CityIndex index = await s_cities.Value;
            if (sender.Text != text)
            {
                return;
            }

            results = index.Search(text, limit: 8);
            sender.ItemsSource = results.Select(city => city.DisplayName).ToList();
        };
        box.QuerySubmitted += async (sender, e) =>
        {
            int picked = e.ChosenSuggestion is string name ? results.Select(city => city.DisplayName).ToList().IndexOf(name) : -1;
            if (picked >= 0)
            {
                Choose(results[picked]);
                return;
            }

            CityIndex index = await s_cities.Value;
            if (index.Search(sender.Text, limit: 1) is [City first])
            {
                Choose(first);
            }
        };
        return box;
    }

    /// <summary>The sync's status line: the weather now, the location, the next request, problems.</summary>
    private void UpdateSyncStatus()
    {
        if (_syncStatus is null)
        {
            return;
        }

        WeatherSyncConfig config = _controller.Settings.WeatherSync;
        WeatherSync? sync = _controller.Weather;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var lines = new List<string>();
        if (sync is null)
        {
            lines.Add("The weather sync is not available in this run.");
        }
        else if (!config.Enabled)
        {
            lines.Add("Off. Nothing is sent.");
        }
        else
        {
            if (_controller.LocationProblem is { } problem)
            {
                lines.Add(problem);
            }
            else if (_controller.WeatherLocation is { } place)
            {
                lines.Add(config.Source == LocationSource.City && config.City is { } city
                    ? $"{city.Name} ({city.Country})"
                    : string.Create(CultureInfo.CurrentCulture, $"This PC's location, about {place.Latitude:0.0}°, {place.Longitude:0.0}°"));
            }

            if (sync.Current(now) is { } weather)
            {
                lines.Add(Describe(weather));
            }

            lines.Add(sync.State switch
            {
                WeatherSyncState.Live => string.Create(CultureInfo.CurrentCulture, $"Updated at {sync.LastSuccess!.Value.ToLocalTime():t}, next check at {sync.NextAttempt.ToLocalTime():t}."),
                WeatherSyncState.Offline => string.Create(CultureInfo.CurrentCulture, $"Offline: {sync.LastError} Showing the last forecast, next try at {sync.NextAttempt.ToLocalTime():t}."),
                _ => sync.LastError is { } error ? $"No weather yet: {error}" : "Asking the weather service…",
            });
        }

        _syncStatus.Text = string.Join(Environment.NewLine, lines);
    }

    /// <summary>"Light rain showers, 3 °C, wind 2 m/s from the south-west".</summary>
    private static string Describe(WeatherSnapshot weather)
    {
        string symbol = weather.Symbol.Split('_')[0];
        string text = symbol switch
        {
            "" => "No symbol",
            "clearsky" => "Clear sky",
            "partlycloudy" => "Partly cloudy",
            _ => symbol.Replace("light", "light ", StringComparison.Ordinal).Replace("heavy", "heavy ", StringComparison.Ordinal)
                .Replace("showers", " showers", StringComparison.Ordinal).Replace("andthunder", " and thunder", StringComparison.Ordinal),
        };
        text = char.ToUpperInvariant(text[0]) + text[1..].Replace("  ", " ", StringComparison.Ordinal);
        string[] directions = ["north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west"];
        string from = directions[(int)Math.Round(((weather.WindFromDirection % 360) + 360) % 360 / 45.0) % 8];
        return string.Create(CultureInfo.CurrentCulture, $"{text}, {weather.AirTemperature:0} °C, wind {weather.WindSpeed:0} m/s from the {from}.");
    }

    private void BuildVisitors()
    {
        VisitorConfig config = _controller.Settings.Visitors;
        Add(Ui.Note("Small characters of our own drop by on your windows: they walk along the top edges, jump from window to window and react to the weather."));
        var status = new InfoBar { IsOpen = false, IsClosable = true };
        Add(status);
        string shortcut = _controller.Window.CollectShortcutRegistered
            ? $"Press {Platform.Overlay.OverlayHost.CollectShortcut} or use the tray menu to collect a visitor without the mouse."
            : $"{Platform.Overlay.OverlayHost.CollectShortcut} is taken by another program; the tray menu collects visitors without the mouse.";
        Add(Ui.Card(Ui.Stack(
            Ui.Choice("How often visitors come", ["Never", "Rarely (about every 30 minutes)", "Sometimes (about every 10 minutes)", "Often (about every 3 minutes)"], (int)config.Frequency,
                index => _controller.Update(s => s with { Visitors = s.Visitors with { Frequency = (VisitorFrequency)index } })),
            Ui.Toggle("Pick up visitors and drop them into the box", config.Interactive,
                on => _controller.Update(s => s with { Visitors = s.Visitors with { Interactive = on } })),
            Ui.Toggle("Announce every visitor with a notification", config.Announce,
                on => _controller.Update(s => s with { Visitors = s.Visitors with { Announce = on } })),
            Ui.Note(shortcut),
            Ui.ActionButton("Invite a visitor now", () =>
            {
                if (!_controller.InviteVisitor())
                {
                    ShowStatus(status, InfoBarSeverity.Informational, "No room right now: there are visitors already, the weather is switched off, or no window edge is free.");
                }
            }))));

        VisitorAlbum album = _controller.Album;
        Add(Ui.Heading("Collection"));
        Add(Ui.Note(string.Create(CultureInfo.CurrentCulture,
            $"{album.TotalCollected} collected, {album.Points} points, best combo {album.BestCombo}. Rarer visitors are worth more, and twice as much in their favourite weather.")));
        var grid = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 158, ItemHeight = 214 };
        foreach (VisitorProfile profile in VisitorCatalog.All)
        {
            int count = album.CountOf(profile.Kind);
            Image picture = Ui.VisitorImage(profile.Kind, 72);
            picture.Opacity = count > 0 ? 1 : 0.3;
            var card = Ui.Card(Ui.Stack(
                picture,
                new TextBlock { Text = profile.Name, Style = Ui.Resource<Style>("BodyStrongTextBlockStyle") },
                Ui.Note(count > 0 ? string.Create(CultureInfo.CurrentCulture, $"Collected {count}×") : "Not met yet"),
                Ui.Note($"{Favourite(profile.Affinity)}, {profile.Rarity.ToString().ToLowerInvariant()}")));
            card.Margin = new Thickness(0, 0, 8, 8);
            ToolTipService.SetToolTip(card, profile.Description);
            grid.Children.Add(card);
        }

        Add(grid);

        Add(Ui.Heading("Achievements"));
        var achievements = new StackPanel { Spacing = 6 };
        foreach (Achievement achievement in Enum.GetValues<Achievement>())
        {
            (string title, string description) = Achievements.Describe(achievement);
            bool reached = album.Has(achievement);
            achievements.Children.Add(Ui.Row(
                new FontIcon { Glyph = reached ? "\uE73E" : "\uE739", Foreground = reached ? Ui.Resource<Microsoft.UI.Xaml.Media.Brush>("AccentTextFillColorPrimaryBrush") : Ui.Resource<Microsoft.UI.Xaml.Media.Brush>("TextFillColorTertiaryBrush") },
                new TextBlock { Text = title, Style = Ui.Resource<Style>("BodyStrongTextBlockStyle"), Width = 150 },
                Ui.Note(reached ? description : $"{description} (not yet)")));
        }

        Add(Ui.Card(achievements));

        Add(Ui.Heading("Share card"));
        Border shareCard = ShareCard(album);
        Add(shareCard);
        Add(Ui.Row(
            Ui.ActionButton("Copy card", async () =>
            {
                (byte[]? png, _, _) = await Ui.RenderPngAsync(shareCard);
                if (png is null)
                {
                    return;
                }

                var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                await stream.WriteAsync(png.AsBuffer());
                stream.Seek(0);
                var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                package.SetBitmap(Windows.Storage.Streams.RandomAccessStreamReference.CreateFromStream(stream));
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
                ShowStatus(status, InfoBarSeverity.Success, "Card copied as a picture.");
            }),
            Ui.ActionButton("Save card…", async () =>
            {
                (byte[]? png, _, _) = await Ui.RenderPngAsync(shareCard);
                if (png is null)
                {
                    return;
                }

                var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = $"{AppInfo.Name} visitors" };
                picker.FileTypeChoices.Add("PNG picture", [".png"]);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, Win32Interop.GetWindowFromWindowId(AppWindow.Id));
                Windows.Storage.StorageFile? file = await picker.PickSaveFileAsync();
                if (file is not null)
                {
                    await Windows.Storage.FileIO.WriteBytesAsync(file, png);
                    ShowStatus(status, InfoBarSeverity.Success, $"Saved {file.Name}.");
                }
            })));
    }

    private static string Favourite(WeatherAffinity affinity) => affinity switch
    {
        WeatherAffinity.Any => "any weather",
        WeatherAffinity.Leaves => "loves falling leaves",
        WeatherAffinity.Sun => "loves sunshine",
        _ => $"loves {affinity.ToString().ToLowerInvariant()}",
    };

    /// <summary>A card with the collection to share as a picture: points, achievements and the visitors met so far.</summary>
    private static Border ShareCard(VisitorAlbum album)
    {
        var visitors = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (VisitorProfile profile in VisitorCatalog.All)
        {
            Image picture = Ui.VisitorImage(profile.Kind, 44);
            picture.Opacity = album.CountOf(profile.Kind) > 0 ? 1 : 0.18;
            visitors.Children.Add(picture);
        }

        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock { Text = $"My {AppInfo.Name} visitors", Style = Ui.Resource<Style>("SubtitleTextBlockStyle"), Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 46, 52, 64)) });
        content.Children.Add(visitors);
        content.Children.Add(new TextBlock
        {
            Text = string.Create(CultureInfo.CurrentCulture, $"{album.Points} points · {album.TotalCollected} collected · {album.Achievements.Count} of {Enum.GetValues<Achievement>().Length} achievements"),
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 76, 86, 106)),
        });

        return new Border
        {
            Width = 440,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(12),
            Background = new Microsoft.UI.Xaml.Media.LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1),
                GradientStops =
                {
                    new Microsoft.UI.Xaml.Media.GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(255, 220, 232, 245), Offset = 0 },
                    new Microsoft.UI.Xaml.Media.GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(255, 245, 240, 225), Offset = 1 },
                },
            },
            Child = content,
        };
    }

    private void BuildSound()
    {
        SoundConfig sound = _controller.Settings.Sound;
        Add(Ui.Card(Ui.Stack(
            Ui.Toggle("Sound", !sound.Muted, on => _controller.Update(s => s with { Sound = s.Sound with { Muted = !on } })),
            Ui.SliderRow("Volume (%)", 0, 100, sound.Master * 100, 5, value => _controller.Update(s => s with { Sound = s.Sound with { Master = (float)(value / 100) } })),
            Ui.SliderRow("Rain (%)", 0, 100, sound.Rain * 100, 5, value => _controller.Update(s => s with { Sound = s.Sound with { Rain = (float)(value / 100) } })),
            Ui.SliderRow("Wind (%)", 0, 100, sound.Wind * 100, 5, value => _controller.Update(s => s with { Sound = s.Sound with { Wind = (float)(value / 100) } })),
            Ui.SliderRow("Visitors (%)", 0, 100, sound.Visitors * 100, 5, value => _controller.Update(s => s with { Sound = s.Sound with { Visitors = (float)(value / 100) } })),
            Ui.Note("The rain sound is made on the fly. Wind and visitor sounds follow with later milestones."))));
    }

    private void BuildDisplays()
    {
        Add(Ui.Note("Weather on each monitor: switch it off or make it fainter."));
        foreach (MonitorInfo monitor in MonitorService.GetMonitors())
        {
            DisplayConfig display = _controller.Settings.Display(monitor.DeviceName);
            string name = string.Create(
                CultureInfo.InvariantCulture,
                $"{monitor.DeviceName.TrimStart('\\', '.')}{(monitor.IsPrimary ? " (primary)" : string.Empty)}: {monitor.Bounds.Width} x {monitor.Bounds.Height}, {Math.Round(monitor.ScaleFactor * 100)} %");
            Add(Ui.Card(Ui.Stack(
                Ui.Toggle(name, display.Enabled, on => UpdateDisplay(monitor.DeviceName, d => d with { Enabled = on })),
                Ui.SliderRow("Intensity (%)", 10, 100, display.Intensity * 100, 5, value => UpdateDisplay(monitor.DeviceName, d => d with { Intensity = (float)(value / 100) })))));
        }
    }

    private void UpdateDisplay(string deviceName, Func<DisplayConfig, DisplayConfig> change) => _controller.Update(s => s with
    {
        Displays = [.. s.Displays.Where(d => !string.Equals(d.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase)), change(s.Display(deviceName))],
    });

    private void BuildApps()
    {
        Add(Ui.Note("Rules for single apps: pause all weather while the app is in front, or keep its windows clear of weather. Only the app's file name is used, on this PC."));
        string[] actions = ["Pause while in the foreground", "Hide the weather over its windows"];
        foreach (AppRule rule in _controller.Settings.AppRules)
        {
            AppRule current = rule;
            Add(Ui.Card(Ui.Row(
                new TextBlock { Text = rule.ProcessName, VerticalAlignment = VerticalAlignment.Center, MinWidth = 200 },
                new TextBlock { Text = actions[(int)rule.Action], VerticalAlignment = VerticalAlignment.Center, MinWidth = 260 },
                Ui.ActionButton("Remove", () =>
                {
                    _controller.Update(s => s with { AppRules = [.. s.AppRules.Where(r => r != current)] });
                    BuildPage();
                }))));
        }

        Add(Ui.Heading("Add a rule"));
        var app = new ComboBox { Header = "App", IsEditable = true, PlaceholderText = "obs64.exe", MinWidth = 260 };
        foreach (string process in RunningApps())
        {
            app.Items.Add(process);
        }

        int action = 0;
        Add(Ui.Row(
            app,
            Ui.Choice("Rule", actions, 0, index => action = index),
            Ui.ActionButton("Add", () =>
            {
                string name = (app.SelectedItem as string ?? app.Text ?? string.Empty).Trim();
                if (name.Length == 0)
                {
                    return;
                }

                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    name += ".exe";
                }

                _controller.Update(s => s with { AppRules = [.. s.AppRules, new AppRule { ProcessName = name, Action = (AppRuleAction)action }] });
                BuildPage();
            })));
    }

    /// <summary>Executable names of the running apps with a window, for the rule picker. Names only, nothing else.</summary>
    private static List<string> RunningApps()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Diagnostics.Process process in System.Diagnostics.Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        names.Add(process.ProcessName + ".exe");
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process ended meanwhile.
                }
            }
        }

        return [.. names];
    }

    private void BuildPerformance()
    {
        PerformanceConfig performance = _controller.Settings.Performance;
        Add(Ui.Card(Ui.Stack(
            Ui.Choice(
                "Smoothness",
                ["Auto: 60 fps, 30 on battery", "Least energy: 30 fps", "Balanced: 60 fps", "Smoothest: the display's rate"],
                (int)performance.Smoothness,
                index => _controller.Update(s => s with { Performance = s.Performance with { Smoothness = (Smoothness)index } })),
            Ui.Choice(
                "On battery",
                ["Keep going", "Reduce: half the intensity, 30 fps", "Pause"],
                (int)performance.Battery,
                index => _controller.Update(s => s with { Performance = s.Performance with { Battery = (BatteryBehavior)index } })))));

        Add(Ui.Heading("Right now"));
        _liveText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Add(Ui.Card(_liveText));
        _lastFrames = -1;
        UpdateLiveNumbers();
        _liveTimer.Start();
    }

    private void UpdateLiveNumbers()
    {
        if (_liveText is null)
        {
            _liveTimer.Stop();
            return;
        }

        OverlayStatus? status = _controller.Window.GetStatus();
        DateTime now = DateTime.UtcNow;
        string text;
        if (status is null)
        {
            text = "The overlay is not running.";
        }
        else if (!status.IsAnimating)
        {
            text = "Idle: nothing moves, no frames are drawn.";
        }
        else
        {
            double fps = _lastFrames < 0 ? 0 : (status.AnimationFrames - _lastFrames) / Math.Max((now - _lastSample).TotalSeconds, 0.001);
            text = string.Create(
                CultureInfo.InvariantCulture,
                $"{fps:0} fps, {status.FrameMilliseconds:0.0} ms per frame, simulation {status.SimulationMilliseconds:0.00} ms, {status.Sprites} sprites");
        }

        RuntimeDecision decision = _controller.Decision;
        string cap = decision.FrameRateCap == 0 ? "the display's rate" : string.Create(CultureInfo.InvariantCulture, $"{decision.FrameRateCap} fps");
        _liveText.Text = $"{text}\nFrame rate limit: {cap}. {decision.Reason ?? "Running."}";
        _lastFrames = status?.AnimationFrames ?? -1;
        _lastSample = now;
    }

    private void BuildAdvanced()
    {
        AdvancedConfig advanced = _controller.Settings.Advanced;
        var stack = Ui.Stack(
            Ui.Toggle("Hide the weather from screenshots and screen sharing", advanced.ExcludeFromCapture, on => _controller.Update(s => s with { Advanced = s.Advanced with { ExcludeFromCapture = on } })),
            Ui.Toggle("Start with Windows", advanced.Autostart, on => _controller.Update(s => s with { Advanced = s.Advanced with { Autostart = on } })),
            Ui.Choice(
                "Reduced motion",
                ["Follow Windows (pause when animations are off)", "Always pause", "Never pause"],
                (int)advanced.ReducedMotion,
                index => _controller.Update(s => s with { Advanced = s.Advanced with { ReducedMotion = (ReducedMotion)index } })));
        if (Autostart.LastError is { } error)
        {
            stack.Children.Add(Ui.Note(error));
        }

        Add(Ui.Card(stack));
        Add(Ui.Heading("Troubleshooting"));
        Add(Ui.Row(
            Ui.ActionButton("Open the diagnostics window", _controller.Window.ShowAndActivate),
            Ui.ActionButton("Reset all settings…", () => _ = ConfirmResetAsync())));
    }

    private async Task ConfirmResetAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "Reset all settings?",
            Content = "Every effect, rule and option goes back to its default. Your own templates stay.",
            PrimaryButtonText = "Reset",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _controller.Reset();
            BuildPage();
        }
    }

    private void BuildAbout()
    {
        Add(Ui.Card(Ui.Stack(
            new TextBlock { Text = $"{AppInfo.Name} {AppInfo.DisplayVersion}", Style = Ui.Resource<Style>("BodyStrongTextBlockStyle") },
            Ui.Note("Animated weather on top of your open windows."))));
        Add(Ui.Heading("Privacy"));
        Add(Ui.Card(Ui.Note(
            "The app reads only where windows are and how big they are, never their titles or contents. It does not record the screen. " +
            "App rules use the app's file name, on this PC only. Nothing is collected. Only when you switch the weather sync on, a location rounded to about 11 km is sent to the weather service, and nothing else.")));
        Add(Ui.Heading("Credits"));
        Add(Ui.Card(Ui.Stack(
            Ui.Note("All graphics and sounds are made by the app itself or for it; see ASSETS.md."),
            Ui.Note("Weather data: MET Norway (api.met.no), CC BY 4.0."),
            Ui.Note($"{CityIndex.Attribution}, geonames.org."))));
    }
}
