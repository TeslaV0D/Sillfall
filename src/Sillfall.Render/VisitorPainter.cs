using System.Numerics;
using System.Text;
using Sillfall.Core.Geometry;
using Sillfall.Core.Rendering;
using Sillfall.Core.Visitors;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using Vortice.WIC;

namespace Sillfall.Render;

/// <summary>
/// Draws the visitors with Direct2D's SVG renderer (PLAN.md, section 6): every drawing is split into parts (body, eyes,
/// feet) that move separately, so a visitor walks, blinks, bobs and squashes without sprite sheets, sharp at every DPI.
/// Props and faces are small SVGs of their own. The documents are built once per device, on first use.
/// </summary>
public sealed class VisitorPainter : IDisposable
{
    private readonly ID2D1DeviceContext5 _context;
    private readonly IWICImagingFactory _wic;
    private readonly Dictionary<(VisitorKind Kind, Part Part), ID2D1SvgDocument?> _parts = [];
    private readonly Dictionary<VisitorProp, ID2D1SvgDocument> _props = [];
    private ID2D1SvgDocument? _box;

    public VisitorPainter(ID2D1DeviceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context.QueryInterface<ID2D1DeviceContext5>();
        _wic = new IWICImagingFactory();
    }

    private enum Part
    {
        Body,
        Eyes,
        FootLeft,
        FootRight,
        HappyEyes,
        SulkyBrows,
        Sunglasses,
    }

    /// <summary>
    /// Draws <paramref name="visitors"/> onto the context's target (between <c>BeginDraw</c> and <c>EndDraw</c>).
    /// <paramref name="area"/> is the target's rectangle on the desktop, <paramref name="opacity"/> the monitor's opacity.
    /// </summary>
    public void Draw(ReadOnlySpan<VisitorPose> visitors, PixelRect area, float opacity, VisitorBox box = default)
    {
        Matrix3x2 previous = _context.Transform;
        if (box.IsVisible)
        {
            DrawBox(box, area, opacity);
        }

        foreach (VisitorPose pose in visitors)
        {
            DrawOne(in pose, area, opacity);
        }

        _context.Transform = previous;
    }

    public void Dispose()
    {
        foreach (ID2D1SvgDocument? document in _parts.Values)
        {
            document?.Dispose();
        }

        foreach (ID2D1SvgDocument document in _props.Values)
        {
            document.Dispose();
        }

        _box?.Dispose();
        _wic.Dispose();
        _context.Dispose();
    }

    private static string File(VisitorKind kind) => kind.ToString().ToLowerInvariant();

    private static string? PartId(Part part) => part switch
    {
        Part.Eyes => "eyes",
        Part.FootLeft => "foot-left",
        Part.FootRight => "foot-right",
        _ => null,
    };

    private void DrawOne(in VisitorPose pose, PixelRect area, float opacity)
    {
        VisitorArt art = VisitorArt.Get(pose.Kind);
        float alpha = Math.Clamp(pose.Alpha * opacity, 0f, 1f);
        if (alpha <= 0.004f || pose.Height <= 0f)
        {
            return;
        }

        float unit = pose.Height / VisitorArt.Size * Math.Max(pose.Scale, 0f);
        float mirror = (art.FacesRight ? 1f : -1f) * (pose.Facing < 0f ? -1f : 1f);
        var feet = new Vector2(pose.X - area.Left, pose.Y - area.Top);
        float stretch = pose.Stretch;
        float tilt = pose.Tilt;

        // From drawing units to target pixels: around the feet, mirrored, stretched, tilted, then onto the surface.
        Matrix3x2 Place(float lift) =>
            Matrix3x2.CreateTranslation(-VisitorArt.Size / 2f, -art.Ground)
            * Matrix3x2.CreateScale(unit * mirror, unit * stretch)
            * Matrix3x2.CreateRotation(tilt)
            * Matrix3x2.CreateTranslation(feet.X, feet.Y - lift);

        bool layered = alpha < 0.996f;
        if (layered)
        {
            _context.PushLayer(new LayerParameters1
            {
                ContentBounds = new Rect(-1e6f, -1e6f, 2e6f, 2e6f),
                MaskTransform = Matrix3x2.Identity,
                Opacity = alpha,
            }, null!); // Direct2D 1.1 manages the layer itself when none is given.
        }

        // Feet stay on the ground and take turns while walking; the body bobs above them.
        Matrix3x2 ground = Place(0f);
        Matrix3x2 body = Place(pose.Bob);
        float stepLift = pose.Walking ? 2.5f : 0f;
        float phase = pose.Step * MathF.PI * 2f;
        DrawPart(art, Part.FootLeft, Matrix3x2.CreateTranslation(0f, -Math.Max(0f, MathF.Sin(phase)) * stepLift) * ground);
        DrawPart(art, Part.FootRight, Matrix3x2.CreateTranslation(0f, -Math.Max(0f, -MathF.Sin(phase)) * stepLift) * ground);
        DrawPart(art, Part.Body, body);

        switch (pose.Expression)
        {
            case VisitorExpression.Happy when art.HasEyes:
                DrawPart(art, Part.HappyEyes, body);
                break;
            case VisitorExpression.Sulky:
                DrawEyes(art, pose.Blink, body);
                DrawPart(art, Part.SulkyBrows, body);
                break;
            default:
                DrawEyes(art, pose.Blink, body);
                break;
        }

        DrawProp(art, in pose, body);

        if (layered)
        {
            _context.PopLayer();
        }
    }

