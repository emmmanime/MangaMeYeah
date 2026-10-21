import os
import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageChops

repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
font_path = os.path.join(repo_root, "tools", "fonts", "RocknRollOne-Regular.ttf")
logo_dir = os.path.join(repo_root, "docs", "logo")
os.makedirs(logo_dir, exist_ok=True)

# Import icon generator
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

def render_logo_plan_a(base_font_size: int = 140, open_counters: bool = False) -> Image.Image:
    """
    Renders Plan A (Icon-proportional white rim):
    - open_counters = False: A案（穴白埋め / Filled Counters）
    - open_counters = True:  A案その2（穴透過抜き / Open Counters）
    """
    ss = 2
    base_sz = base_font_size * ss
    w_stroke = max(2, int(base_sz * 0.040)) # ~11px (at 2x ss, so 5.5px in final 1x)
    n_stroke = max(2, int(base_sz * 0.075)) # ~21px (at 2x ss, so 10.5px in final 1x)
    s_offset = max(2, int(base_sz * 0.045)) # ~13px (at 2x ss, so 6.5px in final 1x)
    margin = (w_stroke + n_stroke + s_offset) * 2 + 50

    char_layouts, baseline_y, total_w, total_h = compute_layout(base_sz, margin=margin)

    # 1. Stroke masks
    navy_mask = Image.new("L", (total_w, total_h), 0)
    d_navy = ImageDraw.Draw(navy_mask)

    white_mask = Image.new("L", (total_w, total_h), 0)
    d_white = ImageDraw.Draw(white_mask)

    holes_accum = Image.new("L", (total_w, total_h), 0)
    punch_mask = Image.new("L", (total_w, total_h), 0)

    for item in char_layouts:
        ch = item["char"]
        font = item["font"]
        cx = item["x"]

        d_navy.text((cx, baseline_y), ch, font=font, stroke_width=n_stroke, fill=255, anchor="ls")
        d_white.text((cx, baseline_y), ch, font=font, stroke_width=w_stroke, fill=255, anchor="ls")

        if item["is_target"]:
            h_local, (ox, oy) = get_char_counter_holes(ch, font)
            if not open_counters:
                holes_accum.paste(h_local, (cx + ox, baseline_y + oy), h_local)
            else:
                # Erode hole by w_stroke to ensure the white rim along the inside of the hole
                # has the EXACT same width (w_stroke) as the outside!
                r_erode = max(2, int(w_stroke * 0.95))
                k = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (r_erode * 2 + 1, r_erode * 2 + 1))
                h_eroded = cv2.erode(np.array(h_local), k)
                punch_mask.paste(Image.fromarray(h_eroded), (cx + ox, baseline_y + oy), Image.fromarray(h_eroded))

    if not open_counters:
        # A案: Fill holes in white
        white_mask = ImageChops.lighter(white_mask, holes_accum)
    else:
        # A案その2: Clear punch_mask from white and navy masks
        white_mask = ImageChops.subtract(white_mask, punch_mask)
        navy_mask = ImageChops.subtract(navy_mask, punch_mask)

    # Shadow mask
    shadow_mask = Image.new("L", (total_w, total_h), 0)
    shadow_mask.paste(navy_mask, (s_offset, int(s_offset * 1.15)))
    if open_counters:
        shadow_mask = ImageChops.subtract(shadow_mask, punch_mask)
    shadow_mask = shadow_mask.filter(ImageFilter.GaussianBlur(max(2, int(s_offset * 0.55))))
    if open_counters:
        shadow_mask = ImageChops.subtract(shadow_mask, punch_mask)

    # Composite canvas
    canvas = Image.new("RGBA", (total_w, total_h), (0, 0, 0, 0))
    canvas.paste(Image.new("RGBA", (total_w, total_h), SHADOW_COLOR), (0, 0), shadow_mask)
    canvas.paste(Image.new("RGBA", (total_w, total_h), NAVY_COLOR), (0, 0), navy_mask)
    canvas.paste(Image.new("RGBA", (total_w, total_h), WHITE_COLOR), (0, 0), white_mask)

    # Draw Blue letters (Layered left-to-right)
    for idx, item in enumerate(char_layouts):
        ch = item["char"]
        font = item["font"]
        cx = item["x"]

        # Letter 1 ('a') white rim over 'M'
        if idx == 1:
            a_white_mask = Image.new("L", (total_w, total_h), 0)
            d_aw = ImageDraw.Draw(a_white_mask)
            d_aw.text((cx, baseline_y), ch, font=font, stroke_width=w_stroke, fill=255, anchor="ls")
            if open_counters:
                a_white_mask = ImageChops.subtract(a_white_mask, punch_mask)
            canvas.paste(Image.new("RGBA", (total_w, total_h), WHITE_COLOR), (0, 0), a_white_mask)

        # Core blue fill of character
        ch_fill_mask = Image.new("L", (total_w, total_h), 0)
        d_chf = ImageDraw.Draw(ch_fill_mask)
        d_chf.text((cx, baseline_y), ch, font=font, fill=255, anchor="ls")
        if open_counters:
            ch_fill_mask = ImageChops.subtract(ch_fill_mask, punch_mask)
        canvas.paste(Image.new("RGBA", (total_w, total_h), BLUE_COLOR), (0, 0), ch_fill_mask)

    if open_counters:
        # Ensure 100% transparent alpha in punched hole areas
        c_arr = np.array(canvas)
        p_arr = np.array(punch_mask)
        c_arr[p_arr > 128, 3] = 0
        canvas = Image.fromarray(c_arr)

    out_w = total_w // ss
    out_h = total_h // ss
    result = canvas.resize((out_w, out_h), Image.Resampling.LANCZOS)
    bbox = result.getbbox()
    if bbox:
        m = 12
        result = result.crop((max(0, bbox[0] - m), max(0, bbox[1] - m), min(out_w, bbox[2] + m), min(out_h, bbox[3] + m)))
    return result

