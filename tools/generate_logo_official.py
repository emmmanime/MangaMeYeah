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
    e_y_kerning: float = 0.68, # Tighter left of Y
    y_e_kerning: float = 0.62, # Deeper tuck right of Y
    white_stroke_px: int = 24,
    navy_stroke_px: int = 13,
    shadow_offset_px: int = 16,
) -> Image.Image:
    """
    Renders official MangaMeYeah! logo with:
    - Version B: Counters filled with white
    - M, M, Y enlarged
    - Balanced tight kerning with right of Y tucked closely
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
        elif ch == "M":
            kern_adv = char_w * 0.90
        elif idx == 6: # e of Me before Y
            kern_adv = char_w * e_y_kerning
        elif ch == "Y": # Y before e of Yeah
            kern_adv = char_w * y_e_kerning
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

    core_mask = Image.new("L", (total_w, total_h), 0)
    d_core = ImageDraw.Draw(core_mask)

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
        d_core.text((cx, baseline_y), ch, font=font, fill=255, anchor="ls")

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

    # Composite
    canvas = Image.new("RGBA", (total_w, total_h), (0, 0, 0, 0))
    shadow_color = (15, 28, 52, 210)
    canvas.paste(Image.new("RGBA", (total_w, total_h), shadow_color), (0, 0), shadow_mask)

    navy_color = (10, 35, 75, 255)
    canvas.paste(Image.new("RGBA", (total_w, total_h), navy_color), (0, 0), navy_mask)
    canvas.paste(Image.new("RGBA", (total_w, total_h), (255, 255, 255, 255)), (0, 0), white_mask)

    inner_stroke_mask = Image.new("L", (total_w, total_h), 0)
    d_inner = ImageDraw.Draw(inner_stroke_mask)
    for item in char_layouts:
        d_inner.text((item["x"], baseline_y), item["char"], font=item["font"], stroke_width=max(2, int(base_sz * 0.024)), fill=255, anchor="ls")
    canvas.paste(Image.new("RGBA", (total_w, total_h), navy_color), (0, 0), inner_stroke_mask)

    grad = make_gradient(total_w, total_h, (0, 195, 255, 255), (0, 115, 235, 255))
    canvas.paste(grad, (0, 0), core_mask)

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
    Creates a 5:2 aspect ratio banner (1500 x 600) for X (Twitter) articles / cards.
    Contains:
    - 5:2 ratio (1500 x 600)
    - "漫" book icon + Refined MangaMeYeah! logo
    - Catchy, completely safe tagline without mentioning MangaMeeya
    - Feature badges and version info
    """
    W = 1500
    H = 600  # 1500:600 = 5:2
    banner = Image.new("RGBA", (W, H), (18, 22, 30, 255))
    draw = ImageDraw.Draw(banner)

    # 1. Subtle comic halftone dot grid
    for gx in range(0, W, 36):
        for gy in range(0, H, 36):
            draw.ellipse([gx - 1, gy - 1, gx + 1, gy + 1], fill=(35, 45, 60, 110))

    # 2. Ambient radial glow behind logo
    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    g_draw = ImageDraw.Draw(glow)
    g_draw.ellipse([W // 2 - 420, H // 2 - 250, W // 2 + 420, H // 2 + 180], fill=(0, 140, 255, 50))
    glow = glow.filter(ImageFilter.GaussianBlur(90))
    banner.paste(glow, (0, 0), glow)

    # 3. Book Icon ("漫")
    icon_img = gen_icon.draw_icon_canvas(256, "漫")
    icon_sz = 175
    icon_resized = icon_img.resize((icon_sz, icon_sz), Image.Resampling.LANCZOS)

    # 4. Logo sizing
    # Fit logo harmoniously next to icon
    logo_scale = 145 / logo_img.height
    logo_w = int(logo_img.width * logo_scale)
    logo_h = int(logo_img.height * logo_scale)
    logo_resized = logo_img.resize((logo_w, logo_h), Image.Resampling.LANCZOS)

    spacing = 28
    header_w = icon_sz + spacing + logo_w
    header_x = (W - header_w) // 2
    header_y = 110

    banner.paste(icon_resized, (header_x, header_y + 10), icon_resized)
    banner.paste(logo_resized, (header_x + icon_sz + spacing, header_y), logo_resized)

    # 5. Tagline (NO MangaMeeya name mentioned - completely safe!)
    tagline_font = ImageFont.truetype(font_path, 25)
    tagline = "サクサク快適に読める、Windows 11 向け新世代高速画像ビューワー"
    tb = draw.textbbox((0, 0), tagline, font=tagline_font)
    tw = tb[2] - tb[0]
    draw.text(((W - tw) // 2, 315), tagline, font=tagline_font, fill=(235, 244, 255, 255))

    # 6. Feature Badges (Pills)
    badges = ["[高速先読みキャッシュ]", "[ネスト書庫直接展開]", "[X分割画像自動結合]", "[MangaMeYeah.ini対応]", "[ポータブル対応]"]
    badge_font = ImageFont.truetype(font_path, 16)
    
    badge_w_list = []
    for b in badges:
        bb = draw.textbbox((0, 0), b, font=badge_font)
        badge_w_list.append((bb[2] - bb[0]) + 32)

    gap = 14
    total_badges_w = sum(badge_w_list) + gap * (len(badges) - 1)
    bx = (W - total_badges_w) // 2
    by = 385

    for idx, b in enumerate(badges):
        bw = badge_w_list[idx]
        bh = 38
        draw.rounded_rectangle([bx, by, bx + bw, by + bh], radius=19, fill=(28, 42, 65, 225), outline=(0, 165, 255, 190), width=1)
        bb = draw.textbbox((0, 0), b, font=badge_font)
        tx = bx + (bw - (bb[2] - bb[0])) // 2
        ty = by + (bh - (bb[3] - bb[1])) // 2 - 2
        draw.text((tx, ty), b, font=badge_font, fill=(240, 248, 255, 255))
        bx += bw + gap

    # 7. Version & Link footer
    foot_font = ImageFont.truetype(font_path, 16)
    foot_text = "v0.3.5 Public Pre-Release  |  github.com/emmmanime/MangaMeYeah"
    fb = draw.textbbox((0, 0), foot_text, font=foot_font)
    fw = fb[2] - fb[0]
    draw.text(((W - fw) // 2, 485), foot_text, font=foot_font, fill=(135, 165, 200, 255))

    banner.save(output_path, "PNG")
    print(f"Generated 5:2 Banner: {output_path}")

if __name__ == "__main__":
    print("Generating official refined logo...")
    logo = render_logo()
    
    logo_path = os.path.join(logo_dir, "logo_official_filled.png")
    logo.save(logo_path, "PNG")
    print(f"Saved: {logo_path}")

    # Generate 5:2 ratio banner (1500 x 600)
    banner_5to2_path = os.path.join(logo_dir, "banner_x_5to2.png")
    generate_5to2_banner(logo, banner_5to2_path)
