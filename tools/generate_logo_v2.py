import os
import math
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageChops

repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
font_path = os.path.join(repo_root, "tools", "fonts", "RocknRollOne-Regular.ttf")
logo_dir = os.path.join(repo_root, "docs", "logo")
os.makedirs(logo_dir, exist_ok=True)

# Letter configuration: (char, scale_multiplier, is_counter_target)
LETTERS_CONFIG = [
    ("M", 1.26, False), # Manga M (large)
    ("a", 1.00, True),  # a
    ("n", 1.00, False), # n
    ("g", 1.00, True),  # g
    ("a", 1.00, True),  # a
    ("M", 1.26, False), # Me M (large)
    ("e", 1.00, True),  # e
    ("Y", 1.28, False), # Yeah Y (large)
    ("e", 1.00, True),  # e
    ("a", 1.00, True),  # a
    ("h", 1.00, False), # h
    ("!", 1.20, False), # ! (punchy)
]

def make_gradient(width, height, top_color, bot_color):
    """Creates a vertical linear gradient image RGBA."""
    base = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    for y in range(height):
        t = y / max(1, height - 1)
        r = int(top_color[0] + (bot_color[0] - top_color[0]) * t)
        g = int(top_color[1] + (bot_color[1] - top_color[1]) * t)
        b = int(top_color[2] + (bot_color[2] - top_color[2]) * t)
        a = int(top_color[3] + (bot_color[3] - top_color[3]) * t)
        line = Image.new("RGBA", (width, 1), (r, g, b, a))
        base.paste(line, (0, y))
    return base

def get_char_counter_holes(char: str, font: ImageFont.FreeTypeFont) -> tuple:
    """Finds inner counter holes of 'a', 'g', 'e' in local character coordinates."""
    bbox = font.getbbox(char, anchor="ls")
    pad = 14
    bw = bbox[2] - bbox[0]
    bh = bbox[3] - bbox[1]
    w = bw + pad * 2
    h = bh + pad * 2

    im = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(im)
    tx = pad - bbox[0]
    ty = pad - bbox[1]
    d.text((tx, ty), char, font=font, fill=255, anchor="ls")

    im_closed = im.copy()
    if char == "e":
        d_c = ImageDraw.Draw(im_closed)
        bb = d.textbbox((tx, ty), char, font=font, anchor="ls")
        bw_t = bb[2] - bb[0]
        bh_t = bb[3] - bb[1]
        bridge_y = int(bb[1] + bh_t * 0.46)
        bridge_x0 = int(bb[0] + bw_t * 0.16)
        bridge_x1 = int(bb[0] + bw_t * 0.38)
        d_c.line([(bridge_x0, bridge_y), (bridge_x1, bridge_y)], fill=255, width=max(4, int(bw_t * 0.06)))

    # Flood fill outer area from (0, 0)
    im_flood = im_closed.copy()
    ImageDraw.floodfill(im_flood, (0, 0), 128)

    holes = im_flood.point(lambda p: 255 if p == 0 else 0, mode="L")
    return holes, (-tx, -ty)