def create_a_vs_a2_comparison_sheet(plan_a_img: Image.Image, plan_a2_img: Image.Image, output_path: str):
    """
    Creates a dedicated comparison sheet between A案 (穴白埋め) and A案その2 (穴透過抜き).
    Shows both on Dark (#161A22) and Light (#F5F6F8) backgrounds.
    """
    icon_180 = gen_icon.draw_icon_canvas(180, "漫")

    W = 1400
    H = 820
    sheet = Image.new("RGBA", (W, H), (22, 26, 34, 255))
    draw = ImageDraw.Draw(sheet)

    label_font_path = r"C:\Windows\Fonts\meiryob.ttc"
    if not os.path.exists(label_font_path):
        label_font_path = font_path
    f_title = ImageFont.truetype(label_font_path, 25)
    f_sub = ImageFont.truetype(label_font_path, 18)
    f_desc = ImageFont.truetype(label_font_path, 14)

    # Header
    draw.text((40, 25), "MangaMeYeah! ロゴ最終比較：A案（穴白埋め） vs A案その2（穴透過抜き）", font=f_title, fill=(255, 255, 255, 255))
    draw.text((40, 65), "文字(青) → 白フチ → 濃紺フチの階層を完全維持したまま、a, g, e の穴（カウンター）の処理を比較", font=f_desc, fill=(160, 185, 215, 255))

    # Reference Icon Card (Top Right)
    draw.rounded_rectangle([980, 20, 1360, 200], radius=12, fill=(30, 36, 48, 255), outline=(50, 65, 88, 255), width=2)
    draw.text((1000, 32), "【基準】アイコン（漫）", font=f_sub, fill=(0, 195, 255, 255))
    draw.text((1000, 56), "文字色・白フチ比率を同調", font=f_desc, fill=(200, 210, 225, 255))
    sheet.paste(icon_180, (1080, 75), icon_180)

    # Section 1: A案（穴白埋め / Filled）
    draw.text((40, 125), "★ A案（穴白埋め）：文字の穴の中を白影で塗りつぶしたバージョン", font=f_title, fill=(70, 220, 150, 255))
    draw.text((40, 160), "・ポップアート的な塊感（ソリッド感）があり、小さい縮小表示でも文字穴が潰れず安定した視認性を維持。", font=f_desc, fill=(210, 220, 235, 255))

    plan_a_scaled = plan_a_img.resize((int(plan_a_img.width * 0.46), int(plan_a_img.height * 0.46)), Image.Resampling.LANCZOS)
    # Dark card
    draw.rounded_rectangle([40, 200, 680, 390], radius=10, fill=(15, 18, 24, 255), outline=(40, 50, 70, 255))
    draw.text((55, 210), "[ダーク背景]", font=f_desc, fill=(140, 160, 180, 255))
    sheet.paste(plan_a_scaled, (55, 250), plan_a_scaled)

    # Light card
    draw.rounded_rectangle([720, 200, 1360, 390], radius=10, fill=(245, 246, 248, 255), outline=(190, 200, 215, 255))
    draw.text((735, 210), "[ライト背景]", font=f_desc, fill=(90, 105, 125, 255))
    sheet.paste(plan_a_scaled, (735, 250), plan_a_scaled)

    # Section 2: A案その2（穴透過抜き / Open）
    draw.line([(40, 420), (1360, 420)], fill=(45, 55, 75, 255), width=2)
    draw.text((40, 440), "★ A案その2（穴透過抜き）：穴の中を背景透過＋白フチ幅を均一に維持", font=f_title, fill=(0, 215, 255, 255))
    draw.text((40, 475), "・タイポグラフィ本来の輪郭が素直に出るデザイン。穴の内側も外周と同じ太さ（5.5px相当）の白フチで保護。", font=f_desc, fill=(210, 220, 235, 255))

    plan_a2_scaled = plan_a2_img.resize((int(plan_a2_img.width * 0.46), int(plan_a2_img.height * 0.46)), Image.Resampling.LANCZOS)
    # Dark card
    draw.rounded_rectangle([40, 515, 680, 705], radius=10, fill=(15, 18, 24, 255), outline=(40, 50, 70, 255))
    draw.text((55, 525), "[ダーク背景（背景色が穴から透過）]", font=f_desc, fill=(140, 160, 180, 255))
    sheet.paste(plan_a2_scaled, (55, 565), plan_a2_scaled)

    # Light card
    draw.rounded_rectangle([720, 515, 1360, 705], radius=10, fill=(245, 246, 248, 255), outline=(190, 200, 215, 255))
    draw.text((735, 525), "[ライト背景（白背景が穴から透過）]", font=f_desc, fill=(90, 105, 125, 255))
    sheet.paste(plan_a2_scaled, (735, 565), plan_a2_scaled)

    sheet.save(output_path, "PNG")
    print(f"Saved comparison sheet: {output_path}")

