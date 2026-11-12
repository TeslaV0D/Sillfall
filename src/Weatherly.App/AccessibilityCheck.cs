using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Weatherly.App;

/// <summary>
/// A screen reader check for the smoke test (PLAN.md, M12): every control one can reach with Tab must tell Narrator
/// what it is. Walks the visible tree and asks UI Automation for each focusable control's name; the parts inside a
/// control (a slider's thumb, a combo box's button) belong to it and are not checked separately.
/// </summary>
internal static class AccessibilityCheck
{
    /// <summary>The focusable controls below <paramref name="root"/> without a name, described for the report.</summary>
    public static List<string> UnnamedControls(DependencyObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var unnamed = new List<string>();
        Walk(root, unnamed);
        return unnamed;
    }

    private static void Walk(DependencyObject node, List<string> unnamed)
    {
        if (node is UIElement { Visibility: Visibility.Collapsed })
        {
            return;
        }

        if (node is Control { IsTabStop: true, IsEnabled: true } control && IsInteractive(control))
        {
            AutomationPeer? peer = FrameworkElementAutomationPeer.FromElement(control) ?? FrameworkElementAutomationPeer.CreatePeerForElement(control);
            if (string.IsNullOrWhiteSpace(peer?.GetName()))
            {
                unnamed.Add(Describe(control));
            }

            return;
        }

        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            Walk(VisualTreeHelper.GetChild(node, i), unnamed);
        }
    }

    private static bool IsInteractive(Control control) =>
        control is ButtonBase or ToggleSwitch or Slider or ComboBox or TextBox or AutoSuggestBox or ListViewItem or NavigationViewItem;

    /// <summary>"Slider in 'Rain'": the type and the nearest text above it, to find it again.</summary>
    private static string Describe(Control control)
    {
        string near = string.Empty;
        for (DependencyObject? parent = VisualTreeHelper.GetParent(control); parent is not null && near.Length == 0; parent = VisualTreeHelper.GetParent(parent))
        {
            near = FindText(parent) ?? string.Empty;
        }

        return near.Length > 0 ? $"{control.GetType().Name} near '{near}'" : control.GetType().Name;
    }

    private static string? FindText(DependencyObject node)
    {
        if (node is TextBlock { Text.Length: > 0 } text)
        {
            return text.Text.Length > 40 ? text.Text[..40] : text.Text;
        }

        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            if (FindText(VisualTreeHelper.GetChild(node, i)) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