def render_logo(
    fill_holes: bool,
    base_font_size: int = 140,
    kerning_factor: float = 0.83, # tight letter spacing
    white_stroke_px: int = 24,    # thick unified white backing
    navy_stroke_px: int = 13,     # outer dark navy border
    shadow_offset_px: int = 16,   # drop shadow offset
) -> Image.Image:
    """
    Renders MangaMeYeah! with:
    - Custom letter scaling (M, M, Y enlarged)
    - Tight kerning
    - Continuous, unified thick white backing
    - Option for filling counter holes in 'a', 'g', 'e'
    - Supersampled 2x for clean anti-aliasing
    """
    ss = 2
    base_sz = base_font_size * ss
    w_stroke = white_stroke_px * ss
    n_stroke = navy_stroke_px * ss
    s_offset = shadow_offset_px * ss

    # Fonts
    fonts = {}
    for _, scale, _ in LETTERS_CONFIG:
        sz = int(base_sz * scale)
        if sz not in fonts:
            fonts[sz] = ImageFont.truetype(font_path, sz)

    # Layout character positions with anchor="ls" (left baseline)
    char_layouts = []
    margin = (w_stroke + n_stroke + s_offset) * 2 + 50
    cur_x = margin
    dummy = Image.new("RGBA", (10, 10))
    d_dum = ImageDraw.Draw(dummy)

    for idx, (ch, scale, is_target) in enumerate(LETTERS_CONFIG):
        font = fonts[int(base_sz * scale)]
        bbox = d_dum.textbbox((0, 0), ch, font=font, anchor="ls")
        char_w = bbox[2] - bbox[0]

        # Tight kerning adjustments
        if idx == 0:
            kern_adv = char_w * 0.90
        elif ch in ["M", "Y"]:
            kern_adv = char_w * 0.92
        elif idx > 0 and LETTERS_CONFIG[idx - 1][0] in ["M", "Y"]:
            kern_adv = char_w * 0.78
        elif ch == "!":
            kern_adv = char_w * 1.05
        else:
            kern_adv = char_w * kerning_factor

        char_layouts.append({
            "char": ch,
            "scale": scale,
            "font": font,
            "x": cur_x,
            "bbox": bbox,
            "is_target": is_target
        })
        cur_x += int(kern_adv)

    baseline_y = int(base_sz * 1.4) + margin
    total_w = cur_x + margin
    total_h = baseline_y + int(base_sz * 0.7) + margin

    # Masks
    core_mask = Image.new("L", (total_w, total_h), 0)
    d_core = ImageDraw.Draw(core_mask)

    white_mask = Image.new("L", (total_w, total_h), 0)
    d_white = ImageDraw.Draw(white_mask)

    navy_mask = Image.new("L", (total_w, total_h), 0)
    d_navy = ImageDraw.Draw(navy_mask)

    holes_accum = Image.new("L", (total_w, total_h), 0)

    # 1. Draw strokes for all letters onto masks
    for item in char_layouts:
        ch = item["char"]
        font = item["font"]
        cx = item["x"]

        # Outer navy stroke
        d_navy.text((cx, baseline_y), ch, font=font, stroke_width=w_stroke + n_stroke, fill=255, anchor="ls")

        # White stroke
        d_white.text((cx, baseline_y), ch, font=font, stroke_width=w_stroke, fill=255, anchor="ls")

        # Core letter body
        d_core.text((cx, baseline_y), ch, font=font, fill=255, anchor="ls")

        # Extract holes if target
        if item["is_target"]:
            h_local, (ox, oy) = get_char_counter_holes(ch, font)
            # Paste into holes_accum at global position
            holes_accum.paste(h_local, (cx + ox, baseline_y + oy), h_local)

    # Apply Hole logic (Filled vs Open)
    if fill_holes:
        # Fill holes in both white and navy layers so no hole shows through
        white_mask = ImageChops.lighter(white_mask, holes_accum)
        navy_mask = ImageChops.lighter(navy_mask, holes_accum)
    else:
        # Clear out holes from white and navy layers so background shows through cleanly
        cutout = ImageChops.subtract(holes_accum, core_mask)
        white_mask = ImageChops.subtract(white_mask, cutout)
        navy_mask = ImageChops.subtract(navy_mask, cutout)

    # 2. Drop Shadow Mask
    shadow_mask = Image.new("L", (total_w, total_h), 0)
    shadow_mask.paste(navy_mask, (s_offset, int(s_offset * 1.15)))
    shadow_mask = shadow_mask.filter(ImageFilter.GaussianBlur(max(3, int(s_offset * 0.55))))

    # 3. Composite Layers
    canvas = Image.new("RGBA", (total_w, total_h), (0, 0, 0, 0))

    # A. Drop shadow
    shadow_color = (15, 28, 52, 210)
    shadow_layer = Image.new("RGBA", (total_w, total_h), shadow_color)
    canvas.paste(shadow_layer, (0, 0), shadow_mask)

    # B. Outer navy outline
    navy_color = (10, 35, 75, 255)
    navy_layer = Image.new("RGBA", (total_w, total_h), navy_color)
    canvas.paste(navy_layer, (0, 0), navy_mask)

    # C. Thick white backing
    white_color = (255, 255, 255, 255)
    white_layer = Image.new("RGBA", (total_w, total_h), white_color)
    canvas.paste(white_layer, (0, 0), white_mask)

    # D. Inner comic line around core letters
    inner_stroke_mask = Image.new("L", (total_w, total_h), 0)
    d_inner = ImageDraw.Draw(inner_stroke_mask)
    for item in char_layouts:
        d_inner.text((item["x"], baseline_y), item["char"], font=item["font"], stroke_width=max(2, int(base_sz * 0.024)), fill=255, anchor="ls")
    canvas.paste(navy_layer, (0, 0), inner_stroke_mask)

    # E. Core gradient fill
    grad_top = (0, 195, 255, 255) # Bright electric cyan
    grad_bot = (0, 115, 235, 255) # Vivid comic azure
    grad = make_gradient(total_w, total_h, grad_top, grad_bot)
    canvas.paste(grad, (0, 0), core_mask)

    # Downsample
    out_w = total_w // ss
    out_h = total_h // ss
    result = canvas.resize((out_w, out_h), Image.Resampling.LANCZOS)

    # Crop
    bbox = result.getbbox()
    if bbox:
        m = 12
        x0 = max(0, bbox[0] - m)
        y0 = max(0, bbox[1] - m)
        x1 = min(out_w, bbox[2] + m)
        y1 = min(out_h, bbox[3] + m)
        result = result.crop((x0, y0, x1, y1))

    return result

