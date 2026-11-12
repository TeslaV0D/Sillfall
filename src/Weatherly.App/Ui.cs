using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Weatherly.Core.Imaging;
using Weatherly.Core.Settings;

namespace Weatherly.App;

/// <summary>
/// Building blocks of the settings window and the flyout, built in code so every page uses the same controls.
/// Handlers are attached after the initial value is set, so building a page never changes a setting.
/// </summary>
internal static class Ui
{
    public static string Name(EffectKind kind) => kind switch
    {
        EffectKind.Rain => "Rain",
        EffectKind.Snow => "Snow",
        EffectKind.Leaves => "Leaves",
        EffectKind.Fog => "Fog",
        EffectKind.Sun => "Sunlight",
        EffectKind.Grass => "Grass",
        _ => kind.ToString(),
    };

    /// <summary>
    /// The icon of an effect: our own 16 × 16 drawings (ASSETS.md), because the system icon font has no weather symbols.
    /// A new element on every call, since an element can only have one parent.
    /// </summary>
    public static PathIcon Icon(EffectKind kind) => new()
    {
        Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), IconPath(kind)),
        Width = 16,
        Height = 16,
    };

    /// <summary>Path markup of <see cref="Icon"/>. <c>F1</c> fills overlapping parts (non-zero rule).</summary>
    private static string IconPath(EffectKind kind) => kind switch
    {
        EffectKind.Rain => "F1 M8,1.2 C8,1.2 3.4,6.8 3.4,10.1 A4.6,4.6 0 0 0 12.6,10.1 C12.6,6.8 8,1.2 8,1.2 Z",
        EffectKind.Snow => "F1 M7.25,1 L7.25,15 L8.75,15 L8.75,1 Z M1.56,5.15 L13.69,12.15 L14.44,10.85 L2.31,3.85 Z "
            + "M13.69,3.85 L1.56,10.85 L2.31,12.15 L14.44,5.15 Z",
        EffectKind.Leaves => "M2.5,13.5 C2.5,6 6,2.5 13.5,2.5 C13.5,10 10,13.5 2.5,13.5 Z M4.2,12.5 L11.6,5.1 L10.9,4.4 L3.5,11.8 Z",
        EffectKind.Fog => "F1 M2.8,3.4 H13.2 A0.8,0.8 0 0 1 13.2,5 H2.8 A0.8,0.8 0 0 1 2.8,3.4 Z "
            + "M1.8,7.2 H11.2 A0.8,0.8 0 0 1 11.2,8.8 H1.8 A0.8,0.8 0 0 1 1.8,7.2 Z "
            + "M4.8,11 H14.2 A0.8,0.8 0 0 1 14.2,12.6 H4.8 A0.8,0.8 0 0 1 4.8,11 Z",
        EffectKind.Sun => "F1 M4.6,8 A3.4,3.4 0 1 1 11.4,8 A3.4,3.4 0 1 1 4.6,8 Z "
            + "M13.2,8.75 L15.4,8.75 L15.4,7.25 L13.2,7.25 Z M11.15,12.21 L12.7,13.76 L13.76,12.7 L12.21,11.15 Z "
            + "M7.25,13.2 L7.25,15.4 L8.75,15.4 L8.75,13.2 Z M3.79,11.15 L2.24,12.7 L3.3,13.76 L4.85,12.21 Z "
            + "M2.8,7.25 L0.6,7.25 L0.6,8.75 L2.8,8.75 Z M4.85,3.79 L3.3,2.24 L2.24,3.3 L3.79,4.85 Z "
            + "M8.75,2.8 L8.75,0.6 L7.25,0.6 L7.25,2.8 Z M12.21,4.85 L13.76,3.3 L12.7,2.24 L11.15,3.79 Z",
        EffectKind.Grass => "F1 M1.6,15 Q3.6,10 1.4,4.6 Q5.4,9 5,15 Z M5.6,15 Q7.4,8 7.6,1.2 Q10,8 9.8,15 Z "
            + "M10.4,15 Q11.4,9.6 14.8,5.4 Q13,10.4 13.8,15 Z",
        _ => "M2,2 H14 V14 H2 Z",
    };

    public static string Name(SyncElement element) =>
        WeatherBlend.EffectOf(element) is { } kind ? Name(kind) : "Wind";

    /// <summary>
    /// The banner for a weather sync that the user paused for some elements by moving their sliders, with "Resume sync".
    /// <see langword="null"/> when nothing is paused.
    /// </summary>
    public static InfoBar? SyncPausedBar(AppController controller, Action resumed)
    {
        ArgumentNullException.ThrowIfNull(controller);
        WeatherSyncConfig sync = controller.Settings.WeatherSync;
        if (!sync.Enabled || sync.Paused.Count == 0)
        {
            return null;
        }

        var bar = new InfoBar
        {
            IsOpen = true,
            IsClosable = false,
            Severity = InfoBarSeverity.Warning,
            Title = "Weather sync paused",
            Message = $"{string.Join(", ", sync.Paused.Select(Name))} keep{(sync.Paused.Count == 1 ? "s" : string.Empty)} the value you set by hand.",
        };
        bar.ActionButton = ActionButton("Resume sync", () =>
        {
            controller.ResumeSync();
            resumed();
        });
        return bar;
    }

    public static TextBlock Heading(string text) => new()
    {
        Text = text,
        Style = Resource<Style>("SubtitleTextBlockStyle"),
        Margin = new Thickness(0, 12, 0, 0),
    };

    public static TextBlock Title(string text) => new() { Text = text, Style = Resource<Style>("TitleTextBlockStyle") };

    public static TextBlock Note(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Resource<Brush>("TextFillColorSecondaryBrush"),
    };

    /// <summary>A rounded card around <paramref name="content"/>, like the cards of the Windows settings.</summary>
    public static Border Card(UIElement content) => new()
    {
        Background = Resource<Brush>("CardBackgroundFillColorDefaultBrush"),
        BorderBrush = Resource<Brush>("CardStrokeColorDefaultBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(16, 12, 16, 12),
        Child = content,
    };

    public static StackPanel Stack(params UIElement[] children)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (UIElement child in children)
        {
            panel.Children.Add(child);
        }

        return panel;
    }

    public static StackPanel Row(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (UIElement child in children)
        {
            panel.Children.Add(child);
        }

        return panel;
    }

    public static Slider SliderRow(string header, double minimum, double maximum, double value, double step, Action<double> changed, double width = 420)
    {
        var slider = new Slider
        {
            Header = header,
            Minimum = minimum,
            Maximum = maximum,
            StepFrequency = step,
            Value = Math.Clamp(value, minimum, maximum),
            Width = width,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        slider.ValueChanged += (_, e) => changed(e.NewValue);
        return slider;
    }

    public static ToggleSwitch Toggle(string header, bool on, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { Header = header, IsOn = on };
        toggle.Toggled += (_, _) => changed(toggle.IsOn);
        return toggle;
    }

    public static ComboBox Choice(string header, IReadOnlyList<string> items, int selected, Action<int> changed)
    {
        var combo = new ComboBox { Header = header, MinWidth = 260 };
        foreach (string item in items)
        {
            combo.Items.Add(item);
        }

        combo.SelectedIndex = Math.Clamp(selected, 0, Math.Max(items.Count - 1, 0));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
            {
                changed(combo.SelectedIndex);
            }
        };
        return combo;
    }

    public static Button ActionButton(string text, Action click)
    {
        var button = new Button { Content = text };
        button.Click += (_, _) => click();
        return button;
    }

    public static T Resource<T>(string key)
        where T : class => (T)Application.Current.Resources[key];

    /// <summary>
    /// Renders <paramref name="element"/> (it must be on screen) and writes it as a PNG. For the smoke test's
    /// pictures of the settings pages. Returns the size, or (0, 0) when nothing was rendered.
    /// </summary>
    public static async Task<(int Width, int Height)> SavePngAsync(UIElement element, string path)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;
        if (width == 0 || height == 0)
        {
            return (0, 0);
        }

        // BGRA, premultiplied: what the PNG encoder expects.
        byte[] pixels = (await bitmap.GetPixelsAsync()).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, PngEncoder.Encode(pixels, width, height));
        return (width, height);
    }
}
