using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Weatherly.Core;
using Weatherly.Core.Scenes;
using Weatherly.Core.Settings;
using Weatherly.Platform.Displays;
using Windows.Graphics;

namespace Weatherly.App;

/// <summary>
/// Quick settings in the style of the Windows 11 flyouts (PLAN.md, section 7): master switch, a tile per effect
/// with a switch and an intensity slider, wind, templates and the way to the full settings. It opens above the tray
/// icon and hides as soon as it loses the focus.
/// </summary>
public sealed partial class FlyoutWindow : Window
{
    private const int FlyoutWidth = 380;

    /// <summary>Height before the content is measured; afterwards the flyout is as high as its content.</summary>
    private const int FlyoutHeight = 600;

    /// <summary>Padding of <c>RootGrid</c> around the content, top and bottom together.</summary>
    private const int ContentPadding = 32;

    /// <summary>Gap between the flyout and the edges of the work area.</summary>
    private const int EdgeMargin = 12;

    private readonly AppController _controller;
    private readonly Action _openSettings;
    private bool _quitting;
    private float _scale = 1f;

    internal FlyoutWindow(AppController controller, Action openSettings)
    {
        _controller = controller;
        _openSettings = openSettings;
        InitializeComponent();

        Title = AppInfo.Name;
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(true, false);
        }

        // The content sits in a scroll viewer, so its height is the height it wants: the flyout grows and shrinks with it.
        Host.SizeChanged += (_, e) =>
        {
            if (AppWindow.IsVisible)
            {
                Place(e.NewSize.Height + ContentPadding);
            }
        };

        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated && !StayOpen)
            {
                AppWindow.Hide();
            }
        };
        AppWindow.Closing += (sender, e) =>
        {
            if (!_quitting)
            {
                e.Cancel = true;
                sender.Hide();
            }
        };
    }

    /// <summary>Keeps the flyout open when it loses the focus (smoke test pictures).</summary>
    public bool StayOpen { get; set; }

    public bool IsShown => AppWindow.IsVisible;

    /// <summary>Opens the flyout in the bottom right corner of the primary monitor's work area, above the tray.</summary>
    public void ShowNearTray()
    {
        _scale = MonitorService.GetMonitors().FirstOrDefault(monitor => monitor.IsPrimary)?.ScaleFactor ?? 1f;
        Build();
        Place(Host.ActualHeight > 0 ? Host.ActualHeight + ContentPadding : FlyoutHeight);
        AppWindow.Show();
        Activate();
    }

    /// <summary>Puts the flyout into the bottom right corner of the primary work area with the given client height (DIP).</summary>
    private void Place(double height)
    {
        RectInt32 workArea = DisplayArea.Primary.WorkArea;
        int margin = (int)Math.Round(EdgeMargin * _scale);
        int chrome = Math.Max(AppWindow.Size.Height - AppWindow.ClientSize.Height, 0);
        int width = (int)Math.Round(FlyoutWidth * _scale);
        int pixels = Math.Min((int)Math.Ceiling(height * _scale) + chrome, workArea.Height - (2 * margin));
        AppWindow.MoveAndResize(new RectInt32(
            workArea.X + workArea.Width - width - margin,
            workArea.Y + workArea.Height - pixels - margin,
            width,
            pixels));
    }

    /// <summary>Writes the flyout as a PNG (smoke test). Returns the rendered size.</summary>
    public Task<(int Width, int Height)> SaveAsync(string path) => Ui.SavePngAsync(RootGrid, path);

    public void Quit()
    {
        _quitting = true;
        Close();
    }

    private void Build()
    {
        Host.Children.Clear();
        AppSettings settings = _controller.Settings;

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = AppInfo.Name, Style = Ui.Resource<Style>("SubtitleTextBlockStyle"), VerticalAlignment = VerticalAlignment.Center });
        var master = new ToggleSwitch { IsOn = settings.Enabled, OnContent = "On", OffContent = "Off", MinWidth = 0 };
        master.Toggled += (_, _) => _controller.Update(s => s with { Enabled = master.IsOn });
        Grid.SetColumn(master, 1);
        header.Children.Add(master);
        Host.Children.Add(header);

        if (_controller.Decision.Reason is { } reason)
        {
            Host.Children.Add(Ui.Note(reason));
        }

        var tiles = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        tiles.ColumnDefinitions.Add(new ColumnDefinition());
        tiles.ColumnDefinitions.Add(new ColumnDefinition());
        EffectKind[] kinds = Enum.GetValues<EffectKind>();
        for (int row = 0; row < (kinds.Length + 1) / 2; row++)
        {
            tiles.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (int i = 0; i < kinds.Length; i++)
        {
            FrameworkElement tile = Tile(kinds[i], settings.Effect(kinds[i]).Intensity);
            Grid.SetColumn(tile, i % 2);
            Grid.SetRow(tile, i / 2);
            tiles.Children.Add(tile);
        }

        Host.Children.Add(tiles);

        Host.Children.Add(Ui.SliderRow("Wind", -400, 400, settings.Wind.Speed, 10, value => _controller.SetWind(w => w with { Speed = (float)value }), width: 330));

        IReadOnlyList<Scene> templates = _controller.AllTemplates;
        var picker = new ComboBox { Header = "Template", PlaceholderText = "Apply a template", MinWidth = 330 };
        foreach (Scene scene in templates)
        {
            picker.Items.Add($"{scene.Emoji}  {scene.Name}");
        }

        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedIndex >= 0 && picker.SelectedIndex < templates.Count)
            {
                _controller.ApplyScene(templates[picker.SelectedIndex]);
                Build();
            }
        };
        Host.Children.Add(picker);

        Host.Children.Add(Ui.Toggle("Real weather", settings.WeatherSync.Enabled, on =>
            _controller.Update(s => s with { WeatherSync = s.WeatherSync with { Enabled = on } })));
        if (Ui.SyncPausedBar(_controller, Build) is { } paused)
        {
            Host.Children.Add(paused);
        }

        Host.Children.Add(Ui.ActionButton("More settings…", () =>
        {
            AppWindow.Hide();
            _openSettings();
        }));
    }

    /// <summary>An effect: a switch with its name and a slider for the intensity.</summary>
    private Border Tile(EffectKind kind, float intensity)
    {
        var toggle = new ToggleButton
        {
            IsChecked = intensity > 0f,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = Ui.Row(Ui.Icon(kind), new TextBlock { Text = Ui.Name(kind) }),
        };
        var slider = new Slider { Minimum = 0, Maximum = 100, StepFrequency = 5, Value = Math.Round(intensity * 100) };
        slider.ValueChanged += (_, e) =>
        {
            toggle.IsChecked = e.NewValue > 0;
            _controller.SetIntensity(kind, (float)(e.NewValue / 100));
        };
        toggle.Click += (_, _) => slider.Value = toggle.IsChecked == true ? Math.Max(slider.Value, 50) : 0;
        return Ui.Card(Ui.Stack(toggle, slider));
    }
}
