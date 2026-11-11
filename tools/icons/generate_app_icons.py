#!/usr/bin/env python3
"""Generates the Weatherly app icons (MSIX logos, splash screen, AppIcon.ico).

The mark is drawn procedurally: a cloud with three rain drops on a rounded gradient plate.
No external artwork is involved, see ASSETS.md.

Usage:  python3 tools/icons/generate_app_icons.py        (requires Pillow)
Output: src/Weatherly.App/Assets/
"""

from pathlib import Path

from PIL import Image, ImageDraw

SUPERSAMPLE = 4
OUTPUT_DIR = Path(__file__).resolve().parents[2] / "src" / "Weatherly.App" / "Assets"

PLATE_TOP = (38, 84, 168)
PLATE_BOTTOM = (22, 150, 172)
WHITE = (255, 255, 255, 255)
DROP_ON_PLATE = (176, 226, 255, 255)
BRAND_BLUE = (30, 104, 170, 255)
DROP_ON_LIGHT = (22, 150, 172, 255)


def _plate(size: int) -> Image.Image:
    """Rounded square with a vertical gradient."""
    gradient = Image.new("RGBA", (size, size))
    pixels = gradient.load()
    for y in range(size):
        t = y / max(size - 1, 1)
        color = tuple(round(a + (b - a) * t) for a, b in zip(PLATE_TOP, PLATE_BOTTOM)) + (255,)
        for x in range(size):
            pixels[x, y] = color

    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, size - 1, size - 1), radius=round(size * 0.22), fill=255)
    plate = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    plate.paste(gradient, (0, 0), mask)
    return plate


def _draw_cloud_and_rain(image: Image.Image, cloud_color, drop_color, scale: float = 1.0) -> None:
    """Draws the mark centered in the image. `scale` enlarges it for plate-less variants."""
    size = image.width
    draw = ImageDraw.Draw(image)

    def p(value: float) -> float:
        # Unit coordinates (0..1) around the center, scaled.
        return (0.5 + (value - 0.5) * scale) * size

    def circle(cx: float, cy: float, r: float) -> None:
        draw.ellipse((p(cx) - r * scale * size, p(cy) - r * scale * size,
                      p(cx) + r * scale * size, p(cy) + r * scale * size), fill=cloud_color)

    # Cloud: a rounded base with three bumps.
    draw.rounded_rectangle((p(0.20), p(0.42), p(0.80), p(0.62)), radius=0.10 * scale * size, fill=cloud_color)
    circle(0.35, 0.43, 0.115)
    circle(0.52, 0.36, 0.165)
    circle(0.67, 0.45, 0.105)

    # Rain: three slanted drops below the cloud.
    width = max(round(0.05 * scale * size), 1)
    for x in (0.36, 0.52, 0.68):
        start = (p(x), p(0.70))
        end = (p(x - 0.045), p(0.83))
        draw.line((start, end), fill=drop_color, width=width)
        for cx, cy in (start, end):
            draw.ellipse((cx - width / 2, cy - width / 2, cx + width / 2, cy + width / 2), fill=drop_color)


def plated_mark(size: int) -> Image.Image:
    big = _plate(size * SUPERSAMPLE)
    _draw_cloud_and_rain(big, WHITE, DROP_ON_PLATE)
    return big.resize((size, size), Image.LANCZOS)


def unplated_mark(size: int, cloud_color, drop_color) -> Image.Image:
    big = Image.new("RGBA", (size * SUPERSAMPLE, size * SUPERSAMPLE), (0, 0, 0, 0))
    _draw_cloud_and_rain(big, cloud_color, drop_color, scale=1.35)
    return big.resize((size, size), Image.LANCZOS)


def centered_on_canvas(mark: Image.Image, width: int, height: int) -> Image.Image:
    canvas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    canvas.paste(mark, ((width - mark.width) // 2, (height - mark.height) // 2), mark)
    return canvas


def main() -> None:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    outputs = {
        "StoreLogo.png": plated_mark(50),
        "Square44x44Logo.scale-200.png": plated_mark(88),
        "Square44x44Logo.targetsize-24_altform-unplated.png": unplated_mark(24, WHITE, DROP_ON_PLATE),
        "Square44x44Logo.targetsize-48_altform-lightunplated.png": unplated_mark(48, BRAND_BLUE, DROP_ON_LIGHT),
        "Square150x150Logo.scale-200.png": plated_mark(300),
        "Wide310x150Logo.scale-200.png": centered_on_canvas(plated_mark(240), 620, 300),
        "SplashScreen.scale-200.png": centered_on_canvas(plated_mark(400), 1240, 600),
    }
    for name, image in outputs.items():
        image.save(OUTPUT_DIR / name, optimize=True)

    icon_sizes = [16, 24, 32, 48, 64, 128, 256]
    plated_mark(256).save(OUTPUT_DIR / "AppIcon.ico", sizes=[(s, s) for s in icon_sizes])

    for name in sorted([*outputs, "AppIcon.ico"]):
        print(f"wrote {(OUTPUT_DIR / name).relative_to(OUTPUT_DIR.parents[2])}")


if __name__ == "__main__":
    main()
