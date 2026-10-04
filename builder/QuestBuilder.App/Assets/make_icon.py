"""Draws the builder's icon: a stylized VR headset on a rounded square (our own drawing, no game artwork).

    uv run --with pillow python builder/QuestBuilder.App/Assets/make_icon.py

-> app.ico (16..256 px, Windows exe and window icon) and logo.png (the window's top bar), next to this script.
"""
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw

HERE = Path(__file__).resolve().parent
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
BIG = 1024  # drawn large, scaled down per size (smooth edges at every size)

TOP_LEFT = (34, 193, 195)     # teal
BOTTOM_RIGHT = (91, 75, 219)  # indigo


def gradient(size: int) -> Image.Image:
    """Diagonal teal -> indigo"""
    small = Image.new("RGB", (64, 64))
    for y in range(64):
        for x in range(64):
            t = (x + y) / 126
            small.putpixel((x, y), tuple(round(a + (b - a) * t) for a, b in zip(TOP_LEFT, BOTTOM_RIGHT)))
    return small.resize((size, size), Image.Resampling.BICUBIC)


def ellipse(draw: ImageDraw.ImageDraw, cx: float, cy: float, rx: float, ry: float, fill: int) -> None:
    draw.ellipse((cx - rx, cy - ry, cx + rx, cy + ry), fill=fill)


def headset_mask(s: int) -> Image.Image:
    """White goggles: a rounded body with a nose notch, two lens openings and short strap stubs"""
    mask = Image.new("L", (s, s), 0)
    draw = ImageDraw.Draw(mask)
    # Strap stubs on both sides
    draw.rounded_rectangle((0.10 * s, 0.445 * s, 0.90 * s, 0.555 * s), radius=0.04 * s, fill=150)
    # The body
    draw.rounded_rectangle((0.18 * s, 0.33 * s, 0.82 * s, 0.67 * s), radius=0.12 * s, fill=255)
    holes = Image.new("L", (s, s), 0)
    hole = ImageDraw.Draw(holes)
    # Nose notch at the bottom middle
    ellipse(hole, 0.5 * s, 0.675 * s, 0.085 * s, 0.075 * s, 255)
    # Lens openings
    ellipse(hole, 0.355 * s, 0.49 * s, 0.095 * s, 0.085 * s, 255)
    ellipse(hole, 0.645 * s, 0.49 * s, 0.095 * s, 0.085 * s, 255)
    return ImageChops.subtract(mask, holes)


def icon(s: int = BIG) -> Image.Image:
    background = Image.new("L", (s, s), 0)
    ImageDraw.Draw(background).rounded_rectangle((0, 0, s - 1, s - 1), radius=0.22 * s, fill=255)
    image = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    image.paste(gradient(s), (0, 0), background)
    white = Image.new("RGBA", (s, s), (255, 255, 255, 255))
    image.paste(white, (0, 0), headset_mask(s))
    # A soft highlight across the top of the visor
    shine = Image.new("L", (s, s), 0)
    ImageDraw.Draw(shine).rounded_rectangle((0.24 * s, 0.36 * s, 0.76 * s, 0.39 * s), radius=0.015 * s, fill=70)
    image.paste(Image.new("RGBA", (s, s), (91, 75, 219, 255)), (0, 0), ImageChops.multiply(shine, headset_mask(s)))
    return image


def main() -> None:
    big = icon()
    frames = [big.resize((n, n), Image.Resampling.LANCZOS) for n in SIZES]
    frames[-1].save(HERE / "app.ico", sizes=[(n, n) for n in SIZES], append_images=frames[:-1])
    big.resize((128, 128), Image.Resampling.LANCZOS).save(HERE / "logo.png")
    print(f"wrote {HERE / 'app.ico'} and {HERE / 'logo.png'}")


if __name__ == "__main__":
    main()
