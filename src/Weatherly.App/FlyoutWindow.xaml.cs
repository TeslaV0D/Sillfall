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
    private const int FlyoutHeight = 600;

    private readonly AppController _controller;
    private readonly Action _openSettings;
    private bool _quitting;

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
        float scale = MonitorService.GetMonitors().FirstOrDefault(monitor => monitor.IsPrimary)?.ScaleFactor ?? 1f;
        int width = (int)Math.Round(FlyoutWidth * scale);
        int height = (int)Math.Round(FlyoutHeight * scale);
        int margin = (int)Math.Round(12 * scale);
        RectInt32 workArea = DisplayArea.Primary.WorkArea;
        AppWindow.MoveAndResize(new RectInt32(
            workArea.X + workArea.Width - width - margin,
            workArea.Y + workArea.Height - height - margin,
            width,
            height));
        Build();
        AppWindow.Show();
        Activate();
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

        Host.Children.Add(Ui.SliderRow("Wind", -400, 400, settings.Wind.Speed, 10, value => _controller.Update(s => s with { Wind = s.Wind with { Speed = (float)value } }), width: 330));

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

        Host.Children.Add(Ui.Note("Weather sync: arrives with the next update."));
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
            Content = Ui.Row(new FontIcon { Glyph = Ui.Glyph(kind) }, new TextBlock { Text = Ui.Name(kind) }),
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