def generate_balanced_5to2_banner(logo_img: Image.Image, output_path: str):
    """
    Creates a balanced 5:2 ratio banner (1500 x 600) for X.
    Tuned visual balance:
    - In the previous banner, the icon was too massive (360x360 block) and the logo felt underpowered.
    - Here, the logo is given prominent height (~330px), bold and commanding.
    - The icon is sized harmoniously (~265px x 265px) so it acts as an elegant crest without overpowering the text.
    - Both are vertically centered, with generous spacing.
    """
    W = 1500
    H = 600
    banner = Image.new("RGBA", (W, H), (18, 22, 30, 255))
    draw = ImageDraw.Draw(banner)

    # Subtle halftone dot mesh
    for gx in range(0, W, 36):
        for gy in range(0, H, 36):
            draw.ellipse([gx - 1, gy - 1, gx + 1, gy + 1], fill=(35, 45, 60, 110))

    # Ambient radial backlight glow
    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    g_draw = ImageDraw.Draw(glow)
    g_draw.ellipse([W // 2 - 500, H // 2 - 250, W // 2 + 500, H // 2 + 250], fill=(0, 140, 255, 60))
    glow = glow.filter(ImageFilter.GaussianBlur(100))
    banner.paste(glow, (0, 0), glow)

    # 1. Book Icon ("漫")
    icon_raw = gen_icon.draw_icon_canvas(512, "漫")
    # Harmonious icon size: 265px
    icon_sz = 265
    icon_resized = icon_raw.resize((icon_sz, icon_sz), Image.Resampling.LANCZOS)

    # 2. Logo: Make it punchy and commanding (~325px height)
    logo_target_h = 325
    logo_scale = logo_target_h / logo_img.height
    logo_target_w = int(logo_img.width * logo_scale)

    # If total width exceeds canvas margins, scale slightly
    gap = 48
    total_w = icon_sz + gap + logo_target_w
    max_w = 1410
    if total_w > max_w:
        ratio = max_w / total_w
        logo_target_h = int(logo_target_h * ratio)
        icon_sz = int(icon_sz * ratio)
        icon_resized = icon_raw.resize((icon_sz, icon_sz), Image.Resampling.LANCZOS)
        logo_target_w = int(logo_img.width * (logo_target_h / logo_img.height))
        total_w = icon_sz + gap + logo_target_w

    logo_resized = logo_img.resize((logo_target_w, logo_target_h), Image.Resampling.LANCZOS)

    # Center horizontally and vertically
    start_x = (W - total_w) // 2
    icon_y = (H - icon_sz) // 2
    logo_y = (H - logo_target_h) // 2

    banner.paste(icon_resized, (start_x, icon_y), icon_resized)
    banner.paste(logo_resized, (start_x + icon_sz + gap, logo_y), logo_resized)

    banner.save(output_path, "PNG")
    print(f"Generated balanced 5:2 banner: {output_path}")

if __name__ == "__main__":
    print("Rendering A案 (Filled Counters)...")
    plan_a = render_logo_plan_a(open_counters=False)
    path_a = os.path.join(logo_dir, "logo_plan_a.png")
    plan_a.save(path_a, "PNG")
    print(f"Saved: {path_a}")

    print("Rendering A案その2 (Open Counters)...")
    plan_a2 = render_logo_plan_a(open_counters=True)
    path_a2 = os.path.join(logo_dir, "logo_plan_a2.png")
    plan_a2.save(path_a2, "PNG")
    print(f"Saved: {path_a2}")

    print("Creating A vs A2 comparison sheet...")
    comp_path = os.path.join(logo_dir, "logo_a_vs_a2_comparison.png")
    create_a_vs_a2_comparison_sheet(plan_a, plan_a2, comp_path)

    print("Generating 5:2 banner for A案...")
    banner_a_path = os.path.join(logo_dir, "banner_x_5to2_plan_a.png")
    generate_balanced_5to2_banner(plan_a, banner_a_path)

    print("Generating 5:2 banner for A案その2...")
    banner_a2_path = os.path.join(logo_dir, "banner_x_5to2_plan_a2.png")
    generate_balanced_5to2_banner(plan_a2, banner_a2_path)
