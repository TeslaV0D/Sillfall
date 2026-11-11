using Microsoft.UI.Xaml;
using Weatherly.Core;

namespace Weatherly.App;

/// <summary>
/// Placeholder window of the foundation milestone: shows the product version and the startup diagnostics.
/// It is replaced by the tray flyout and the settings window in later milestones.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow(IReadOnlyList<DiagnosticResult> diagnostics)
    {
        InitializeComponent();

        Title = AppInfo.Name;
        AppTitleBar.Title = AppInfo.Name;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");

        HeadingText.Text = $"{AppInfo.Name} {AppInfo.DisplayVersion}";
        DiagnosticsList.ItemsSource = diagnostics;
    }
}
