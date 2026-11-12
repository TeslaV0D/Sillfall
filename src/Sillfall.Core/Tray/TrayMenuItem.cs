namespace Sillfall.Core.Tray;

/// <summary>
/// One entry of the tray menu, independent of the Win32 menu that shows it. A separator has no text,
/// a submenu has <see cref="Children"/>, every other entry runs <see cref="Invoke"/> when chosen.
/// </summary>
public sealed record TrayMenuItem
{
    public static TrayMenuItem Separator { get; } = new() { IsSeparator = true };

    public string Text { get; init; } = string.Empty;

    public Action? Invoke { get; init; }

    /// <summary>Shown with a check mark (or a radio bullet when <see cref="IsRadio"/>).</summary>
    public bool IsChecked { get; init; }

    public bool IsRadio { get; init; }

    public bool IsEnabled { get; init; } = true;

    /// <summary>Shown in bold: the action of a left click on the icon.</summary>
    public bool IsDefault { get; init; }

    public bool IsSeparator { get; init; }

    public IReadOnlyList<TrayMenuItem>? Children { get; init; }
}
