import os
import math
import struct
import io
from PIL import Image, ImageDraw, ImageFont, ImageFilter

repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
font_path = os.path.join(repo_root, "tools", "fonts", "RocknRollOne-Regular.ttf")
assets_dir = os.path.join(repo_root, "src", "MangaMeYeah", "Assets")
docs_dir = os.path.join(repo_root, "docs")
ico_target = os.path.join(assets_dir, "MangaMeYeah.ico")
preview_target = os.path.join(docs_dir, "icon_c_preview.png")

def make_curved_polygon(top_curve, bottom_curve, left_x, right_x, steps=30):
    """
    top_curve(t), bottom_curve(t) return (x, y) for t in [0.0, 1.0]
    Returns list of (x, y) forming a closed shape from left to right along top, then right to left along bottom.
    """
    pts = []
    # top: left to right
    for i in range(steps + 1):
        t = i / steps
        pts.append(top_curve(t))
    # bottom: right to left
    for i in range(steps, -1, -1):
        t = i / steps
        pts.append(bottom_curve(t))
    return pts

def draw_icon_canvas(size: int, char_text: str) -> Image.Image:
    # Use 4x supersampling for ultra smooth vector-like rendering
    ss = 4 if size <= 64 else (3 if size <= 128 else 2)
    W = size * ss
    H = size * ss
    
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    # Parametric layout geometry: expanded to fill the icon bounds (matching MangaMeeya taskbar presence)
    mid_x = W * 0.50
    left_x = W * 0.025
    right_x = W * 0.975
    
    # Arched book dimensions (page arches rise close to top and bottom bounds)
    top_apex_y = H * 0.02      # peak of the page arch
    top_shoulder_y = H * 0.09  # outer edge shoulder
    top_spine_y = H * 0.12     # center spine dip
    
    bot_shoulder_y = H * 0.88  # outer bottom corner
    bot_apex_y = H * 0.83      # arch peak at bottom
    bot_spine_y = H * 0.90     # center spine bottom dip

    # 1. Blue-gray drop shadow (本の影はブルーグレー)
    shadow_img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    s_draw = ImageDraw.Draw(shadow_img)
    shadow_color = (60, 85, 115, 180) # ブルーグレー
    
    # Shadow ellipse under the base of the book
    shadow_box = [
        int(W * 0.02),
        int(H * 0.85),
        int(W * 0.98),
        int(H * 0.99)
    ]
    s_draw.ellipse(shadow_box, fill=shadow_color)
    # Blur shadow slightly
    blur_r = max(2, int(W * 0.02))
    shadow_img = shadow_img.filter(ImageFilter.GaussianBlur(blur_r))
    img.paste(shadow_img, (0, 0), shadow_img)

    # 2. Hardcover backing / book spine base (Deep navy/slate trim)
    # The back cover peeking out slightly beneath and around the pages
    cover_offset = max(2, int(W * 0.024))
    cover_color = (32, 58, 90, 255)
    cover_outline = (18, 36, 60, 255)
    cov_line_w = max(2, int(W * 0.018))

    def left_page_top(t, offset_y=0):
        # t from 0 (left edge) to 1 (spine)
        x = left_x + t * (mid_x - left_x)
        # Parabolic arc that rises to top_apex_y and drops to spine
        # At t=0: top_shoulder_y, at t=0.45: top_apex_y, at t=1: top_spine_y
        arch = -math.sin(t * math.pi) * (top_shoulder_y - top_apex_y) * 1.5
        y = top_shoulder_y + (top_spine_y - top_shoulder_y) * (t ** 1.3) + arch + offset_y
        return (x, y)

    def right_page_top(t, offset_y=0):
        # t from 0 (spine) to 1 (right edge)
        x = mid_x + t * (right_x - mid_x)
        arch = -math.sin((1 - t) * math.pi) * (top_shoulder_y - top_apex_y) * 1.5
        y = top_spine_y + (top_shoulder_y - top_spine_y) * (t ** 0.77) + arch + offset_y
        return (x, y)

    def left_page_bot(t, offset_y=0):
        x = left_x + t * (mid_x - left_x)
        arch = -math.sin(t * math.pi) * (bot_shoulder_y - bot_apex_y) * 1.4
        y = bot_shoulder_y + (bot_spine_y - bot_shoulder_y) * (t ** 1.3) + arch + offset_y
        return (x, y)

    def right_page_bot(t, offset_y=0):
        x = mid_x + t * (right_x - mid_x)
        arch = -math.sin((1 - t) * math.pi) * (bot_shoulder_y - bot_apex_y) * 1.4
        y = bot_spine_y + (bot_shoulder_y - bot_spine_y) * (t ** 0.77) + arch + offset_y
        return (x, y)

    # Draw cover bottom edge peeking out
    cov_bot_pts = []
    for i in range(25):
        t = i / 24
        cov_bot_pts.append(left_page_bot(t, cover_offset))
    for i in range(25):
        t = i / 24
        cov_bot_pts.append(right_page_bot(t, cover_offset))
    # Close along spine and sides
    cov_bot_pts.append((right_x + cover_offset * 0.5, bot_shoulder_y))
    cov_bot_pts.append((left_x - cover_offset * 0.5, bot_shoulder_y))
    draw.polygon(cov_bot_pts, fill=cover_color, outline=cover_outline, width=cov_line_w)

    # 3. Left Page (White/Cream paper with arch)
    left_poly = make_curved_polygon(
        top_curve=lambda t: left_page_top(t),
        bottom_curve=lambda t: left_page_bot(t),
        left_x=left_x,
        right_x=mid_x,
        steps=32
    )
    # Right Page
    right_poly = make_curved_polygon(
        top_curve=lambda t: right_page_top(t),
        bottom_curve=lambda t: right_page_bot(t),
        left_x=mid_x,
        right_x=right_x,
        steps=32
    )

    page_edge_color = (40, 52, 68, 255)
    page_line_w = max(2, int(W * 0.022))

    # Fill left page (slightly warm comic paper)
    draw.polygon(left_poly, fill=(255, 254, 250, 255), outline=page_edge_color, width=page_line_w)
    # Fill right page (slight shadow depth near spine)
    draw.polygon(right_poly, fill=(244, 245, 248, 255), outline=page_edge_color, width=page_line_w)

    # Center book spine crease / shadow
    spine_w = max(2, int(W * 0.02))
    draw.line([left_page_top(1.0), left_page_bot(1.0)], fill=(120, 140, 165, 220), width=spine_w)

    # 4. 小回り (コマ割り) — manga panel dividers with curvature (アール) matching the page!
    # Draw comic panel frames inside each page that curve along with the top & bottom arcs
    if size >= 32:
        panel_stroke_color = (165, 185, 205, 255) # Clear bluish-gray panel lines
        panel_line_w = max(1, int(W * 0.012))

        # Helper to draw a curved panel row
        def draw_curved_panel(page_side, t_start, t_end, row_top_ratio, row_bot_ratio):
            # Interpolate between page top curve and page bot curve
            pts = []
            steps = 16
            # top edge of panel
            for i in range(steps + 1):
                t = t_start + (t_end - t_start) * (i / steps)
                pt_top = left_page_top(t) if page_side == "L" else right_page_top(t)
                pt_bot = left_page_bot(t) if page_side == "L" else right_page_bot(t)
                px = pt_top[0]
                py = pt_top[1] + (pt_bot[1] - pt_top[1]) * row_top_ratio
                pts.append((px, py))
            # bot edge of panel (reversed)
            for i in range(steps, -1, -1):
                t = t_start + (t_end - t_start) * (i / steps)
                pt_top = left_page_top(t) if page_side == "L" else right_page_top(t)
                pt_bot = left_page_bot(t) if page_side == "L" else right_page_bot(t)
                px = pt_top[0]
                py = pt_top[1] + (pt_bot[1] - pt_top[1]) * row_bot_ratio
                pts.append((px, py))
            draw.polygon(pts, fill=None, outline=panel_stroke_color, width=panel_line_w)

        # Left Page panels: Top panel and Bottom panel (both following the curve)
        draw_curved_panel("L", t_start=0.15, t_end=0.85, row_top_ratio=0.14, row_bot_ratio=0.48)
        draw_curved_panel("L", t_start=0.15, t_end=0.85, row_top_ratio=0.55, row_bot_ratio=0.88)

        # Right Page panels: Top 2-split panels and Bottom large panel
        draw_curved_panel("R", t_start=0.15, t_end=0.48, row_top_ratio=0.14, row_bot_ratio=0.48)
        draw_curved_panel("R", t_start=0.54, t_end=0.85, row_top_ratio=0.14, row_bot_ratio=0.48)
        draw_curved_panel("R", t_start=0.15, t_end=0.85, row_top_ratio=0.55, row_bot_ratio=0.88)

    # 5. Main Character ("漫" or "マ") using C案 (RocknRoll One)
    # User requirements:
    # - フォント: C案 (RocknRoll One)
    # - 文字の色: 明るめの青系 (Electric Azure / Sky Blue, e.g. #0099FF / #0084FF)
    # - 白背景でも目立つ様に調整: Strong contrast with dark navy outline + drop shadow + crisp white inner/outer highlight
    font_scale = 0.70 if char_text == "漫" else 0.76
    font_px = int(W * font_scale)
    font = ImageFont.truetype(font_path, font_px)

    bbox = draw.textbbox((0, 0), char_text, font=font)
    tw = bbox[2] - bbox[0]
    th = bbox[3] - bbox[1]
    tx = (W - tw) // 2 - bbox[0]
    ty = (H - th) // 2 - bbox[1] - int(H * 0.01)

    # Colors for character
    # Bright cyan/sky blue: vivid and energetic
    char_fill = (0, 140, 245, 255)         # 明るい青系 (Vivid Bright Azure)
    char_navy_stroke = (10, 35, 75, 255)   # 白背景でも文字輪郭を極めて鮮明にする濃紺アウトライン
    char_white_rim = (255, 255, 255, 255)  # 白フチ（視認性とポップ感を両立）
    char_shadow = (20, 35, 60, 220)        # ドロップシャドウ

    shadow_dist = max(2, int(W * 0.024))
    navy_stroke_w = max(2, int(W * 0.046))
    white_rim_w = max(1, int(W * 0.026))

    # A. Character Drop shadow
    draw.text(
        (tx + shadow_dist, ty + shadow_dist),
        char_text,
        font=font,
        fill=char_shadow,
        stroke_width=navy_stroke_w,
        stroke_fill=char_shadow,
    )

    # B. Outer dark navy stroke (ensures bold outline against white backgrounds!)
    draw.text(
        (tx, ty),
        char_text,
        font=font,
        fill=char_navy_stroke,
        stroke_width=navy_stroke_w,
        stroke_fill=char_navy_stroke,
    )

    # C. White accent rim
    draw.text(
        (tx, ty),
        char_text,
        font=font,
        fill=char_fill,
        stroke_width=white_rim_w,
        stroke_fill=char_white_rim,
    )

    # D. Final bright blue character body
    draw.text(
        (tx, ty),
        char_text,
        font=font,
        fill=char_fill,
    )

    return img.resize((size, size), Image.Resampling.LANCZOS)

