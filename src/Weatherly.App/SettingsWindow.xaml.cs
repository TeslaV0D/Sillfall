using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Weatherly.Core;
using Weatherly.Core.Displays;
using Weatherly.Core.Effects.Sun;
using Weatherly.Core.Localization;
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
        ("effects", "Page.Effects", ""),
        ("templates", "Page.Templates", ""),
        ("sync", "Page.Sync", ""),
        ("visitors", "Page.Visitors", ""),
        ("sound", "Page.Sound", ""),
        ("displays", "Page.Displays", ""),
        ("apps", "Page.Apps", ""),
        ("performance", "Page.Performance", ""),
        ("advanced", "Page.Advanced", ""),
        ("about", "Page.About", ""),
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

        Title = Texts.Format("Settings.WindowTitle", AppInfo.Name);
        SettingsTitleBar.Title = Title;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(SettingsTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(Scaled(1040), Scaled(760)));

        foreach ((string key, string title, string glyph) in Pages)
        {
            Navigation.MenuItems.Add(new NavigationViewItem { Content = Texts.Get(title), Tag = key, Icon = new FontIcon { Glyph = glyph } });
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
        // Titles follow the language, which may have changed since the last page.
        Title = Texts.Format("Settings.WindowTitle", AppInfo.Name);
        SettingsTitleBar.Title = Title;
        for (int i = 0; i < Navigation.MenuItems.Count && i < Pages.Count; i++)
        {
            if (Navigation.MenuItems[i] is NavigationViewItem item)
            {
                item.Content = Texts.Get(Pages[i].Title);
            }
        }

        string title = Texts.Get(Pages.FirstOrDefault(page => page.Key == _page).Title ?? string.Empty);
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
        Add(Ui.Note(Texts.Get("Effects.Intro")));
        AddSyncPausedBar();
        AppSettings settings = _controller.Settings;
        foreach (EffectKind kind in Enum.GetValues<EffectKind>())
        {
            EffectConfig config = settings.Effect(kind);
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(Ui.SliderRow(Texts.Get("Effects.Intensity"), 0, 100, config.Intensity * 100, 5, value => _controller.SetIntensity(kind, (float)(value / 100))));

            StackPanel random = Ui.Stack(
                Ui.SliderRow(Texts.Get("Effects.RandomMin"), 0, 100, config.RandomMin * 100, 5, value => UpdateEffect(kind, c => c with { RandomMin = (float)(value / 100) })),
                Ui.SliderRow(Texts.Get("Effects.RandomMax"), 0, 100, config.RandomMax * 100, 5, value => UpdateEffect(kind, c => c with { RandomMax = (float)(value / 100) })),
                Ui.SliderRow(Texts.Get("Effects.RandomMinutes"), EffectConfig.MinRandomMinutes, EffectConfig.MaxRandomMinutes, config.RandomMinutes, 1, value => UpdateEffect(kind, c => c with { RandomMinutes = (float)value })));
            random.Visibility = config.Mode == IntensityMode.Random ? Visibility.Visible : Visibility.Collapsed;
            panel.Children.Add(Ui.Choice(Texts.Get("Effects.Mode"), [Texts.Get("Effects.Mode.Fixed"), Texts.Get("Effects.Mode.Random")], (int)config.Mode, index =>
            {
                UpdateEffect(kind, c => c with { Mode = (IntensityMode)index });
                random.Visibility = index == (int)IntensityMode.Random ? Visibility.Visible : Visibility.Collapsed;
            }));
            panel.Children.Add(random);

            panel.Children.Add(Ui.SliderRow(Texts.Get("Effects.Brightness"), 25, 200, config.Brightness * 100, 5, value => UpdateEffect(kind, c => c with { Brightness = (float)(value / 100) })));

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
            panel.Children.Add(Ui.Toggle(Texts.Get("Effects.OwnColour"), config.Color is not null, on =>
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
                panel.Children.Add(Ui.SliderRow(Texts.Get("Effects.RainDepth"), 0, 100, settings.RainDepth * 100, 5, value => _controller.Update(s => s with { RainDepth = (float)(value / 100) })));
            }
            else if (kind == EffectKind.Sun)
            {
                panel.Children.Add(Ui.Choice(Texts.Get("Effects.SunSide"), [Texts.Get("Effects.SunSide.Right"), Texts.Get("Effects.SunSide.Left")], settings.SunSide == SunSide.Left ? 1 : 0, index =>
                    _controller.Update(s => s with { SunSide = index == 1 ? SunSide.Left : SunSide.Right })));
            }

            var expander = new Expander
            {
                Header = Ui.Row(Ui.Icon(kind), new TextBlock { Text = Ui.Name(kind) }),
                Content = panel,
                IsExpanded = kind == EffectKind.Rain,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            AutomationProperties.SetName(expander, Ui.Name(kind));
            Add(expander);
        }

        Add(Ui.Heading(Texts.Get("Effects.Wind")));
        Add(Ui.Card(Ui.Stack(
            Ui.SliderRow(Texts.Get("Effects.WindSpeed"), -400, 400, settings.Wind.Speed, 10, value => _controller.SetWind(w => w with { Speed = (float)value })),
            Ui.SliderRow(Texts.Get("Effects.Gusts"), 0, 100, settings.Wind.Gustiness * 100, 5, value => _controller.SetWind(w => w with { Gustiness = (float)(value / 100) })))));
    }

    private void BuildTemplates()
    {
        AppSettings settings = _controller.Settings;
        var status = new InfoBar { IsOpen = false, IsClosable = true };

        Add(Ui.Heading(Texts.Get("Templates.Apply")));
        var grid = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 200, ItemHeight = 44 };
        foreach (Scene scene in _controller.AllTemplates)
        {
            var button = Ui.ActionButton($"{scene.Emoji}  {scene.Name}", () => _controller.ApplyScene(scene));
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Margin = new Thickness(0, 0, 8, 8);
            grid.Children.Add(button);
        }

        Add(grid);

        Add(Ui.Heading(Texts.Get("Templates.Yours")));
        if (settings.Templates.Count == 0)
        {
            Add(Ui.Note(Texts.Get("Templates.None")));
        }

        foreach (Scene scene in settings.Templates)
        {
            Add(Ui.Card(Ui.Row(
                new TextBlock { Text = $"{scene.Emoji}  {scene.Name}", VerticalAlignment = VerticalAlignment.Center, MinWidth = 220 },
                Ui.ActionButton(Texts.Get("Templates.CopyLink"), () =>
                {
                    var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    package.SetText(SceneCodec.ToLink(scene));
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
                    ShowStatus(status, InfoBarSeverity.Success, Texts.Get("Templates.LinkCopied"));
                }),
                Ui.ActionButton(Texts.Get("Templates.Export"), () => _ = ExportAsync(scene, status)),
                Ui.ActionButton(Texts.Get("Templates.Delete"), () =>
                {
                    _controller.Update(s => s with { Templates = [.. s.Templates.Where(t => t.Name != scene.Name)] });
                    BuildPage();
                }))));
        }

        Add(Ui.Heading(Texts.Get("Templates.SaveCurrent")));
        var name = new TextBox { Header = Texts.Get("Templates.Name"), PlaceholderText = Texts.Get("Templates.NameExample"), MaxLength = Scene.MaxNameLength, Width = 260 };
        var emoji = new TextBox { Header = Texts.Get("Templates.Emoji"), Text = "🌦️", MaxLength = 8, Width = 80 };
        Add(Ui.Row(name, emoji, Ui.ActionButton(Texts.Get("Templates.Save"), () =>
        {
            string title = string.IsNullOrWhiteSpace(name.Text) ? Texts.Get("Templates.DefaultName") : name.Text.Trim();
            _controller.Update(s => s with { Templates = [.. s.Templates.Where(t => t.Name != title), Scene.From(s, title, emoji.Text)] });
            BuildPage();
        })));

        Add(Ui.Heading(Texts.Get("Templates.OpenShared")));
        var link = new TextBox { Header = Texts.Get("Templates.Link"), PlaceholderText = SceneCodec.LinkPrefix + "…", Width = 420 };
        Add(Ui.Row(link, Ui.ActionButton(Texts.Get("Templates.ApplyLink"), () =>
        {
            if (SceneCodec.TryFromLink(link.Text.Trim(), out Scene scene, out string? error))
            {
                _controller.Update(s => scene.ApplyTo(s) with { Templates = [.. s.Templates.Where(t => t.Name != scene.Name), scene] });
                BuildPage();
            }
            else
            {
                ShowStatus(status, InfoBarSeverity.Error, error ?? Texts.Get("Templates.BadLink"));
            }
        })));
        Add(Ui.ActionButton(Texts.Get("Templates.Import"), () => _ = ImportAsync(status)));
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
        picker.FileTypeChoices.Add(Texts.Format("Templates.FileType", AppInfo.Name), [SceneCodec.FileExtension]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Win32Interop.GetWindowFromWindowId(AppWindow.Id));
        Windows.Storage.StorageFile? file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            await Windows.Storage.FileIO.WriteTextAsync(file, SceneCodec.ToJson(scene));
            ShowStatus(status, InfoBarSeverity.Success, Texts.Format("Common.SavedAs", file.Name));
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
            ShowStatus(status, InfoBarSeverity.Error, error ?? Texts.Get("Templates.BadFile"));
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
        Add(Ui.Note(Texts.Get("Sync.Intro")));
        AddSyncPausedBar();

        var details = new StackPanel { Spacing = 16, Visibility = sync.Enabled ? Visibility.Visible : Visibility.Collapsed };
        Add(Ui.Card(Ui.Stack(
            Ui.Toggle(Texts.Get("Sync.Enable"), sync.Enabled, on =>
            {
                details.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                UpdateSync(s => s with { Enabled = on });
            }),
            Ui.Note(Texts.Get("Sync.Privacy")))));
        Add(details);

        _syncStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        details.Children.Add(Ui.Card(Ui.Stack(
            new TextBlock { Text = Texts.Get("Sync.Now"), Style = Ui.Resource<Style>("BodyStrongTextBlockStyle") },
            _syncStatus,
            Ui.ActionButton(Texts.Get("Sync.UpdateNow"), _controller.RefreshWeatherNow))));
        UpdateSyncStatus();

        // Location: a city from the bundled list (searched on this PC) or the Windows location service.
        var cityPanel = new StackPanel { Spacing = 8, Visibility = sync.Source == LocationSource.City ? Visibility.Visible : Visibility.Collapsed };
        var chosen = new TextBlock { Text = sync.City is { } city ? Texts.Format("Sync.WeatherFor", $"{city.Name} ({city.Country})") : Texts.Get("Sync.NoCity") };
        cityPanel.Children.Add(chosen);
        cityPanel.Children.Add(CitySearch(chosen));
        details.Children.Add(Ui.Card(Ui.Stack(
            Ui.Choice(Texts.Get("Sync.Location"), [Texts.Get("Sync.Location.City"), Texts.Get("Sync.Location.Device")], (int)sync.Source, index =>
            {
                cityPanel.Visibility = index == (int)LocationSource.City ? Visibility.Visible : Visibility.Collapsed;
                UpdateSync(s => s with { Source = (LocationSource)index });
            }),
            cityPanel)));

        // Custom sync: a weight per element, and the inverted world.
        var weights = Ui.Stack(
            Ui.Toggle(Texts.Get("Sync.Invert"), sync.Invert, on => UpdateSync(s => s with { Invert = on })),
            Ui.Note(Texts.Get("Sync.Weights")));
        foreach (SyncElement element in Enum.GetValues<SyncElement>())
        {
            weights.Children.Add(Ui.SliderRow(Texts.Format("Common.Percent", Ui.Name(element)), 0, 100, sync.Weight(element) * 100, 5,
                value => UpdateSync(s => s.WithWeight(element, (float)(value / 100)))));
        }

        details.Children.Add(Ui.Card(weights));
        details.Children.Add(Ui.Note($"{Texts.Get("Credits.Weather")} {Texts.Get("Credits.Cities")}"));
    }

    private void UpdateSync(Func<WeatherSyncConfig, WeatherSyncConfig> change) =>
        _controller.Update(settings => settings with { WeatherSync = change(settings.WeatherSync) });

    /// <summary>The city search: suggestions from the bundled list while typing, nothing leaves the PC.</summary>
    private AutoSuggestBox CitySearch(TextBlock chosen)
    {
        var box = new AutoSuggestBox
        {
            Header = Texts.Get("Sync.City"),
            PlaceholderText = Texts.Get("Sync.CitySearch"),
            QueryIcon = new SymbolIcon(Symbol.Find),
            Width = 420,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        IReadOnlyList<City> results = [];

        void Choose(City city)
        {
            chosen.Text = Texts.Format("Sync.WeatherFor", city.DisplayName);
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
            lines.Add(Texts.Get("Sync.Unavailable"));
        }
        else if (!config.Enabled)
        {
            lines.Add(Texts.Get("Sync.Off"));
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
                    : Texts.Format("Sync.DeviceAt", place.Latitude.ToString("0.0", CultureInfo.CurrentCulture), place.Longitude.ToString("0.0", CultureInfo.CurrentCulture)));
            }

            if (sync.Current(now) is { } weather)
            {
                lines.Add(Describe(weather));
            }

            lines.Add(sync.State switch
            {
                WeatherSyncState.Live => Texts.Format("Sync.Live", sync.LastSuccess!.Value.ToLocalTime().ToString("t", CultureInfo.CurrentCulture), sync.NextAttempt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)),
                WeatherSyncState.Offline => Texts.Format("Sync.Offline", sync.LastError, sync.NextAttempt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)),
                _ => sync.LastError is { } error ? Texts.Format("Sync.NoWeather", error) : Texts.Get("Sync.Asking"),
            });
        }

        _syncStatus.Text = string.Join(Environment.NewLine, lines);
    }

    /// <summary>"Light rain showers, 3 °C, wind 2 m/s from the south-west".</summary>
    private static string Describe(WeatherSnapshot weather)
    {
        string symbol = weather.Symbol.Split('_')[0];
        bool thunder = symbol.EndsWith("andthunder", StringComparison.Ordinal);
        string plain = thunder ? symbol[..^"andthunder".Length] : symbol;
        string text = plain.Length == 0 ? Texts.Get("Weather.None")
            : Texts.Get($"Weather.{plain}") is { } known && known != $"Weather.{plain}" ? known
            : plain;
        if (thunder)
        {
            text += Texts.Get("Weather.Thunder");
        }

        string[] directions = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
        string from = Texts.Get($"Direction.{directions[(int)Math.Round(((weather.WindFromDirection % 360) + 360) % 360 / 45.0) % 8]}");
        return Texts.Format("Sync.Describe", text, weather.AirTemperature.ToString("0", CultureInfo.CurrentCulture), weather.WindSpeed.ToString("0", CultureInfo.CurrentCulture), from);
    }

    private void BuildVisitors()
    {
        VisitorConfig config = _controller.Settings.Visitors;
        Add(Ui.Note(Texts.Get("Visitors.Intro")));
        var status = new InfoBar { IsOpen = false, IsClosable = true };
        Add(status);
        string shortcut = _controller.Window.CollectShortcutRegistered
            ? Texts.Format("Visitors.Shortcut", Platform.Overlay.OverlayHost.CollectShortcut)
            : Texts.Format("Visitors.ShortcutTaken", Platform.Overlay.OverlayHost.CollectShortcut);
        Add(Ui.Card(Ui.Stack(
            Ui.Choice(Texts.Get("Visitors.Frequency"), [Texts.Get("Visitors.Frequency.Never"), Texts.Get("Visitors.Frequency.Rarely"), Texts.Get("Visitors.Frequency.Sometimes"), Texts.Get("Visitors.Frequency.Often")], (int)config.Frequency,
                index => _controller.Update(s => s with { Visitors = s.Visitors with { Frequency = (VisitorFrequency)index } })),
            Ui.Toggle(Texts.Get("Visitors.Interactive"), config.Interactive,
                on => _controller.Update(s => s with { Visitors = s.Visitors with { Interactive = on } })),
            Ui.Toggle(Texts.Get("Visitors.Announce"), config.Announce,
                on => _controller.Update(s => s with { Visitors = s.Visitors with { Announce = on } })),
            Ui.Note(shortcut),
            Ui.ActionButton(Texts.Get("Visitors.Invite"), () =>
            {
                if (!_controller.InviteVisitor())
                {
                    ShowStatus(status, InfoBarSeverity.Informational, Texts.Get("Visitors.NoRoom"));
                }
            }))));

        VisitorAlbum album = _controller.Album;
        Add(Ui.Heading(Texts.Get("Visitors.Collection")));
        Add(Ui.Note(string.Create(CultureInfo.CurrentCulture,
            Texts.Format("Visitors.Summary", album.TotalCollected, album.Points, album.BestCombo))));
        var grid = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 158, ItemHeight = 214 };
        foreach (VisitorProfile profile in VisitorCatalog.All)
        {
            int count = album.CountOf(profile.Kind);
            Image picture = Ui.VisitorImage(profile.Kind, 72);
            picture.Opacity = count > 0 ? 1 : 0.3;
            var card = Ui.Card(Ui.Stack(
                picture,
                new TextBlock { Text = profile.Name, Style = Ui.Resource<Style>("BodyStrongTextBlockStyle") },
                Ui.Note(count > 0 ? Texts.Format("Visitors.CollectedCount", count) : Texts.Get("Visitors.NotMet")),
                Ui.Note($"{Favourite(profile.Affinity)}, {Texts.Get($"Rarity.{profile.Rarity}")}")));
            card.Margin = new Thickness(0, 0, 8, 8);
            ToolTipService.SetToolTip(card, Texts.Get($"Visitor.{profile.Kind}"));
            grid.Children.Add(card);
        }

        Add(grid);

        Add(Ui.Heading(Texts.Get("Visitors.Achievements")));
        var achievements = new StackPanel { Spacing = 6 };
        foreach (Achievement achievement in Enum.GetValues<Achievement>())
        {
            (string title, string description) = Achievements.Describe(achievement);
            bool reached = album.Has(achievement);
            achievements.Children.Add(Ui.Row(
                new FontIcon { Glyph = reached ? "\uE73E" : "\uE739", Foreground = reached ? Ui.Resource<Microsoft.UI.Xaml.Media.Brush>("AccentTextFillColorPrimaryBrush") : Ui.Resource<Microsoft.UI.Xaml.Media.Brush>("TextFillColorTertiaryBrush") },
                new TextBlock { Text = title, Style = Ui.Resource<Style>("BodyStrongTextBlockStyle"), Width = 150 },
                Ui.Note(reached ? description : Texts.Format("Visitors.NotYet", description))));
        }

        Add(Ui.Card(achievements));

        Add(Ui.Heading(Texts.Get("Visitors.ShareCard")));
        Border shareCard = ShareCard(album);
        Add(shareCard);
        Add(Ui.Row(
            Ui.ActionButton(Texts.Get("Visitors.CopyCard"), async () =>
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
                ShowStatus(status, InfoBarSeverity.Success, Texts.Get("Visitors.CardCopied"));
            }),
            Ui.ActionButton(Texts.Get("Visitors.ShareCardButton"), async () =>
            {
                (byte[]? png, _, _) = await Ui.RenderPngAsync(shareCard);
                if (png is not null)
                {
                    await ShareAsync(png, Texts.Format("Visitors.CardTitle", AppInfo.Name));
                }
            }),
            Ui.ActionButton(Texts.Get("Visitors.SaveCard"), async () =>
            {
                (byte[]? png, _, _) = await Ui.RenderPngAsync(shareCard);
                if (png is null)
                {
                    return;
                }

                var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = Texts.Format("Visitors.CardFile", AppInfo.Name) };
                picker.FileTypeChoices.Add(Texts.Get("Common.PngPicture"), [".png"]);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, Win32Interop.GetWindowFromWindowId(AppWindow.Id));
                Windows.Storage.StorageFile? file = await picker.PickSaveFileAsync();
                if (file is not null)
                {
                    await Windows.Storage.FileIO.WriteBytesAsync(file, png);
                    ShowStatus(status, InfoBarSeverity.Success, Texts.Format("Common.SavedAs", file.Name));
                }
            })));
    }

    /// <summary>
    /// Opens Windows' share window with the card as a picture (PLAN.md, section 6). Desktop apps reach it through the
    /// window handle (<c>DataTransferManagerInterop</c>); the picture goes through a temporary file, which sharing targets
    /// read most reliably.
    /// </summary>
    private async Task ShareAsync(byte[] png, string title)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{AppInfo.Name}-visitors.png");
        await File.WriteAllBytesAsync(path, png);
        Windows.Storage.StorageFile file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
        nint window = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        Windows.ApplicationModel.DataTransfer.DataTransferManager manager = Windows.ApplicationModel.DataTransfer.DataTransferManagerInterop.GetForWindow(window);

        void OnDataRequested(Windows.ApplicationModel.DataTransfer.DataTransferManager sender, Windows.ApplicationModel.DataTransfer.DataRequestedEventArgs e)
        {
            sender.DataRequested -= OnDataRequested;
            e.Request.Data.Properties.Title = title;
            e.Request.Data.SetStorageItems([file]);
            e.Request.Data.SetBitmap(Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(file));
        }

        manager.DataRequested += OnDataRequested;
        Windows.ApplicationModel.DataTransfer.DataTransferManagerInterop.ShowShareUIForWindow(window);
    }

    private static string Favourite(WeatherAffinity affinity) => affinity switch
    {
        WeatherAffinity.Any => Texts.Get("Affinity.Any"),
        WeatherAffinity.Leaves => Texts.Get("Affinity.Leaves"),
        WeatherAffinity.Sun => Texts.Get("Affinity.Sun"),
        _ => Texts.Get($"Affinity.{affinity}"),
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
        content.Children.Add(new TextBlock { Text = Texts.Format("Visitors.CardTitle", AppInfo.Name), Style = Ui.Resource<Style>("SubtitleTextBlockStyle"), Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 46, 52, 64)) });
        content.Children.Add(visitors);
        content.Children.Add(new TextBlock
        {
            Text = Texts.Format("Visitors.CardLine", album.Points, album.TotalCollected, album.Achievements.Count, Enum.GetValues<Achievement>().Length),
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
            Ui.Toggle(Texts.Get("Sound.Enable"), !sound.Muted, on => _controller.Update(s => s with { Sound = s.Sound with { Muted = !on } })),
            Ui.SliderRow(Texts.Get("Sound.Volume"), 0, 100, sound.Master * 100, 5, value => _controller.Update(s => s with { Sound = s.Sound with { Master = (float)(value / 100) } })),
            Ui.SliderRow(Texts.Get("Sound.Rain"), 0, 100, sound.Rain * 100, 5, value => _controller.Update(s => s with { Sound = s.Sound with { Rain = (float)(value / 100) } })),
            Ui.SliderRow(Texts.Get("Sound.Wind"), 0, 100, sound.Wind * 100, 5, value => _controller.Update(s => s with { Sound = s.Sound with { Wind = (float)(value / 100) } })),
            Ui.SliderRow(Texts.Get("Sound.Visitors"), 0, 100, sound.Visitors * 100, 5, value => _controller.Update(s => s with { Sound = s.Sound with { Visitors = (float)(value / 100) } })),
            Ui.Note(Texts.Get("Sound.Note")))));
    }

    private void BuildDisplays()
    {
        Add(Ui.Note(Texts.Get("Displays.Intro")));
        foreach (MonitorInfo monitor in MonitorService.GetMonitors())
        {
            DisplayConfig display = _controller.Settings.Display(monitor.DeviceName);
            string name = string.Create(
                CultureInfo.InvariantCulture,
                $"{monitor.DeviceName.TrimStart('\\', '.')}{(monitor.IsPrimary ? Texts.Get("Displays.Primary") : string.Empty)}: {monitor.Bounds.Width} x {monitor.Bounds.Height}, {Math.Round(monitor.ScaleFactor * 100)} %");
            Add(Ui.Card(Ui.Stack(
                Ui.Toggle(name, display.Enabled, on => UpdateDisplay(monitor.DeviceName, d => d with { Enabled = on })),
                Ui.SliderRow(Texts.Get("Effects.Intensity"), 10, 100, display.Intensity * 100, 5, value => UpdateDisplay(monitor.DeviceName, d => d with { Intensity = (float)(value / 100) })))));
        }
    }

    private void UpdateDisplay(string deviceName, Func<DisplayConfig, DisplayConfig> change) => _controller.Update(s => s with
    {
        Displays = [.. s.Displays.Where(d => !string.Equals(d.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase)), change(s.Display(deviceName))],
    });

    private void BuildApps()
    {
        Add(Ui.Note(Texts.Get("Apps.Intro")));
        string[] actions = [Texts.Get("Apps.Pause"), Texts.Get("Apps.Hide")];
        foreach (AppRule rule in _controller.Settings.AppRules)
        {
            AppRule current = rule;
            Add(Ui.Card(Ui.Row(
                new TextBlock { Text = rule.ProcessName, VerticalAlignment = VerticalAlignment.Center, MinWidth = 200 },
                new TextBlock { Text = actions[(int)rule.Action], VerticalAlignment = VerticalAlignment.Center, MinWidth = 260 },
                Ui.ActionButton(Texts.Get("Apps.Remove"), () =>
                {
                    _controller.Update(s => s with { AppRules = [.. s.AppRules.Where(r => r != current)] });
                    BuildPage();
                }))));
        }

        Add(Ui.Heading(Texts.Get("Apps.AddRule")));
        var app = new ComboBox { Header = Texts.Get("Apps.App"), IsEditable = true, PlaceholderText = "obs64.exe", MinWidth = 260 };
        foreach (string process in RunningApps())
        {
            app.Items.Add(process);
        }

        int action = 0;
        Add(Ui.Row(
            app,
            Ui.Choice(Texts.Get("Apps.Rule"), actions, 0, index => action = index),
            Ui.ActionButton(Texts.Get("Apps.Add"), () =>
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
                Texts.Get("Performance.Smoothness"),
                [Texts.Get("Performance.Auto"), Texts.Get("Performance.LeastEnergy"), Texts.Get("Performance.Balanced"), Texts.Get("Performance.Smoothest")],
                (int)performance.Smoothness,
                index => _controller.Update(s => s with { Performance = s.Performance with { Smoothness = (Smoothness)index } })),
            Ui.Choice(
                Texts.Get("Performance.Battery"),
                [Texts.Get("Performance.KeepGoing"), Texts.Get("Performance.Reduce"), Texts.Get("Performance.Pause")],
                (int)performance.Battery,
                index => _controller.Update(s => s with { Performance = s.Performance with { Battery = (BatteryBehavior)index } })))));

        Add(Ui.Heading(Texts.Get("Performance.Now")));
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
            text = Texts.Get("Performance.NotRunning");
        }
        else if (!status.IsAnimating)
        {
            text = Texts.Get("Performance.Idle");
        }
        else
        {
            double fps = _lastFrames < 0 ? 0 : (status.AnimationFrames - _lastFrames) / Math.Max((now - _lastSample).TotalSeconds, 0.001);
            text = Texts.Format("Performance.Live", fps, status.FrameMilliseconds, status.SimulationMilliseconds, status.Sprites);
        }

        RuntimeDecision decision = _controller.Decision;
        string cap = decision.FrameRateCap == 0 ? Texts.Get("Performance.DisplayRate") : string.Create(CultureInfo.InvariantCulture, $"{decision.FrameRateCap} fps");
        _liveText.Text = Texts.Format("Performance.Status", text, cap, decision.Reason ?? Texts.Get("Performance.Running"));
        _lastFrames = status?.AnimationFrames ?? -1;
        _lastSample = now;
    }

    private void BuildAdvanced()
    {
        AdvancedConfig advanced = _controller.Settings.Advanced;
        var stack = Ui.Stack(
            Ui.Toggle(Texts.Get("Advanced.Capture"), advanced.ExcludeFromCapture, on => _controller.Update(s => s with { Advanced = s.Advanced with { ExcludeFromCapture = on } })),
            Ui.Toggle(Texts.Get("Advanced.Autostart"), advanced.Autostart, on => _controller.Update(s => s with { Advanced = s.Advanced with { Autostart = on } })),
            Ui.Choice(
                Texts.Get("Advanced.ReducedMotion"),
                [Texts.Get("Advanced.ReducedMotion.Follow"), Texts.Get("Advanced.ReducedMotion.Always"), Texts.Get("Advanced.ReducedMotion.Never")],
                (int)advanced.ReducedMotion,
                index => _controller.Update(s => s with { Advanced = s.Advanced with { ReducedMotion = (ReducedMotion)index } })),
            Ui.Toggle(Texts.Get("Advanced.HighContrast"), advanced.PauseInHighContrast,
                on => _controller.Update(s => s with { Advanced = s.Advanced with { PauseInHighContrast = on } })),
            Ui.Choice(
                Texts.Get("Advanced.Language"),
                [Texts.Get("Advanced.Language.System"), "English", "Deutsch"],
                advanced.Language switch { "en" => 1, "de" => 2, _ => 0 },
                index =>
                {
                    _controller.Update(s => s with { Advanced = s.Advanced with { Language = index switch { 1 => "en", 2 => "de", _ => string.Empty } } });

                    // The page is built again in the new language; menus and the flyout follow when they open next.
                    DispatcherQueue.TryEnqueue(BuildPage);
                }));
        if (Autostart.LastError is { } error)
        {
            stack.Children.Add(Ui.Note(error));
        }

        Add(Ui.Card(stack));
        Add(Ui.Heading(Texts.Get("Advanced.Troubleshooting")));
        Add(Ui.Row(
            Ui.ActionButton(Texts.Get("Advanced.Diagnostics"), _controller.Window.ShowAndActivate),
            Ui.ActionButton(Texts.Get("Advanced.Reset"), () => _ = ConfirmResetAsync())));
    }

    private async Task ConfirmResetAsync()
    {
        var dialog = new ContentDialog
        {
            Title = Texts.Get("Advanced.ResetTitle"),
            Content = Texts.Get("Advanced.ResetText"),
            PrimaryButtonText = Texts.Get("Advanced.ResetConfirm"),
            CloseButtonText = Texts.Get("Common.Cancel"),
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
            Ui.Note(Texts.Get("About.Tagline")))));
        Add(Ui.Heading(Texts.Get("About.Privacy")));
        Add(Ui.Card(Ui.Note(
            Texts.Get("About.PrivacyText"))));
        Add(Ui.Heading(Texts.Get("About.Credits")));
        Add(Ui.Card(Ui.Stack(
            Ui.Note(Texts.Get("About.Assets")),
            Ui.Note(Texts.Get("Credits.Weather")),
            Ui.Note(Texts.Get("Credits.Cities")))));
    }
}