    /// <summary>The box, scaled into its rectangle; it grows a little while a held visitor is over it.</summary>
    private void DrawBox(VisitorBox box, PixelRect area, float opacity)
    {
        float alpha = Math.Clamp(box.Visibility * opacity, 0f, 1f);
        float grow = box.Hot ? 1.12f : 1f;
        float size = Math.Min(box.Bounds.Width, box.Bounds.Height) / VisitorArt.Size * grow;
        var centre = new Vector2(box.Bounds.Left + (box.Bounds.Width / 2f) - area.Left, box.Bounds.Bottom - area.Top);
        _context.PushLayer(new LayerParameters1
        {
            ContentBounds = new Rect(-1e6f, -1e6f, 2e6f, 2e6f),
            MaskTransform = Matrix3x2.Identity,
            Opacity = alpha,
        }, null!);
        _context.Transform = Matrix3x2.CreateTranslation(-VisitorArt.Size / 2f, -VisitorArt.Size) * Matrix3x2.CreateScale(size) * Matrix3x2.CreateTranslation(centre);
        _context.DrawSvgDocument(BoxDocument());
        _context.PopLayer();
    }

    private ID2D1SvgDocument BoxDocument()
    {
        _box ??= Create(VisitorArt.LoadSvg("box"));
        return _box;
    }

    private void DrawEyes(VisitorArt art, float blink, Matrix3x2 body)
    {
        if (!art.HasEyes || art.Eyes.Length == 0)
        {
            return;
        }

        // Blinking squeezes the eyes towards their middle row.
        float y = art.Eyes.Average(eye => eye.Y);
        float open = 1f - (0.9f * Math.Clamp(blink, 0f, 1f));
        Matrix3x2 squeeze = Matrix3x2.CreateTranslation(0f, -y) * Matrix3x2.CreateScale(1f, open) * Matrix3x2.CreateTranslation(0f, y);
        DrawPart(art, Part.Eyes, squeeze * body);
    }

    private void DrawProp(VisitorArt art, in VisitorPose pose, Matrix3x2 body)
    {
        float amount = Math.Clamp(pose.PropAmount, 0f, 1f);
        if (pose.Prop == VisitorProp.None || amount <= 0.01f)
        {
            return;
        }

        if (pose.Prop == VisitorProp.Sunglasses)
        {
            // Slide down onto the eyes.
            DrawPart(art, Part.Sunglasses, Matrix3x2.CreateTranslation(0f, -(1f - amount) * 6f) * body);
            return;
        }

        (Vector2 anchor, Vector2 hold, Vector2 scale) = pose.Prop switch
        {
            VisitorProp.Umbrella => (new Vector2(32f, 34f), art.HeadTop, new Vector2(0.2f + (0.8f * amount), 0.5f + (0.5f * amount))),
            VisitorProp.Leaf => (new Vector2(32f, 34f), art.HeadTop, new Vector2(amount, amount)),
            VisitorProp.Lantern => (new Vector2(32f, 24f), art.Side, new Vector2(amount * 0.8f, amount * 0.8f)),
            _ => (new Vector2(32f, 32f), art.Side, new Vector2(amount, amount)),
        };

        Matrix3x2 transform = Matrix3x2.CreateTranslation(-anchor) * Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(hold) * body;
        ID2D1SvgDocument document = PropDocument(pose.Prop);
        _context.Transform = transform;
        _context.DrawSvgDocument(document);
    }

    private void DrawPart(VisitorArt art, Part part, Matrix3x2 transform)
    {
        if (PartDocument(art, part) is { } document)
        {
            _context.Transform = transform;
            _context.DrawSvgDocument(document);
        }
    }

    private ID2D1SvgDocument? PartDocument(VisitorArt art, Part part)
    {
        if (_parts.TryGetValue((art.Kind, part), out ID2D1SvgDocument? document))
        {
            return document;
        }

        string? svg = part switch
        {
            Part.HappyEyes => art.HappyEyes(),
            Part.SulkyBrows => art.SulkyBrows(),
            Part.Sunglasses => art.Sunglasses(),
            _ => VisitorArt.Part(VisitorArt.LoadSvg(File(art.Kind)), PartId(part)),
        };
        document = svg is null ? null : Create(svg);
        _parts[(art.Kind, part)] = document;
        return document;
    }

    private ID2D1SvgDocument PropDocument(VisitorProp prop)
    {
        if (!_props.TryGetValue(prop, out ID2D1SvgDocument? document))
        {
            document = Create(VisitorArt.LoadSvg(prop.ToString().ToLowerInvariant()));
            _props[prop] = document;
        }

        return document;
    }

    private ID2D1SvgDocument Create(string svg)
    {
        using IWICStream stream = _wic.CreateStream();
        stream.Initialize(Encoding.UTF8.GetBytes(svg));
        return _context.CreateSvgDocument(stream, new Size(VisitorArt.Size, VisitorArt.Size));
    }
}
