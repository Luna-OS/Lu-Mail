#!/usr/bin/env python3
"""Zeichnet das Lu-Mail-Symbol (Briefumschlag + Mondsichel im Luna-Theme).

Erzeugt:
  shared/icon/lu-mail-1024.png          Quelle für das macOS-Symbol (build-app.sh skaliert per sips)
  windows/src/LuMail.App/Assets/LuMail.ico   Windows-Symbol (16–256 px)

Aufruf: python3 scripts/make-icon.py   (benötigt Pillow: pip install pillow)
"""
import pathlib

from PIL import Image, ImageChops, ImageDraw

ROOT = pathlib.Path(__file__).resolve().parent.parent
SCALE = 4  # Überabtastung für glatte Kanten
SIZE = 1024


def hex_rgb(value: str) -> tuple:
    value = value.lstrip("#")
    return tuple(int(value[i:i + 2], 16) for i in (0, 2, 4))


def render() -> Image.Image:
    s = SIZE * SCALE
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))

    # Hintergrund: abgerundetes Quadrat mit Nachthimmel-Verlauf (#2D2A5C → #0F1020)
    inset = int(s * 0.09)
    box = (inset, inset, s - inset, s - inset)
    radius = int((box[2] - box[0]) * 0.23)
    top, bottom = hex_rgb("#2D2A5C"), hex_rgb("#0F1020")
    gradient = Image.new("RGBA", (1, s))
    for y in range(s):
        t = y / (s - 1)
        gradient.putpixel((0, y), tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,))
    gradient = gradient.resize((s, s))
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).rounded_rectangle(box, radius=radius, fill=255)
    img.paste(gradient, (0, 0), mask)

    draw = ImageDraw.Draw(img)
    width = box[2] - box[0]

    # Briefumschlag
    ex0 = box[0] + width * 0.2
    ex1 = box[2] - width * 0.2
    ey1 = box[3] - width * 0.2
    ey0 = ey1 - width * 0.42
    draw.rounded_rectangle((ex0, ey0, ex1, ey1), radius=int((ex1 - ex0) * 0.08), fill=hex_rgb("#ECEBFF"))
    ew, eh = ex1 - ex0, ey1 - ey0
    line = max(2, int(s * 0.025))
    flap = [(ex0 + ew * 0.06, ey0 + eh * 0.08), ((ex0 + ex1) / 2, ey0 + eh * 0.58), (ex1 - ew * 0.06, ey0 + eh * 0.08)]
    draw.line(flap, fill=hex_rgb("#8B7CFF"), width=line, joint="curve")
    for x, y in (flap[0], flap[2]):
        draw.ellipse((x - line / 2, y - line / 2, x + line / 2, y + line / 2), fill=hex_rgb("#8B7CFF"))

    # Mondsichel oben rechts
    moon = width * 0.34
    mx1 = box[2] - width * 0.1
    my0 = box[1] + width * 0.08
    mx0, my1 = mx1 - moon, my0 + moon
    full = Image.new("L", (s, s), 0)
    ImageDraw.Draw(full).ellipse((mx0, my0, mx1, my1), fill=255)
    cut = Image.new("L", (s, s), 0)
    ImageDraw.Draw(cut).ellipse((mx0 + moon * 0.32, my0 - moon * 0.18, mx1 + moon * 0.32, my1 - moon * 0.18), fill=255)
    crescent = ImageChops.subtract(full, cut)
    img.paste(Image.new("RGBA", (s, s), hex_rgb("#F5D06F") + (255,)), (0, 0), crescent)

    return img.resize((SIZE, SIZE), Image.LANCZOS)


def main() -> None:
    icon = render()
    png = ROOT / "shared" / "icon" / "lu-mail-1024.png"
    png.parent.mkdir(parents=True, exist_ok=True)
    icon.save(png, optimize=True)
    ico = ROOT / "windows" / "src" / "LuMail.App" / "Assets" / "LuMail.ico"
    ico.parent.mkdir(parents=True, exist_ok=True)
    icon.save(ico, sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print(f"geschrieben: {png.relative_to(ROOT)}, {ico.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
