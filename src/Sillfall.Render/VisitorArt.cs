using System.Globalization;
using System.Numerics;
using System.Text;
using System.Xml.Linq;
using Sillfall.Core.Visitors;

namespace Sillfall.Render;

/// <summary>
/// Where the parts of a visitor drawing are, in its 64 × 64 SVG units (<c>Visitors/*.svg</c>): the anchors the painter
/// animates around, and the extra drawings it builds from them (happy eyes, sulky brows, sunglasses).
/// </summary>
/// <param name="Kind">Which visitor.</param>
/// <param name="FacesRight">The drawing looks right (or at the viewer); otherwise it is mirrored for walking right.</param>
/// <param name="Ground">Row of the feet's bottom: it stands on the surface.</param>
/// <param name="Eyes">Centres of the eyes (empty: none to animate).</param>
/// <param name="EyeRadius">Radius of an eye.</param>
/// <param name="HeadTop">Where an umbrella or a leaf is held over the head.</param>
/// <param name="Side">Where a snowball or a lantern is held.</param>
/// <param name="HasEyes">Whether the drawing has a separate "eyes" part (Sol's sunglasses are its eyes).</param>
internal sealed record VisitorArt(
    VisitorKind Kind, bool FacesRight, float Ground, Vector2[] Eyes, float EyeRadius, Vector2 HeadTop, Vector2 Side, bool HasEyes = true)
{
    public const float Size = 64f;

    /// <summary>Colour of eyes and lines, the same in every drawing.</summary>
    public const string Ink = "#2E3440";

    public static IReadOnlyList<VisitorArt> All { get; } =
    [
        new(VisitorKind.Pip, FacesRight: false, 58f, [new(12.3f, 16f), new(19.7f, 16f)], 3.2f, new(16f, 11f), new(24f, 40f)),
        new(VisitorKind.Tuck, true, 61f, [new(26.5f, 28f), new(37.5f, 28f)], 3.6f, new(32f, 18f), new(48f, 44f)),
        new(VisitorKind.Bristle, true, 60f, [new(51f, 44f)], 1.8f, new(34f, 20f), new(56f, 52f)),
        new(VisitorKind.Lumen, true, 55f, [new(28.8f, 23.5f), new(35.2f, 23.5f)], 2.8f, new(32f, 8f), new(44f, 46f)),
        new(VisitorKind.Sol, true, 61f, [new(25.75f, 28.25f), new(38.25f, 28.25f)], 3.2f, new(32f, 14f), new(46f, 48f), HasEyes: false),
        new(VisitorKind.Drift, true, 61f, [new(28f, 36f), new(38f, 36f)], 2f, new(34f, 15f), new(53f, 42f)),
        new(VisitorKind.Sprout, true, 61.7f, [new(27.5f, 50f), new(36.5f, 50f)], 1.9f, new(32f, 8f), new(45f, 48f)),
        new(VisitorKind.Nim, true, 61f, [new(26.5f, 32f), new(33.5f, 32f)], 2f, new(30f, 22f), new(42f, 46f)),
    ];

    public static VisitorArt Get(VisitorKind kind) => All[(int)kind];

    /// <summary>The visitor's drawing from the assembly.</summary>
    public static string LoadSvg(string name)
    {
        using Stream stream = typeof(VisitorArt).Assembly.GetManifestResourceStream($"Sillfall.Render.Visitors.{name}.svg")
            ?? throw new InvalidOperationException($"The drawing {name}.svg is missing from the assembly.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// One part of a drawing as its own SVG: <c>null</c> for everything outside the named groups (the body), else only that
    /// group. Empty when the drawing has no such part.
    /// </summary>
    public static string? Part(string svg, string? part)
    {
        XDocument document = XDocument.Parse(svg);
        XElement root = document.Root!;
        XName group = root.Name.Namespace + "g";
        List<XElement> named = [.. root.Elements(group).Where(element => element.Attribute("id") is not null)];
        if (part is null)
        {
            foreach (XElement element in named)
            {
                element.Remove();
            }
        }
        else
        {
            XElement? keep = named.FirstOrDefault(element => (string?)element.Attribute("id") == part);
            if (keep is null)
            {
                return null;
            }

            foreach (XNode node in root.Nodes().ToList())
            {
                if (node != keep)
                {
                    node.Remove();
                }
            }
        }

        return document.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>Closed, smiling eyes (two arcs).</summary>
    public string HappyEyes() => Overlay(Eyes.Select(eye =>
        Path($"M{F(eye.X - EyeRadius)} {F(eye.Y + (EyeRadius * 0.35f))} Q{F(eye.X)} {F(eye.Y - (EyeRadius * 1.3f))} {F(eye.X + EyeRadius)} {F(eye.Y + (EyeRadius * 0.35f))}")));

    /// <summary>Brows that slope down towards the middle of the face.</summary>
    public string SulkyBrows()
    {
        float middle = Eyes.Average(eye => eye.X);
        return Overlay(Eyes.Select(eye =>
        {
            float inner = eye.X < middle || Eyes.Length == 1 ? 1f : -1f;
            float outerX = eye.X - (inner * EyeRadius * 1.2f);
            float innerX = eye.X + (inner * EyeRadius * 1.2f);
            float top = eye.Y - (EyeRadius * 1.9f);
            return Path($"M{F(outerX)} {F(top)} L{F(innerX)} {F(top + (EyeRadius * 0.8f))}");
        }));
    }

    /// <summary>Sunglasses over the eyes, as a prop for sunny weather.</summary>
    public string Sunglasses()
    {
        float width = Math.Max(EyeRadius * 2.8f, 5f);
        float height = width * 0.62f;
        var parts = new List<string>();
        foreach (Vector2 eye in Eyes)
        {
            parts.Add($"<rect x=\"{F(eye.X - (width / 2))}\" y=\"{F(eye.Y - (height / 2))}\" width=\"{F(width)}\" height=\"{F(height)}\" rx=\"{F(height / 2.2f)}\" fill=\"{Ink}\"/>");
        }

        if (Eyes.Length == 2)
        {
            float left = Math.Min(Eyes[0].X, Eyes[1].X) + (width / 2);
            float right = Math.Max(Eyes[0].X, Eyes[1].X) - (width / 2);
            float y = Eyes.Average(eye => eye.Y) - (height * 0.25f);
            parts.Add($"<rect x=\"{F(left - 0.2f)}\" y=\"{F(y)}\" width=\"{F(right - left + 0.4f)}\" height=\"1.2\" rx=\"0.6\" fill=\"{Ink}\"/>");
        }

        return Overlay(parts);
    }

    private string Path(string data) =>
        $"<path d=\"{data}\" stroke=\"{Ink}\" stroke-width=\"{F(Math.Max(EyeRadius * 0.45f, 1.1f))}\" stroke-linecap=\"round\" fill=\"none\"/>";

    private static string Overlay(IEnumerable<string> elements) =>
        $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 64 64\" width=\"64\" height=\"64\">{string.Concat(elements)}</svg>";

    private static string F(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
