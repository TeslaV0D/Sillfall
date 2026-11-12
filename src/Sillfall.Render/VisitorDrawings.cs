using Sillfall.Core.Visitors;

namespace Sillfall.Render;

/// <summary>The visitors' drawings as SVG text, for pictures in the app's own windows (collection, share card).</summary>
public static class VisitorDrawings
{
    public static string Svg(VisitorKind kind) => VisitorArt.LoadSvg(kind.ToString().ToLowerInvariant());
}
