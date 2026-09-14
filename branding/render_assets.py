#!/usr/bin/env python3
"""Rasterize branding/gem-core.svg into WinUI / installer / store assets.

Run from the repo root:

    python3 branding/render_assets.py

Requires: rsvg-convert (librsvg2-bin), Pillow.

This script is the Linux/CI path. Windows ICO can also be rebuilt with
ImageMagick or Visual Studio — see branding/README.md.
"""

from __future__ import annotations

import struct
import subprocess
import sys
from io import BytesIO
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
SVG = Path(__file__).resolve().parent / "gem-core.svg"
APP_ASSETS = ROOT / "src" / "GeoMineralTrace.App" / "Assets"
INSTALLER_ASSETS = ROOT / "installer" / "assets"
BRANDING = Path(__file__).resolve().parent

CYAN = (0, 255, 255, 255)
BLACK = (0, 0, 0, 255)
NEAR_BLACK = (5, 8, 10, 255)
TEAL_DEEP = (0, 20, 24, 255)


def require_rsvg() -> str:
    from shutil import which

    exe = which("rsvg-convert")
    if not exe:
        print("rsvg-convert not found. Install librsvg2-bin.", file=sys.stderr)
        sys.exit(1)
    return exe


def render_svg(size: int, *, background: str | None = None) -> Image.Image:
    """Render the mark at `size` px. background is CSS color or None (transparent)."""
    exe = require_rsvg()
    cmd = [
        exe,
        f"--width={size}",
        f"--height={size}",
        "--format=png",
        str(SVG),
    ]
    if background:
        cmd[1:1] = [f"--background-color={background}"]
    raw = subprocess.check_output(cmd)
    img = Image.open(BytesIO(raw)).convert("RGBA")
    if img.size != (size, size):
        img = img.resize((size, size), Image.Resampling.LANCZOS)
    return img


def plated(size: int) -> Image.Image:
    return render_svg(size, background="black")


def unplated(size: int) -> Image.Image:
    return render_svg(size, background=None)