def generate_multi_size_ico(ico_path: str):
    sizes = [256, 128, 64, 48, 32, 24, 16]
    png_blobs = []

    for sz in sizes:
        ch = "漫" if sz >= 32 else "マ"
        im = draw_icon_canvas(sz, ch)
        buf = io.BytesIO()
        im.save(buf, format="PNG")
        png_blobs.append((sz, buf.getvalue()))

    num_images = len(png_blobs)
    header = struct.pack("<HHH", 0, 1, num_images)
    offset = 6 + 16 * num_images
    dir_entries = []
    payloads = []

    for sz, blob in png_blobs:
        w_byte = 0 if sz >= 256 else sz
        h_byte = 0 if sz >= 256 else sz
        entry = struct.pack(
            "<BBBBHHII",
            w_byte,
            h_byte,
            0,
            0,
            1,
            32,
            len(blob),
            offset,
        )
        dir_entries.append(entry)
        payloads.append(blob)
        offset += len(blob)

    with open(ico_path, "wb") as f:
        f.write(header)
        for e in dir_entries:
            f.write(e)
        for p in payloads:
            f.write(p)
    print(f"Generated multi-res ICO: {ico_path}")

def generate_preview_comparison(output_png: str):
    # Preview sheet showing icon on:
    # 1. Pure White (#FFFFFF) background
    # 2. Windows 11 Dark Mode Taskbar (#202020) background
    # 3. Soft Gray (#E5E7EB) background
    # Across sizes: 256px, 48px, 32px, 24px, 16px (and 16px magnified 4x)
    W = 1000
    H = 680
    sheet = Image.new("RGBA", (W, H), (24, 26, 32, 255))
    draw = ImageDraw.Draw(sheet)

    label_font_path = r"C:\Windows\Fonts\meiryob.ttc"
    if not os.path.exists(label_font_path):
        label_font_path = font_path

    title_font = ImageFont.truetype(label_font_path, 22)
    heading_font = ImageFont.truetype(label_font_path, 15)
    body_font = ImageFont.truetype(label_font_path, 12)

    draw.text((30, 20), "MangaMeYeah アイコン新デザイン（C案 RocknRoll One × アール付きブック × 明るい青）", font=title_font, fill=(245, 245, 250, 255))
    draw.text((30, 52), "・MangaMeeya風のページアール（曲面） ＋ アール追従コマ割り ＋ ブルーグレー影 ＋ 白背景対応の濃紺エッジ付き明るい青", font=body_font, fill=(160, 190, 220, 255))

    # Pre-render icons
    im256 = draw_icon_canvas(256, "漫")
    im48 = draw_icon_canvas(48, "漫")
    im32 = draw_icon_canvas(32, "漫")
    im24 = draw_icon_canvas(24, "マ")
    im16 = draw_icon_canvas(16, "マ")
    im16_mag = im16.resize((64, 64), Image.Resampling.NEAREST)

    # 3 Background Test Strips
    bg_tests = [
        ("白背景での視認性 (White #FFFFFF)", (255, 255, 255, 255), (20, 25, 35, 255)),
        ("Win11ダークタスクバー (Dark #202020)", (32, 32, 32, 255), (240, 240, 245, 255)),
        ("ライトグレーデスクトップ (#E2E8F0)", (226, 232, 240, 255), (30, 40, 55, 255)),
    ]

    strip_h = 170
    start_y = 90

    for idx, (label, bg_col, text_col) in enumerate(bg_tests):
        y0 = start_y + idx * (strip_h + 15)
        # Card background
        draw.rounded_rectangle([30, y0, W - 30, y0 + strip_h], radius=8, fill=bg_col, outline=(80, 90, 110, 255), width=1)
        draw.text((45, y0 + 10), label, font=heading_font, fill=text_col)

        # Place 256 scaled down to 128 for card fit
        sheet.paste(im256.resize((128, 128), Image.Resampling.LANCZOS), (50, y0 + 35), im256.resize((128, 128), Image.Resampling.LANCZOS))
        draw.text((75, y0 + 148), "256px (縮小)", font=body_font, fill=text_col)

        # 48px
        sheet.paste(im48, (230, y0 + 75), im48)
        draw.text((236, y0 + 130), "48px", font=body_font, fill=text_col)

        # 32px
        sheet.paste(im32, (340, y0 + 83), im32)
        draw.text((342, y0 + 130), "32px", font=body_font, fill=text_col)

        # 24px (マ)
        sheet.paste(im24, (440, y0 + 87), im24)
        draw.text((440, y0 + 130), "24px「マ」", font=body_font, fill=text_col)

        # 16px (マ)
        sheet.paste(im16, (550, y0 + 91), im16)
        draw.text((545, y0 + 130), "16px「マ」", font=body_font, fill=text_col)

        # 16px 4x Magnified
        sheet.paste(im16_mag, (660, y0 + 59), im16_mag)
        draw.text((655, y0 + 130), "16px (4倍拡大)", font=body_font, fill=text_col)

    sheet.save(output_png)
    print(f"Generated preview: {output_png}")

if __name__ == "__main__":
    generate_multi_size_ico(ico_target)
    generate_preview_comparison(preview_target)
