"""Generate the Zycord brand mark: SVG, PNG set, and multi-resolution .ico.

The mark is a flat graphite squircle carrying a light Z. Geometry lives here
once and is emitted to every format, so the icon, the favicon and the README
header cannot drift apart.

Usage: python brand/make_icon.py
"""
import os

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))

# ---------------------------------------------------------------- palette
GRAPHITE_TOP = (0x29, 0x29, 0x2E)
GRAPHITE     = (0x17, 0x17, 0x1A)
INK          = (0xE9, 0xE9, 0xEC)
WHITE        = (0xFF, 0xFF, 0xFF)

# ------------------------------------------------- geometry, in a 1024 grid
U = 1024.0
CORNER = 266.24          # 26 / 100
RIM_INSET = 9.0
RIM_WIDTH = 12.0
SHEEN_DEPTH = 0.54       # fraction of the height
SHEEN_ALPHA = 10         # peak sheen alpha, 0-255
SHEEN_POWER = 6.0        # how quickly the sheen falls away

# The Z, in 100-unit space, scaled here to the 1024 grid.
Z_POINTS = [
    (72.0, 28.0), (28.0, 28.0), (28.0, 38.5), (55.0, 38.5), (28.0, 61.5),
    (28.0, 72.0), (72.0, 72.0), (72.0, 61.5), (45.0, 61.5), (72.0, 38.5),
]

ICO_SIZES = [16, 24, 32, 48, 64, 128, 256]
PNG_SIZES = [16, 24, 32, 48, 64, 128, 256, 512, 1024]
SS = 4  # supersample factor


def px(v, size):
    return v * size / U


def px100(v, size):
    """Scale a coordinate given in the 100-unit grid used by the SVG."""
    return v * size / 100.0


def gradient(size):
    """A flat graphite fill with a slight vertical lift."""
    n = 256
    img = Image.new("RGBA", (n, n))
    d = ImageDraw.Draw(img)
    for y in range(n):
        k = y / float(n - 1)
        c = tuple(int(GRAPHITE_TOP[i] + (GRAPHITE[i] - GRAPHITE_TOP[i]) * k) for i in range(3))
        d.line([(0, y), (n - 1, y)], fill=c + (255,))
    return img.resize((size, size), Image.LANCZOS)


def rounded_mask(size, radius):
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [0, 0, size - 1, size - 1], radius=radius, fill=255)
    return mask


def render(size):
    big = size * SS
    radius = px(CORNER, big)

    canvas = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    mask = rounded_mask(big, radius)

    canvas.paste(gradient(big), (0, 0), mask)

    # Glass sheen across the top half.
    sheen = Image.new("L", (big, big), 0)
    sd = ImageDraw.Draw(sheen)
    depth = int(big * SHEEN_DEPTH)
    for y in range(depth):
        a = int(SHEEN_ALPHA * (1.0 - y / float(depth)) ** SHEEN_POWER)
        sd.line([(0, y), (big, y)], fill=a)
    sheen = Image.composite(sheen, Image.new("L", (big, big), 0), mask)
    canvas.paste(Image.new("RGBA", (big, big), WHITE + (255,)), (0, 0), sheen)

    # Hairline rim.
    rim = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    rd = ImageDraw.Draw(rim)
    inset = px(RIM_INSET, big)
    rd.rounded_rectangle(
        [inset, inset, big - 1 - inset, big - 1 - inset],
        radius=max(1.0, radius - inset),
        outline=WHITE + (76,),
        width=max(1, int(px(RIM_WIDTH, big))),
    )
    canvas = Image.alpha_composite(canvas, rim)

    # The Z.
    z = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    pts = [(px100(x, big), px100(y, big)) for x, y in Z_POINTS]
    ImageDraw.Draw(z).polygon(pts, fill=INK + (255,))
    canvas = Image.alpha_composite(canvas, z)

    return canvas.resize((size, size), Image.LANCZOS)


SVG = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" role="img" aria-label="Zycord">
  <title>Zycord</title>
  <rect width="100" height="100" rx="24" fill="#17171a"/>
  <rect x=".75" y=".75" width="98.5" height="98.5" rx="23.4" fill="none" stroke="#ffffff" stroke-opacity=".14" stroke-width="1.2"/>
  <rect width="100" height="46" rx="24" fill="#ffffff" fill-opacity=".05"/>
  <polygon points="72,28 28,28 28,38.5 55,38.5 28,61.5 28,72 72,72 72,61.5 45,61.5 72,38.5" fill="#e9e9ec"/>
</svg>
"""


def main():
    with open(os.path.join(HERE, "zycord.svg"), "w", encoding="utf-8", newline="\n") as f:
        f.write(SVG)

    # The renderer keeps its own copy so the shipped interface is self-contained.
    renderer = os.path.join(os.path.dirname(HERE), "app", "renderer", "mark.svg")
    if os.path.isdir(os.path.dirname(renderer)):
        with open(renderer, "w", encoding="utf-8", newline="\n") as f:
            f.write(SVG)

    images = {}
    for size in sorted(set(PNG_SIZES + ICO_SIZES)):
        images[size] = render(size)
        print("rendered %d" % size)

    for size in PNG_SIZES:
        images[size].save(os.path.join(HERE, "zycord-%d.png" % size))

    images[256].save(os.path.join(HERE, "zycord.ico"), format="ICO",
                     sizes=[(s, s) for s in ICO_SIZES])

    # A small favicon for the website, capped at 48 px.
    images[48].save(os.path.join(HERE, "favicon.ico"), format="ICO",
                    sizes=[(s, s) for s in (16, 24, 32, 48)])

    site_favicon = os.path.join(os.path.dirname(HERE), "site", "favicon.ico")
    if os.path.isdir(os.path.dirname(site_favicon)):
        images[48].save(site_favicon, format="ICO", sizes=[(s, s) for s in (16, 24, 32, 48)])

    print("wrote zycord.svg, %d PNGs, zycord.ico, favicon.ico" % len(PNG_SIZES))


if __name__ == "__main__":
    main()