def contain_on_canvas(
    mark: Image.Image,
    canvas_size: tuple[int, int],
    *,
    background: tuple[int, int, int, int],
    mark_height: int | None = None,
    offset: tuple[int, int] = (0, 0),
) -> Image.Image:
    canvas = Image.new("RGBA", canvas_size, background)
    if mark_height is None:
        mark_height = min(canvas_size) - max(8, min(canvas_size) // 8)
    ratio = mark_height / mark.size[1]
    w = max(1, int(mark.size[0] * ratio))
    h = max(1, mark_height)
    fitted = mark.resize((w, h), Image.Resampling.LANCZOS)
    x = (canvas_size[0] - w) // 2 + offset[0]
    y = (canvas_size[1] - h) // 2 + offset[1]
    canvas.alpha_composite(fitted, (x, y))
    return canvas


def save_png(img: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, format="PNG", optimize=True)
    print(f"  wrote {path.relative_to(ROOT)}  {img.size[0]}x{img.size[1]}")


def _and_mask_row_bytes(width: int) -> int:
    # 1 bit/pixel, rows padded to 32 bits
    return ((width + 31) // 32) * 4


def _image_to_ico_dib(img: Image.Image) -> bytes:
    """Classic ICO DIB: 32-bit BGRA XOR + 1-bit AND mask. Height is 2x in header."""
    rgba = img.convert("RGBA")
    w, h = rgba.size
    pixels = list(rgba.getdata())

    xor = bytearray()
    # BMP is bottom-up
    for y in range(h - 1, -1, -1):
        row = pixels[y * w : (y + 1) * w]
        for r, g, b, a in row:
            xor.extend((b, g, r, a))

    and_row = _and_mask_row_bytes(w)
    and_mask = bytearray()
    for y in range(h - 1, -1, -1):
        row = pixels[y * w : (y + 1) * w]
        bits = 0
        count = 0
        packed = bytearray()
        for _r, _g, _b, a in row:
            bits = (bits << 1) | (1 if a == 0 else 0)
            count += 1
            if count == 8:
                packed.append(bits)
                bits = 0
                count = 0
        if count:
            bits <<= 8 - count
            packed.append(bits)
        packed.extend(b"\x00" * (and_row - len(packed)))
        and_mask.extend(packed)

    header = struct.pack(
        "<IIIHHIIIIII",
        40,  # biSize
        w,
        h * 2,  # includes AND mask
        1,  # planes
        32,  # bit count
        0,  # BI_RGB
        len(xor) + len(and_mask),
        0,
        0,
        0,
        0,
    )
    return header + bytes(xor) + bytes(and_mask)


def write_ico(path: Path, frames: list[Image.Image]) -> None:
    """Write a multi-resolution ICO using 32-bit DIB frames (Windows-classic)."""
    entries: list[tuple[int, int, bytes]] = []
    for frame in frames:
        img = frame.convert("RGBA")
        if img.size[0] != img.size[1]:
            raise ValueError(f"ICO frame must be square, got {img.size}")
        payload = _image_to_ico_dib(img)
        entries.append((img.size[0], img.size[1], payload))

    count = len(entries)
    offset = 6 + 16 * count
    buf = bytearray(struct.pack("<HHH", 0, 1, count))
    payloads = bytearray()
    for w, h, payload in entries:
        buf.extend(
            struct.pack(
                "<BBBBHHII",
                w if w < 256 else 0,
                h if h < 256 else 0,
                0,
                0,
                1,
                32,
                len(payload),
                offset,
            )
        )
        payloads.extend(payload)
        offset += len(payload)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(bytes(buf) + bytes(payloads))
    print(f"  wrote {path.relative_to(ROOT)}  frames={[e[0] for e in entries]}")


def _load_font(size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    candidates = [
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "C:\\Windows\\Fonts\\segoeuib.ttf",
        "C:\\Windows\\Fonts\\segoeui.ttf",
    ]
    for candidate in candidates:
        p = Path(candidate)
        if p.exists():
            return ImageFont.truetype(str(p), size=size)
    return ImageFont.load_default()


def make_wizard_large(logo: Image.Image) -> Image.Image:
    """164x314 Inno Setup side panel — black / cyan, Unbound Rockhound wordmark."""
    w, h = 164, 314
    img = Image.new("RGBA", (w, h), NEAR_BLACK)
    draw = ImageDraw.Draw(img)
    for y in range(h):
        t = y / max(h - 1, 1)
        r = int(5 + (0 - 5) * t)
        g = int(8 + (20 - 8) * t)
        b = int(10 + (24 - 10) * t)
        draw.line([(0, y), (w, y)], fill=(r, g, b, 255))
    draw.rectangle([0, 0, 4, h], fill=CYAN)

    glow = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    gdraw = ImageDraw.Draw(glow)
    gdraw.ellipse([18, 40, 146, 168], fill=(0, 255, 255, 36))
    img = Image.alpha_composite(img, glow.filter(ImageFilter.GaussianBlur(10)))

    mark = logo.resize((88, 88), Image.Resampling.LANCZOS)
    img.alpha_composite(mark, (38, 56))

    font_brand = _load_font(15)
    font_sub = _load_font(11)
    font_co = _load_font(10)
    draw = ImageDraw.Draw(img)
    draw.text((14, 184), "UNBOUND", font=font_brand, fill=(245, 255, 255, 255))
    draw.text((14, 204), "ROCKHOUND", font=font_brand, fill=CYAN)
    draw.text((14, 236), "Field research", font=font_sub, fill=(160, 200, 204, 255))
    draw.text((14, 252), "and geolocation", font=font_sub, fill=(160, 200, 204, 255))
    draw.text((14, 284), "Unbound Infotech", font=font_co, fill=CYAN)
    return img.convert("RGB")


def make_wizard_small(logo: Image.Image) -> Image.Image:
    img = Image.new("RGBA", (55, 55), TEAL_DEEP)
    mark = logo.resize((43, 43), Image.Resampling.LANCZOS)
    img.alpha_composite(mark, (6, 6))
    return img.convert("RGB")


def _centered_text(
    draw: ImageDraw.ImageDraw,
    text: str,
    cy: int,
    font: ImageFont.ImageFont,
    fill: tuple[int, int, int, int],
    canvas_width: int,
    tracking: float = 0,
) -> None:
    """Draw a single-line string centered on x, using optional letter tracking."""
    if tracking <= 0:
        bbox = draw.textbbox((0, 0), text, font=font)
        tw = bbox[2] - bbox[0]
        draw.text(((canvas_width - tw) // 2, cy), text, font=font, fill=fill)
        return
    space_min = max(8, int(tracking * 4))
    widths: list[int] = []
    for ch in text:
        if ch == " ":
            widths.append(space_min)
            continue
        bbox = draw.textbbox((0, 0), ch, font=font)
        widths.append(max(1, bbox[2] - bbox[0]))
    total = sum(widths) + tracking * max(len(text) - 1, 0)
    x = (canvas_width - total) / 2
    for ch, w in zip(text, widths, strict=True):
        if ch != " ":
            draw.text((x, cy), ch, font=font, fill=fill)
        x += w + tracking


def make_splash(master: Image.Image) -> Image.Image:
    canvas = Image.new("RGBA", (1240, 600), BLACK)
    mark = master.resize((280, 280), Image.Resampling.LANCZOS)
    canvas.alpha_composite(mark, ((1240 - 280) // 2, 72))
    draw = ImageDraw.Draw(canvas)
    font = _load_font(34)
    _centered_text(draw, "UNBOUND ROCKHOUND", 392, font, CYAN, 1240)
    sub = _load_font(17)
    _centered_text(
        draw,
        "Forensic geolocation and field research",
        448,
        sub,
        (168, 208, 212, 255),
        1240,
    )
    return canvas


def make_wide(master: Image.Image) -> Image.Image:
    return contain_on_canvas(master, (620, 300), background=BLACK, mark_height=220)


def main() -> None:
    if not SVG.exists():
        print(f"Missing {SVG}", file=sys.stderr)
        sys.exit(1)

    print("Rendering Gem Core from", SVG.relative_to(ROOT))
    master = plated(1024)
    master_clear = unplated(1024)
    save_png(master, BRANDING / "gem-core.png")
    save_png(master_clear, BRANDING / "gem-core-unplated.png")

    # WinUI / MSIX visual assets (existing filenames + sizes)
    save_png(plated(50), APP_ASSETS / "StoreLogo.png")
    save_png(plated(100), APP_ASSETS / "StoreLogo.scale-200.png")
    save_png(plated(300), APP_ASSETS / "Square150x150Logo.scale-200.png")
    save_png(plated(88), APP_ASSETS / "Square44x44Logo.scale-200.png")
    save_png(unplated(24), APP_ASSETS / "Square44x44Logo.targetsize-24_altform-unplated.png")
    save_png(unplated(48), APP_ASSETS / "Square44x44Logo.targetsize-48_altform-lightunplated.png")
    save_png(plated(48), APP_ASSETS / "LockScreenLogo.scale-200.png")
    save_png(make_wide(master), APP_ASSETS / "Wide310x150Logo.scale-200.png")
    save_png(make_splash(master), APP_ASSETS / "SplashScreen.scale-200.png")

    ico_sizes = (256, 128, 64, 48, 32, 16)
    ico_frames = [plated(s) for s in ico_sizes]
    write_ico(APP_ASSETS / "AppIcon.ico", ico_frames)
    write_ico(INSTALLER_ASSETS / "AppIcon.ico", ico_frames)

    wizard_large = make_wizard_large(master)
    wizard_small = make_wizard_small(master)
    INSTALLER_ASSETS.mkdir(parents=True, exist_ok=True)
    wizard_large.save(INSTALLER_ASSETS / "WizardImage.bmp", format="BMP")
    wizard_small.save(INSTALLER_ASSETS / "WizardSmallImage.bmp", format="BMP")
    print("  wrote installer/assets/WizardImage.bmp  164x314")
    print("  wrote installer/assets/WizardSmallImage.bmp  55x55")


if __name__ == "__main__":
    main()
