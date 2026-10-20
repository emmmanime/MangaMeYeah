import os
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageChops

repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
font_path = os.path.join(repo_root, "tools", "fonts", "RocknRollOne-Regular.ttf")
logo_dir = os.path.join(repo_root, "docs", "logo")
os.makedirs(logo_dir, exist_ok=True)

# Import icon generator for embedding the "漫" book icon
import importlib.util
spec = importlib.util.spec_from_file_location("gen_icon", os.path.join(repo_root, "tools", "generate_icon_c.py"))
gen_icon = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gen_icon)

# Letter configuration: (char, scale_multiplier, is_counter_target)
LETTERS_CONFIG = [
    ("M", 1.26, False), # 0: Manga M (large)
    ("a", 1.00, True),  # 1: a (outline preserved over M)
    ("n", 1.00, False), # 2: n
    ("g", 1.00, True),  # 3: g
    ("a", 1.00, True),  # 4: a (end of Manga)
    ("M", 1.26, False), # 5: Me M (large)
    ("e", 1.00, True),  # 6: e (end of Me)
    ("Y", 1.28, False), # 7: Yeah Y (large)
    ("e", 1.00, True),  # 8: e (tucked under Y)
    ("a", 1.00, True),  # 9: a
    ("h", 1.00, False), # 10: h
    ("!", 1.20, False), # 11: ! (punchy)
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
    """Finds inner counter holes of 'a', 'g', 'e' in local coordinates."""
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

    im_flood = im_closed.copy()
    ImageDraw.floodfill(im_flood, (0, 0), 128)
    holes = im_flood.point(lambda p: 255 if p == 0 else 0, mode="L")
    return holes, (-tx, -ty)

def render_logo(
    base_font_size: int = 140,
    a_M_adv_ratio: float = 0.96, # 5th 'a' to 'M' (clearly separated, not touching)
    e_Y_adv_ratio: float = 0.74, # 7th 'e' to 'Y' (balanced to match a-M gap)
    y_e_adv_ratio: float = 0.62, # 'Y' to 'e' (snugly tucked under Y arm)
    white_stroke_px: int = 24,
    navy_stroke_px: int = 13,
    shadow_offset_px: int = 16,
) -> Image.Image:
    """
    Renders official MangaMeYeah! logo with:
    - 2nd letter 'a' outline preserved on top of 1st letter 'M'
    - Balanced, clearly separated gaps between words (Manga-Me and Me-Yeah)
    - Version B: Counters filled with white
    - M, M, Y enlarged
    - Continuous, unified thick white backing
    - Supersampled 2x
    """
    ss = 2
    base_sz = base_font_size * ss
    w_stroke = white_stroke_px * ss
    n_stroke = navy_stroke_px * ss
    s_offset = shadow_offset_px * ss

    fonts = {}
    for _, scale, _ in LETTERS_CONFIG:
        sz = int(base_sz * scale)
        if sz not in fonts:
            fonts[sz] = ImageFont.truetype(font_path, sz)

    char_layouts = []
    margin = (w_stroke + n_stroke + s_offset) * 2 + 50
    cur_x = margin
    dummy = Image.new("RGBA", (10, 10))
    d_dum = ImageDraw.Draw(dummy)

    for idx, (ch, scale, is_target) in enumerate(LETTERS_CONFIG):
        font = fonts[int(base_sz * scale)]
        bbox = d_dum.textbbox((0, 0), ch, font=font, anchor="ls")
        char_w = bbox[2] - bbox[0]

        if idx == 0:
            kern_adv = char_w * 0.88
        elif idx == 4: # 5th letter 'a' (end of Manga) before M of Me
            kern_adv = char_w * a_M_adv_ratio
        elif ch == "M":
            kern_adv = char_w * 0.90
        elif idx == 6: # 7th letter 'e' (end of Me) before Y of Yeah
            kern_adv = char_w * e_Y_adv_ratio
        elif ch == "Y": # 8th letter 'Y' before 'e' of Yeah
            kern_adv = char_w * y_e_adv_ratio
        elif idx > 0 and LETTERS_CONFIG[idx - 1][0] in ["M"]:
            kern_adv = char_w * 0.78
        elif ch == "!":
            kern_adv = char_w * 1.05
        else:
            kern_adv = char_w * 0.82

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

    white_mask = Image.new("L", (total_w, total_h), 0)
    d_white = ImageDraw.Draw(white_mask)

    navy_mask = Image.new("L", (total_w, total_h), 0)
    d_navy = ImageDraw.Draw(navy_mask)

    holes_accum = Image.new("L", (total_w, total_h), 0)

    for item in char_layouts:
        ch = item["char"]
        font = item["font"]
        cx = item["x"]

        d_navy.text((cx, baseline_y), ch, font=font, stroke_width=w_stroke + n_stroke, fill=255, anchor="ls")
        d_white.text((cx, baseline_y), ch, font=font, stroke_width=w_stroke, fill=255, anchor="ls")

        if item["is_target"]:
            h_local, (ox, oy) = get_char_counter_holes(ch, font)
            holes_accum.paste(h_local, (cx + ox, baseline_y + oy), h_local)

    # Version B: Fill holes with white
    white_mask = ImageChops.lighter(white_mask, holes_accum)
    navy_mask = ImageChops.lighter(navy_mask, holes_accum)

    # Shadow
    shadow_mask = Image.new("L", (total_w, total_h), 0)
    shadow_mask.paste(navy_mask, (s_offset, int(s_offset * 1.15)))
    shadow_mask = shadow_mask.filter(ImageFilter.GaussianBlur(max(3, int(s_offset * 0.55))))

    # Composite Canvas
    canvas = Image.new("RGBA", (total_w, total_h), (0, 0, 0, 0))
    shadow_color = (15, 28, 52, 210)
    canvas.paste(Image.new("RGBA", (total_w, total_h), shadow_color), (0, 0), shadow_mask)

    navy_color = (10, 35, 75, 255)
    canvas.paste(Image.new("RGBA", (total_w, total_h), navy_color), (0, 0), navy_mask)
    canvas.paste(Image.new("RGBA", (total_w, total_h), (255, 255, 255, 255)), (0, 0), white_mask)

    # Exact same blue as book icon: (0, 140, 245, 255)
    blue_core = Image.new("RGBA", (total_w, total_h), (0, 140, 245, 255))

    # Draw each character LAYERED (left to right)
    # This ensures letter 1 ('a') has its left navy stroke sitting ON TOP of letter 0 ('M'),
    # so 'a' outline is 100% visible and never swallowed!
    inner_stroke_w = max(2, int(base_sz * 0.024))
    for item in char_layouts:
        ch = item["char"]
        font = item["font"]
        cx = item["x"]

        # Inner stroke of character
        ch_stroke_mask = Image.new("L", (total_w, total_h), 0)
        d_chs = ImageDraw.Draw(ch_stroke_mask)
        d_chs.text((cx, baseline_y), ch, font=font, stroke_width=inner_stroke_w, fill=255, anchor="ls")
        canvas.paste(Image.new("RGBA", (total_w, total_h), navy_color), (0, 0), ch_stroke_mask)

        # Core blue fill of character (exact icon color)
        ch_fill_mask = Image.new("L", (total_w, total_h), 0)
        d_chf = ImageDraw.Draw(ch_fill_mask)
        d_chf.text((cx, baseline_y), ch, font=font, fill=255, anchor="ls")
        canvas.paste(blue_core, (0, 0), ch_fill_mask)

    out_w = total_w // ss
    out_h = total_h // ss
    result = canvas.resize((out_w, out_h), Image.Resampling.LANCZOS)
    bbox = result.getbbox()
    if bbox:
        m = 12
        result = result.crop((max(0, bbox[0] - m), max(0, bbox[1] - m), min(out_w, bbox[2] + m), min(out_h, bbox[3] + m)))

    return result

def generate_5to2_banner(logo_img: Image.Image, output_path: str):
    """
    Creates a clean, versatile 5:2 ratio banner (1500 x 600) for X articles & announcements.
    - ONLY the "漫" book icon and the official MangaMeYeah! logo.
    - Icon and Logo have top and bottom aligned (上下を合わせる).
    - Maximized in size within the 5:2 canvas.
    - Fully reusable for future releases and posts (no release-specific text).
    """
    W = 1500
    H = 600  # 1500:600 = exactly 5:2 ratio
    banner = Image.new("RGBA", (W, H), (18, 22, 30, 255))
    draw = ImageDraw.Draw(banner)

    # 1. Subtle comic halftone dot mesh
    for gx in range(0, W, 36):
        for gy in range(0, H, 36):
            draw.ellipse([gx - 1, gy - 1, gx + 1, gy + 1], fill=(35, 45, 60, 110))

    # 2. Ambient radial backlight glow centered behind content
    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    g_draw = ImageDraw.Draw(glow)
    g_draw.ellipse([W // 2 - 500, H // 2 - 250, W // 2 + 500, H // 2 + 250], fill=(0, 140, 255, 60))
    glow = glow.filter(ImageFilter.GaussianBlur(100))
    banner.paste(glow, (0, 0), glow)

    # 3. Book Icon ("漫")
    icon_raw = gen_icon.draw_icon_canvas(512, "漫")

    # Target content height: maximize within H=600 with clean balanced margins
    # We want icon and logo heights to match 1:1 (上下を合わせる)
    target_content_h = 360  # Leaves 120px top and 120px bottom in 600px height

    # Scale icon to target_content_h
    icon_resized = icon_raw.resize((target_content_h, target_content_h), Image.Resampling.LANCZOS)

    # Scale logo so its height matches icon height exactly!
    logo_scale = target_content_h / logo_img.height
    logo_target_w = int(logo_img.width * logo_scale)
    logo_resized = logo_img.resize((logo_target_w, target_content_h), Image.Resampling.LANCZOS)

    # Gap between icon and logo
    gap = 40
    total_content_w = target_content_h + gap + logo_target_w

    # If total_content_w exceeds width with margins (e.g. 1400), scale down slightly
    max_w = 1420
    if total_content_w > max_w:
        ratio = max_w / total_content_w
        target_content_h = int(target_content_h * ratio)
        icon_resized = icon_raw.resize((target_content_h, target_content_h), Image.Resampling.LANCZOS)
        logo_target_w = int(logo_img.width * (target_content_h / logo_img.height))
        logo_resized = logo_img.resize((logo_target_w, target_content_h), Image.Resampling.LANCZOS)
        total_content_w = target_content_h + gap + logo_target_w

    # Center horizontally and vertically
    start_x = (W - total_content_w) // 2
    start_y = (H - target_content_h) // 2

    # Paste icon and logo: perfectly aligned top and bottom!
    banner.paste(icon_resized, (start_x, start_y), icon_resized)
    banner.paste(logo_resized, (start_x + target_content_h + gap, start_y), logo_resized)

    banner.save(output_path, "PNG")
    print(f"Generated 5:2 Versatile Banner: {output_path}")

if __name__ == "__main__":
    print("Generating refined official logo...")
    logo = render_logo()

    logo_path = os.path.join(logo_dir, "logo_official_filled.png")
    logo.save(logo_path, "PNG")
    print(f"Saved: {logo_path}")

    # Generate 5:2 versatile banner (1500 x 600)
    banner_5to2_path = os.path.join(logo_dir, "banner_x_5to2.png")
    generate_5to2_banner(logo, banner_5to2_path)