def generate_comparison_sheet(out_open_path: str, out_filled_path: str, sheet_path: str):
    """Generates both versions and a side-by-side comparison sheet."""
    print("Generating Version A (Counters Open)...")
    img_open = render_logo(fill_holes=False)
    img_open.save(out_open_path, "PNG")
    print(f"Saved: {out_open_path}")

    print("Generating Version B (Counters Filled)...")
    img_filled = render_logo(fill_holes=True)
    img_filled.save(out_filled_path, "PNG")
    print(f"Saved: {out_filled_path}")

    # Create Comparison Sheet
    W = 1280
    H = 960
    sheet = Image.new("RGBA", (W, H), (24, 26, 32, 255))
    draw = ImageDraw.Draw(sheet)

    title_font = ImageFont.truetype(font_path, 23)
    heading_font = ImageFont.truetype(font_path, 17)
    body_font = ImageFont.truetype(font_path, 13)

    draw.text((40, 25), "MangaMeYeah! ロゴ ブラッシュアップ比較（白影一体化・太幅・M/M/Y拡大）", font=title_font, fill=(255, 255, 255, 255))
    draw.text((40, 62), "・条件：文字間タイト化 ＋ 白影を繋げて太く一体化 ＋ Mangaの『M』、Meの『M』、Yeah!の『Y』を拡大", font=body_font, fill=(150, 185, 220, 255))

    # Section 1: Version A (Holes Open)
    draw.rounded_rectangle([40, 100, W - 40, 360], radius=10, fill=(35, 42, 54, 255), outline=(75, 90, 115, 255), width=1)
    draw.text((55, 112), "【バージョン A】 穴が抜けているバージョン (Open Counters)", font=heading_font, fill=(0, 200, 255, 255))
    draw.text((55, 140), "・a, g, e の内側の穴（カウンター）が抜けており、背景色が見える標準的なタイポグラフィ仕様。", font=body_font, fill=(190, 210, 230, 255))
    
    scale_a = min(1.0, (W - 120) / img_open.width, 160 / img_open.height)
    fit_a = img_open.resize((int(img_open.width * scale_a), int(img_open.height * scale_a)), Image.Resampling.LANCZOS)
    sheet.paste(fit_a, ((W - fit_a.width) // 2, 175), fit_a)

    # Section 2: Version B (Holes Filled with White)
    draw.rounded_rectangle([40, 380, W - 40, 640], radius=10, fill=(35, 42, 54, 255), outline=(75, 90, 115, 255), width=1)
    draw.text((55, 392), "【バージョン B】 a, g, e の穴を白影で塗りつぶしたバージョン (Filled Counters)", font=heading_font, fill=(255, 215, 0, 255))
    draw.text((55, 420), "・a, g, e の穴が白影で埋まり、文字の芯が太くポップなステッカー・漫画ロゴ調の肉厚な印象。", font=body_font, fill=(190, 210, 230, 255))

    scale_b = min(1.0, (W - 120) / img_filled.width, 160 / img_filled.height)
    fit_b = img_filled.resize((int(img_filled.width * scale_b), int(img_filled.height * scale_b)), Image.Resampling.LANCZOS)
    sheet.paste(fit_b, ((W - fit_b.width) // 2, 455), fit_b)

    # Section 3: White Background Test Strip (Side-by-side on pure white)
    draw.rounded_rectangle([40, 660, W - 40, 930], radius=10, fill=(255, 255, 255, 255), outline=(200, 210, 220, 255), width=1)
    draw.text((55, 672), "【白背景での見え方比較 (White Background)】", font=heading_font, fill=(20, 30, 45, 255))

    mini_scale = 0.44
    mini_a = img_open.resize((int(img_open.width * mini_scale), int(img_open.height * mini_scale)), Image.Resampling.LANCZOS)
    mini_b = img_filled.resize((int(img_filled.width * mini_scale), int(img_filled.height * mini_scale)), Image.Resampling.LANCZOS)

    draw.text((60, 705), "▼ バージョン A（穴あき）", font=body_font, fill=(50, 70, 95, 255))
    sheet.paste(mini_a, (60, 740), mini_a)

    draw.text((660, 705), "▼ バージョン B（白影埋め）", font=body_font, fill=(50, 70, 95, 255))
    sheet.paste(mini_b, (660, 740), mini_b)

    sheet.save(sheet_path, "PNG")
    print(f"Generated comparison sheet: {sheet_path}")

if __name__ == "__main__":
    out_open = os.path.join(logo_dir, "logo_v2_open_counters.png")
    out_filled = os.path.join(logo_dir, "logo_v2_filled_counters.png")
    out_sheet = os.path.join(logo_dir, "logo_v2_comparison.png")
    generate_comparison_sheet(out_open, out_filled, out_sheet)
