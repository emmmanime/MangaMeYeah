import os
import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageChops

repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
font_path = os.path.join(repo_root, "tools", "fonts", "RocknRollOne-Regular.ttf")
logo_dir = os.path.join(repo_root, "docs", "logo")
os.makedirs(logo_dir, exist_ok=True)

# Import icon generator to get the exact "漫" book icon for comparison
import importlib.util
spec = importlib.util.spec_from_file_location("gen_icon", os.path.join(repo_root, "tools", "generate_icon_c.py"))
gen_icon = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gen_icon)

# Exact colors from icon_c
BLUE_COLOR = (0, 140, 245, 255)         # Icon "漫" vivid blue
WHITE_COLOR = (255, 255, 255, 255)      # Pure white rim
NAVY_COLOR = (10, 35, 75, 255)          # Dark navy outer border
SHADOW_COLOR = (15, 28, 52, 200)        # Soft drop shadow

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

def get_char_counter_holes(char: str, font: ImageFont.FreeTypeFont) -> tuple:
    """Finds inner counter holes of 'a', 'g', 'e' in local coordinates."""
    bbox = font.getbbox(char, anchor="ls")
    pad = 16
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

def compute_layout(base_sz: int, a_M_adv_ratio: float = 0.96, e_Y_adv_ratio: float = 0.74, y_e_adv_ratio: float = 0.62, margin: int = 150):
    fonts = {}
    for _, scale, _ in LETTERS_CONFIG:
        sz = int(base_sz * scale)
        if sz not in fonts:
            fonts[sz] = ImageFont.truetype(font_path, sz)

    char_layouts = []
    cur_x = margin
    dummy = Image.new("RGBA", (10, 10))
    d_dum = ImageDraw.Draw(dummy)

    for idx, (ch, scale, is_target) in enumerate(LETTERS_CONFIG):
        font = fonts[int(base_sz * scale)]
        bbox = d_dum.textbbox((0, 0), ch, font=font, anchor="ls")
        char_w = bbox[2] - bbox[0]

        if idx == 0:
            kern_adv = char_w * 0.88
        elif idx == 4: # 5th letter 'a' before M
            kern_adv = char_w * a_M_adv_ratio
        elif ch == "M":
            kern_adv = char_w * 0.90
        elif idx == 6: # 7th letter 'e' before Y
            kern_adv = char_w * e_Y_adv_ratio
        elif ch == "Y": # 8th letter 'Y' before 'e'
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

    return char_layouts, baseline_y, total_w, total_h

