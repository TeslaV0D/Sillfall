using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Weatherly.Core.Imaging;
using Weatherly.Core.Localization;
using Weatherly.Core.Settings;
using Weatherly.Core.Visitors;

namespace Weatherly.App;

/// <summary>
/// Building blocks of the settings window and the flyout, built in code so every page uses the same controls.
/// Handlers are attached after the initial value is set, so building a page never changes a setting.
/// </summary>
internal static class Ui
{
    public static string Name(EffectKind kind) => kind switch
    {
        EffectKind.Rain => Texts.Get("Effect.Rain"),
        EffectKind.Snow => Texts.Get("Effect.Snow"),
        EffectKind.Leaves => Texts.Get("Effect.Leaves"),
        EffectKind.Fog => Texts.Get("Effect.Fog"),
        EffectKind.Sun => Texts.Get("Effect.Sun"),
        EffectKind.Grass => Texts.Get("Effect.Grass"),
        _ => kind.ToString(),
    };

    /// <summary>
    /// The icon of an effect: our own 16 × 16 drawings (ASSETS.md), because the system icon font has no weather symbols.
    /// A new element on every call, since an element can only have one parent.
    /// </summary>
    public static PathIcon Icon(EffectKind kind)
    {
        var icon = new PathIcon
        {
            Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), IconPath(kind)),
            Width = 16,
            Height = 16,
        };

        // Decoration next to the effect's name: Narrator reads the name, not the drawing.
        AutomationProperties.SetAccessibilityView(icon, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        return icon;
    }

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
        WeatherBlend.EffectOf(element) is { } kind ? Name(kind) : Texts.Get("Effects.Wind");

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
            Title = Texts.Get("Sync.PausedTitle"),
            Message = Texts.Format(sync.Paused.Count == 1 ? "Sync.PausedOne" : "Sync.PausedMany", string.Join(", ", sync.Paused.Select(Name))),
        };
        bar.ActionButton = ActionButton(Texts.Get("Sync.Resume"), () =>
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
        AutomationProperties.SetName(slider, header);
        slider.ValueChanged += (_, e) => changed(e.NewValue);
        return slider;
    }

    public static ToggleSwitch Toggle(string header, bool on, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { Header = header, IsOn = on, OnContent = Texts.Get("Common.On"), OffContent = Texts.Get("Common.Off") };
        AutomationProperties.SetName(toggle, header);
        toggle.Toggled += (_, _) => changed(toggle.IsOn);
        return toggle;
    }

    public static ComboBox Choice(string header, IReadOnlyList<string> items, int selected, Action<int> changed)
    {
        var combo = new ComboBox { Header = header, MinWidth = 260 };
        AutomationProperties.SetName(combo, header);
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
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>A link that opens in the browser. Narrator reads <paramref name="text"/>.</summary>
    public static HyperlinkButton Link(string text, string url)
    {
        var link = new HyperlinkButton
        {
            Content = text,
            NavigateUri = new Uri(url),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(link, text);
        return link;
    }

    /// <summary>
    /// Names an expander for Narrator. The focus lands on the toggle button in its header, a part of the template, which
    /// does not take the expander's name: it gets the name once the template is applied.
    /// </summary>
    public static void NameExpander(Expander expander, string name)
    {
        ArgumentNullException.ThrowIfNull(expander);
        AutomationProperties.SetName(expander, name);
        expander.Loaded += (_, _) =>
        {
            if (FindDescendant<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>(expander) is { } header)
            {
                AutomationProperties.SetName(header, name);
            }
        };
    }

    private static T? FindDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }

    public static T Resource<T>(string key)
        where T : class => (T)Application.Current.Resources[key];

    /// <summary>
    /// Renders <paramref name="element"/> (it must be on screen) as a PNG in memory. Returns the bytes and the size; no
    /// bytes when nothing was rendered.
    /// </summary>
    public static async Task<(byte[]? Png, int Width, int Height)> RenderPngAsync(UIElement element)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;
        if (width == 0 || height == 0)
        {
            return (null, 0, 0);
        }

        // BGRA, premultiplied: what the PNG encoder expects.
        byte[] pixels = (await bitmap.GetPixelsAsync()).ToArray();
        return (PngEncoder.Encode(pixels, width, height), width, height);
    }

    /// <summary>
    /// Renders <paramref name="element"/> (it must be on screen) and writes it as a PNG. For the smoke test's
    /// pictures of the settings pages. Returns the size, or (0, 0) when nothing was rendered.
    /// </summary>
    public static async Task<(int Width, int Height)> SavePngAsync(UIElement element, string path)
    {
        (byte[]? png, int width, int height) = await RenderPngAsync(element);
        if (png is null)
        {
            return (0, 0);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, png);
        return (width, height);
    }

    /// <summary>A visitor's drawing as an image (the same SVG the overlay draws), <paramref name="size"/> pixels square.</summary>
    public static Image VisitorImage(VisitorKind kind, double size)
    {
        var source = new SvgImageSource { RasterizePixelWidth = size * 2, RasterizePixelHeight = size * 2 };
        var image = new Image { Source = source, Width = size, Height = size };
        AutomationProperties.SetName(image, VisitorCatalog.Get(kind).Name);
        _ = LoadSvgAsync(source, Render.VisitorDrawings.Svg(kind));
        return image;
    }

    private static async Task LoadSvgAsync(SvgImageSource source, string svg)
    {
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(System.Text.Encoding.UTF8.GetBytes(svg));
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        await source.SetSourceAsync(stream);
    }
}
