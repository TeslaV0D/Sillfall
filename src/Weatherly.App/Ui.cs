using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

    /// <summary>Segoe Fluent Icons glyph of an effect.</summary>
    public static string Glyph(EffectKind kind) => kind switch
    {
        EffectKind.Rain => "",
        EffectKind.Snow => "",
        EffectKind.Leaves => "",
        EffectKind.Fog => "",
        EffectKind.Sun => "",
        EffectKind.Grass => "",
        _ => "",
    };

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