def render_logo_plan_a(base_font_size: int = 140) -> Image.Image:
    """
    Plan A:
    - Order: Blue text -> White rim -> Dark Navy border -> Drop shadow
    - White rim width proportional to icon (crisp, narrow: ~3.7% of font size)
    - Directly preserves sharp apexes and corners as well as smooth curves
    - 2nd letter 'a' white rim sits cleanly on 1st letter 'M' without black dividing lines
    """
    ss = 2
    base_sz = base_font_size * ss
    w_stroke = max(2, int(base_sz * 0.040)) # ~11px (at 2x ss, so 5.5px in final 1x)
    n_stroke = max(2, int(base_sz * 0.075)) # ~21px (at 2x ss, so 10.5px in final 1x)
    s_offset = max(2, int(base_sz * 0.045)) # ~13px (at 2x ss, so 6.5px in final 1x)
    margin = (w_stroke + n_stroke + s_offset) * 2 + 50

    char_layouts, baseline_y, total_w, total_h = compute_layout(base_sz, margin=margin)

    # 1. Dark navy outer mask
    navy_mask = Image.new("L", (total_w, total_h), 0)
    d_navy = ImageDraw.Draw(navy_mask)

    # 2. White rim mask
    white_mask = Image.new("L", (total_w, total_h), 0)
    d_white = ImageDraw.Draw(white_mask)

    # 3. Holes accumulator
    holes_accum = Image.new("L", (total_w, total_h), 0)

    for item in char_layouts:
        ch = item["char"]
        font = item["font"]
        cx = item["x"]

        d_navy.text((cx, baseline_y), ch, font=font, stroke_width=n_stroke, fill=255, anchor="ls")
        d_white.text((cx, baseline_y), ch, font=font, stroke_width=w_stroke, fill=255, anchor="ls")

        if item["is_target"]:
            h_local, (ox, oy) = get_char_counter_holes(ch, font)
            holes_accum.paste(h_local, (cx + ox, baseline_y + oy), h_local)

    # Fill holes in white
    white_mask = ImageChops.lighter(white_mask, holes_accum)

    # 4. Shadow mask
    shadow_mask = Image.new("L", (total_w, total_h), 0)
    shadow_mask.paste(navy_mask, (s_offset, int(s_offset * 1.15)))
    shadow_mask = shadow_mask.filter(ImageFilter.GaussianBlur(max(2, int(s_offset * 0.55))))

    # Composite canvas
    canvas = Image.new("RGBA", (total_w, total_h), (0, 0, 0, 0))
    canvas.paste(Image.new("RGBA", (total_w, total_h), SHADOW_COLOR), (0, 0), shadow_mask)
    canvas.paste(Image.new("RGBA", (total_w, total_h), NAVY_COLOR), (0, 0), navy_mask)
    canvas.paste(Image.new("RGBA", (total_w, total_h), WHITE_COLOR), (0, 0), white_mask)

    # 5. Draw Blue letters (Layered left-to-right)
    # For letter 1 ('a') overlapping letter 0 ('M'): draw 'a' white rim on top of 'M' so 'a' has a clean white rim over M!
    for idx, item in enumerate(char_layouts):
        ch = item["char"]
        font = item["font"]
        cx = item["x"]

        # If letter 1 ('a'), draw its individual white rim to cleanly overlay over 'M'
        if idx == 1:
            a_white_mask = Image.new("L", (total_w, total_h), 0)
            d_aw = ImageDraw.Draw(a_white_mask)
            d_aw.text((cx, baseline_y), ch, font=font, stroke_width=w_stroke, fill=255, anchor="ls")
            canvas.paste(Image.new("RGBA", (total_w, total_h), WHITE_COLOR), (0, 0), a_white_mask)

        # Draw blue character core (solid icon blue)
        ch_fill_mask = Image.new("L", (total_w, total_h), 0)
        d_chf = ImageDraw.Draw(ch_fill_mask)
        d_chf.text((cx, baseline_y), ch, font=font, fill=255, anchor="ls")
        canvas.paste(Image.new("RGBA", (total_w, total_h), BLUE_COLOR), (0, 0), ch_fill_mask)

    out_w = total_w // ss
    out_h = total_h // ss
    result = canvas.resize((out_w, out_h), Image.Resampling.LANCZOS)
    bbox = result.getbbox()
    if bbox:
        m = 12
        result = result.crop((max(0, bbox[0] - m), max(0, bbox[1] - m), min(out_w, bbox[2] + m), min(out_h, bbox[3] + m)))
    return result

