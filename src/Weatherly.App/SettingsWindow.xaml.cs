using System.Globalization;
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

    private readonly AppController _controller;
    private readonly DispatcherQueueTimer _liveTimer;
    private string _page = "effects";
    private bool _quitting;
    private TextBlock? _liveText;
    private long _lastFrames = -1;
    private DateTime _lastSample;

    internal SettingsWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();

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
        AppSettings settings = _controller.Settings;
        foreach (EffectKind kind in Enum.GetValues<EffectKind>())
        {
            EffectConfig config = settings.Effect(kind);
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(Ui.SliderRow("Intensity (%)", 0, 100, config.Intensity * 100, 5, value => UpdateEffect(kind, c => c with { Intensity = (float)(value / 100) })));

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
            Ui.SliderRow("Wind (px/s, negative blows to the left)", -400, 400, settings.Wind.Speed, 10, value => _controller.Update(s => s with { Wind = s.Wind with { Speed = (float)value } })),
            Ui.SliderRow("Gusts (%)", 0, 100, settings.Wind.Gustiness * 100, 5, value => _controller.Update(s => s with { Wind = s.Wind with { Gustiness = (float)(value / 100) } })))));
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

    private void BuildSync()
    {
        Add(Ui.Card(Ui.Stack(
            Ui.Note("Real weather comes with the next milestone (M10): the effects follow the weather at your location or a city you choose, with an invert mode and a weight per effect. Moving a slider pauses the sync for that effect."),
            Ui.Note("Your location stays rough: coordinates are rounded to about 11 km before anything is sent."))));
    }

    private void BuildVisitors()
    {
        Add(Ui.Card(Ui.Note("Visitors are small characters of our own that drop by on your windows. They arrive with milestone M11: how often they come, collecting them and a collection to look at.")));
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
            "App rules use the app's file name, on this PC only. Nothing is collected or sent; the weather sync (M10) will send a rounded location to the weather service and nothing else.")));
        Add(Ui.Heading("Credits"));
        Add(Ui.Card(Ui.Note("All graphics and sounds are made by the app itself or for it; see ASSETS.md. Weather data will come from MET Norway (M10).")));
    }
}