def render_logo_plan_b(base_font_size: int = 140) -> Image.Image:
    """
    Plan B:
    - Order: Blue text -> Wide White rim -> Dark Navy border -> Drop shadow
    - White rim width is thick, matching current logo's wide backing (w_stroke ~ 48px at 2x)
    - Blue text directly touches White rim (NO black line between blue and white!)
    - Preserves sharp angular profile vs round curves by applying corner miter / faceted dilation
    """
    ss = 2
    base_sz = base_font_size * ss
    w_stroke = 24 * ss   # 48px at 2x (24px in 1x)
    n_stroke = 11 * ss   # 22px at 2x (11px in 1x)
    s_offset = 15 * ss   # 30px at 2x (15px in 1x)
    margin = (w_stroke + n_stroke + s_offset) * 2 + 50

    char_layouts, baseline_y, total_w, total_h = compute_layout(base_sz, margin=margin)

    # 1. Generate text core mask
    core_mask = Image.new("L", (total_w, total_h), 0)
    d_core = ImageDraw.Draw(core_mask)
    holes_accum = Image.new("L", (total_w, total_h), 0)

    for item in char_layouts:
        ch = item["char"]
        font = item["font"]
        cx = item["x"]
        d_core.text((cx, baseline_y), ch, font=font, fill=255, anchor="ls")

        if item["is_target"]:
            h_local, (ox, oy) = get_char_counter_holes(ch, font)
            holes_accum.paste(h_local, (cx + ox, baseline_y + oy), h_local)

    core_arr = np.array(core_mask)

    # 2. Corner-preserving dilation for White rim
    # We construct an octagonal / faceted kernel that maintains sharp vertices of M, Y, !
    r_w = w_stroke
    k_w = np.zeros((r_w * 2 + 1, r_w * 2 + 1), dtype=np.uint8)
    cv2.circle(k_w, (r_w, r_w), r_w, 255, -1)
    # Cut 45 degree corners for miter joins
    cut = int(r_w * 0.45)
    pts_miter = np.array([
        [cut, 0], [r_w * 2 - cut, 0],
        [r_w * 2, cut], [r_w * 2, r_w * 2 - cut],
        [r_w * 2 - cut, r_w * 2], [cut, r_w * 2],
        [0, r_w * 2 - cut], [0, cut]
    ], dtype=np.int32)
    cv2.fillPoly(k_w, [pts_miter], 255)

    white_arr = cv2.dilate(core_arr, k_w)
    white_mask = Image.fromarray(white_arr)
    # Fill counter holes
    white_mask = ImageChops.lighter(white_mask, holes_accum)

    # 3. Navy outer outline (dilated from white_mask)
    r_n = n_stroke
    k_n = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (r_n * 2 + 1, r_n * 2 + 1))
    navy_arr = cv2.dilate(np.array(white_mask), k_n)
    navy_mask = Image.fromarray(navy_arr)

    # 4. Shadow mask
    shadow_mask = Image.new("L", (total_w, total_h), 0)
    shadow_mask.paste(navy_mask, (s_offset, int(s_offset * 1.15)))
    shadow_mask = shadow_mask.filter(ImageFilter.GaussianBlur(max(2, int(s_offset * 0.55))))

    # Composite canvas
    canvas = Image.new("RGBA", (total_w, total_h), (0, 0, 0, 0))
    canvas.paste(Image.new("RGBA", (total_w, total_h), SHADOW_COLOR), (0, 0), shadow_mask)
    canvas.paste(Image.new("RGBA", (total_w, total_h), NAVY_COLOR), (0, 0), navy_mask)
    canvas.paste(Image.new("RGBA", (total_w, total_h), WHITE_COLOR), (0, 0), white_mask)

    # 5. Draw Blue letters (Layered left-to-right)
    # Blue letters directly touch the white background!
    for idx, item in enumerate(char_layouts):
        ch = item["char"]
        font = item["font"]
        cx = item["x"]

        # Letter 1 ('a') white rim over 'M' so 'a' outline is clean
        if idx == 1:
            a_white_mask = Image.new("L", (total_w, total_h), 0)
            d_aw = ImageDraw.Draw(a_white_mask)
            d_aw.text((cx, baseline_y), ch, font=font, stroke_width=max(2, int(base_sz * 0.035)), fill=255, anchor="ls")
            canvas.paste(Image.new("RGBA", (total_w, total_h), WHITE_COLOR), (0, 0), a_white_mask)

        ch_fill_mask = Image.new("L", (total_w, total_h), 0)
        d_chf = ImageDraw.Draw(ch_fill_mask)
        d_chf.text((cx, baseline_y), ch, font=font, fill=255, anchor="ls")
        canvas.paste(Image.new("RGBA", (total_w, total_h), BLUE_COLOR), (0, 0), ch_fill_mask)

    out_w = total_w // ss
    out_h = total_h // ss
    result = canvas.resize((out_w, out_h), Image.Resampling.LANCZOS)
    bbox = result.getbbox()
    if bbox:
        m = 12
        result = result.crop((max(0, bbox[0] - m), max(0, bbox[1] - m), min(out_w, bbox[2] + m), min(out_h, bbox[3] + m)))
    return result

def create_comparison_sheet(plan_a_img: Image.Image, plan_b_img: Image.Image, output_path: str):
    """
    Creates a comprehensive comparison sheet showing:
    1. Reference Book Icon ("漫")
    2. Current Logo (with inner navy stroke)
    3. Plan A (Icon-proportional white rim)
    4. Plan B (Current wide white rim with corner preservation)
    on both Dark (#181A20) and Light (#F5F6F8) backgrounds.
    """
    # Load current official logo
    curr_path = os.path.join(logo_dir, "logo_official_filled.png")
    curr_logo = Image.open(curr_path).convert("RGBA") if os.path.exists(curr_path) else plan_b_img

    # Generate 180px book icon
    icon_180 = gen_icon.draw_icon_canvas(180, "漫")

    # Canvas dimensions
    W = 1400
    H = 1000
    sheet = Image.new("RGBA", (W, H), (22, 26, 34, 255))
    draw = ImageDraw.Draw(sheet)

    # Fonts for labels
    label_font_path = r"C:\Windows\Fonts\meiryob.ttc"
    if not os.path.exists(label_font_path):
        label_font_path = font_path
    f_title = ImageFont.truetype(label_font_path, 26)
    f_sub = ImageFont.truetype(label_font_path, 18)
    f_desc = ImageFont.truetype(label_font_path, 15)

    # Header
    draw.text((40, 25), "MangaMeYeah! ロゴ新デザイン比較（文字 → 白の縁取り → 黒の縁取り）", font=f_title, fill=(255, 255, 255, 255))
    draw.text((40, 65), "青の文字色を完全統一し、青の周りを「白」で縁取ることで発色と視認性をアイコンと同調", font=f_desc, fill=(160, 185, 215, 255))

    # Reference Icon Card (Top Right)
    draw.rounded_rectangle([980, 20, 1360, 220], radius=12, fill=(30, 36, 48, 255), outline=(50, 65, 88, 255), width=2)
    draw.text((1000, 35), "【基準】アイコン（漫）", font=f_sub, fill=(0, 195, 255, 255))
    draw.text((1000, 60), "文字(青) → 白フチ → 濃紺フチ", font=f_desc, fill=(200, 210, 225, 255))
    sheet.paste(icon_180, (1080, 85), icon_180)

    # Section 1: Previous (Current) Logo
    draw.text((40, 120), "■ 現行ロゴ（参考：青の周りに黒縁 → 太い白影）", font=f_sub, fill=(180, 190, 205, 255))
    curr_scaled = curr_logo.resize((int(curr_logo.width * 0.42), int(curr_logo.height * 0.42)), Image.Resampling.LANCZOS)
    sheet.paste(curr_scaled, (60, 150), curr_scaled)

    # Section 2: Plan A
    draw.line([(40, 255), (1360, 255)], fill=(45, 55, 75, 255), width=2)
    draw.text((40, 275), "★ A案：白の縁取り幅をアイコンと同じにする（アイコン準拠・シャープ）", font=f_title, fill=(70, 220, 150, 255))
    draw.text((40, 315), "・白フチ幅が細身で、各文字の鋭角（M, Yの頂点）と丸み（a, g, e）が極めてシャープに際立ちます。\n・文字の周りに黒が一切なく、アイコンと100%同じ「文字(青) → 白フチ → 濃紺」の比率です。", font=f_desc, fill=(210, 220, 235, 255))

    plan_a_scaled = plan_a_img.resize((int(plan_a_img.width * 0.48), int(plan_a_img.height * 0.48)), Image.Resampling.LANCZOS)
    # Dark card
    draw.rounded_rectangle([40, 370, 680, 560], radius=10, fill=(15, 18, 24, 255), outline=(40, 50, 70, 255))
    draw.text((55, 380), "[ダーク背景]", font=f_desc, fill=(140, 160, 180, 255))
    sheet.paste(plan_a_scaled, (60, 420), plan_a_scaled)

    # Light card
    draw.rounded_rectangle([720, 370, 1360, 560], radius=10, fill=(245, 246, 248, 255), outline=(190, 200, 215, 255))
    draw.text((735, 380), "[ライト背景]", font=f_desc, fill=(90, 105, 125, 255))
    sheet.paste(plan_a_scaled, (740, 420), plan_a_scaled)

    # Section 3: Plan B
    draw.line([(40, 590), (1360, 590)], fill=(45, 55, 75, 255), width=2)
    draw.text((40, 610), "★ B案：白の縁取り幅を現在のロゴと同じにする（太め白プレート・角出し調整）", font=f_title, fill=(255, 200, 80, 255))
    draw.text((40, 650), "・白フチを現行ロゴ同様の太さ（24px相当）にしつつ、青の周りの黒縁を排除して白に直結。\n・丸いソーセージ状にならず、MやYの頂点の鋭角性を維持する角出し（ファセット）処理を適用。", font=f_desc, fill=(210, 220, 235, 255))

    plan_b_scaled = plan_b_img.resize((int(plan_b_img.width * 0.48), int(plan_b_img.height * 0.48)), Image.Resampling.LANCZOS)
    # Dark card
    draw.rounded_rectangle([40, 710, 680, 930], radius=10, fill=(15, 18, 24, 255), outline=(40, 50, 70, 255))
    draw.text((55, 720), "[ダーク背景]", font=f_desc, fill=(140, 160, 180, 255))
    sheet.paste(plan_b_scaled, (60, 770), plan_b_scaled)

    # Light card
    draw.rounded_rectangle([720, 710, 1360, 930], radius=10, fill=(245, 246, 248, 255), outline=(190, 200, 215, 255))
    draw.text((735, 720), "[ライト背景]", font=f_desc, fill=(90, 105, 125, 255))
    sheet.paste(plan_b_scaled, (740, 770), plan_b_scaled)

    sheet.save(output_path, "PNG")
    print(f"Saved comparison sheet: {output_path}")

if __name__ == "__main__":
    print("Generating Plan A...")
    plan_a = render_logo_plan_a()
    path_a = os.path.join(logo_dir, "logo_plan_a.png")
    plan_a.save(path_a, "PNG")
    print(f"Saved Plan A: {path_a}")

    print("Generating Plan B...")
    plan_b = render_logo_plan_b()
    path_b = os.path.join(logo_dir, "logo_plan_b.png")
    plan_b.save(path_b, "PNG")
    print(f"Saved Plan B: {path_b}")

    print("Creating comparison sheet...")
    comp_path = os.path.join(logo_dir, "logo_ab_comparison.png")
    create_comparison_sheet(plan_a, plan_b, comp_path)
