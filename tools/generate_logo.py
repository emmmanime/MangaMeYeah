import os
import math
import io
from PIL import Image, ImageDraw, ImageFont, ImageFilter

repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
font_path = os.path.join(repo_root, "tools", "fonts", "RocknRollOne-Regular.ttf")
logo_dir = os.path.join(repo_root, "docs", "logo")
os.makedirs(logo_dir, exist_ok=True)

# Import the icon generator for embedding the "漫" book icon
import importlib.util
spec = importlib.util.spec_from_file_location("gen_icon", os.path.join(repo_root, "tools", "generate_icon_c.py"))
gen_icon = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gen_icon)

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

def draw_styled_text(
    text: str,
    font_size: int,
    navy_stroke_ratio: float = 0.048,
    white_rim_ratio: float = 0.026,
    shadow_offset_ratio: float = 0.032,
    gradient_top=(0, 190, 255, 255),    # Vivid Cyan
    gradient_bot=(0, 110, 235, 255),    # Deep Electric Azure
    navy_color=(10, 35, 75, 255),       # Dark Navy Outline
    white_color=(255, 255, 255, 255),   # Crisp White Rim
    shadow_color=(15, 28, 52, 200),     # Soft Blue-Gray Shadow
    tilt_deg: float = 0.0
) -> Image.Image:
    """
    Renders styled text matching the MangaMeYeah icon aesthetic with 4x supersampling.
    """
    ss = 4
    font_px = font_size * ss
    font = ImageFont.truetype(font_path, font_px)

    # Calculate bounding box
    dummy = Image.new("RGBA", (10, 10))
    d_dummy = ImageDraw.Draw(dummy)
    bbox = d_dummy.textbbox((0, 0), text, font=font)
    
    pad = int(font_px * 0.35)
    tw = (bbox[2] - bbox[0]) + pad * 2
    th = (bbox[3] - bbox[1]) + pad * 2

    canvas = Image.new("RGBA", (tw, th), (0, 0, 0, 0))
    draw = ImageDraw.Draw(canvas)

    tx = pad - bbox[0]
    ty = pad - bbox[1]

    navy_stroke_w = max(2, int(font_px * navy_stroke_ratio))
    white_rim_w = max(1, int(font_px * white_rim_ratio))
    shadow_dist_x = max(2, int(font_px * shadow_offset_ratio))
    shadow_dist_y = max(2, int(font_px * shadow_offset_ratio * 1.1))

    # 1. Drop Shadow
    shadow_layer = Image.new("RGBA", (tw, th), (0, 0, 0, 0))
    s_draw = ImageDraw.Draw(shadow_layer)
    s_draw.text(
        (tx + shadow_dist_x, ty + shadow_dist_y),
        text,
        font=font,
        fill=shadow_color,
        stroke_width=navy_stroke_w + max(2, int(font_px * 0.015)),
        stroke_fill=shadow_color
    )
    # Blur shadow slightly
    blur_r = max(2, int(font_px * 0.016))
    shadow_layer = shadow_layer.filter(ImageFilter.GaussianBlur(blur_r))
    canvas.paste(shadow_layer, (0, 0), shadow_layer)

    # 2. Outer Navy Stroke
    draw.text(
        (tx, ty),
        text,
        font=font,
        fill=navy_color,
        stroke_width=navy_stroke_w,
        stroke_fill=navy_color
    )

    # 3. White Accent Rim
    draw.text(
        (tx, ty),
        text,
        font=font,
        fill=white_color,
        stroke_width=white_rim_w,
        stroke_fill=white_color
    )

    # 4. Gradient Core Fill
    # Render text body into an alpha mask
    text_mask = Image.new("L", (tw, th), 0)
    m_draw = ImageDraw.Draw(text_mask)
    m_draw.text((tx, ty), text, font=font, fill=255)

    grad = make_gradient(tw, th, gradient_top, gradient_bot)
    canvas.paste(grad, (0, 0), text_mask)

    # Downscale supersampled image
    target_w = tw // ss
    target_h = th // ss
    result = canvas.resize((target_w, target_h), Image.Resampling.LANCZOS)

    # Crop excess transparent borders
    bbox_crop = result.getbbox()
    if bbox_crop:
        # Add 6px margin
        mx = 6
        x0 = max(0, bbox_crop[0] - mx)
        y0 = max(0, bbox_crop[1] - mx)
        x1 = min(target_w, bbox_crop[2] + mx)
        y1 = min(target_h, bbox_crop[3] + mx)
        result = result.crop((x0, y0, x1, y1))

    if tilt_deg != 0.0:
        result = result.rotate(tilt_deg, resample=Image.Resampling.BICUBIC, expand=True)

    return result

def generate_logo_title_only(output_path: str):
    """Variant 1: Pure MangaMeYeah! title logo with icon styling."""
    logo = draw_styled_text("MangaMeYeah!", font_size=130)
    logo.save(output_path, "PNG")
    print(f"Generated: {output_path}")

def generate_logo_with_icon(output_path: str):
    """Variant 2: Book Icon ('漫') + MangaMeYeah! horizontal lockup."""
    text_img = draw_styled_text("MangaMeYeah!", font_size=120)
    
    # Generate 256px book icon
    icon_img = gen_icon.draw_icon_canvas(256, "漫")
    # Resize icon to match text height harmoniously
    icon_h = int(text_img.height * 1.12)
    icon_w = icon_h
    icon_resized = icon_img.resize((icon_w, icon_h), Image.Resampling.LANCZOS)

    spacing = 30
    total_w = icon_w + spacing + text_img.width + 40
    total_h = max(icon_h, text_img.height) + 30

    combined = Image.new("RGBA", (total_w, total_h), (0, 0, 0, 0))
    
    # Paste icon
    icon_y = (total_h - icon_h) // 2
    combined.paste(icon_resized, (20, icon_y), icon_resized)

    # Paste text
    text_y = (total_h - text_img.height) // 2
    combined.paste(text_img, (20 + icon_w + spacing, text_y), text_img)

    # Trim empty padding
    bbox = combined.getbbox()
    if bbox:
        combined = combined.crop((max(0, bbox[0] - 10), max(0, bbox[1] - 10), min(total_w, bbox[2] + 10), min(total_h, bbox[3] + 10)))

    combined.save(output_path, "PNG")
    print(f"Generated: {output_path}")

def generate_logo_with_subtitle(output_path: str):
    """Variant 3: Title + Katakana 'マンガミーヤー' & Subtitle."""
    main_text = draw_styled_text("MangaMeYeah!", font_size=110)
    
    # Japanese katakana sub badge
    sub_kana = draw_styled_text("マンガミーヤー", font_size=40, navy_stroke_ratio=0.038, white_rim_ratio=0.02)
    
    # English description
    sub_desc = "High-Speed Comic & Image Viewer for Windows"
    sub_font = ImageFont.truetype(font_path, 22)
    
    # Measure sub_desc
    dummy = Image.new("RGBA", (10, 10))
    d_dum = ImageDraw.Draw(dummy)
    sbbox = d_dum.textbbox((0, 0), sub_desc, font=sub_font)
    sw = sbbox[2] - sbbox[0]
    sh = sbbox[3] - sbbox[1]

    # Canvas
    total_w = max(main_text.width, sw + sub_kana.width + 40) + 60
    total_h = main_text.height + max(sub_kana.height, sh) + 40

    res = Image.new("RGBA", (total_w, total_h), (0, 0, 0, 0))
    draw = ImageDraw.Draw(res)

    # Main text centered
    mx = (total_w - main_text.width) // 2
    res.paste(main_text, (mx, 10), main_text)

    # Sub row centered
    sub_row_y = main_text.height + 15
    gap = 20
    row_total_w = sub_kana.width + gap + sw
    rx = (total_w - row_total_w) // 2

    res.paste(sub_kana, (rx, sub_row_y - 8), sub_kana)

    # Subtitle text with subtle dark fill & light glow for contrast
    desc_x = rx + sub_kana.width + gap
    desc_y = sub_row_y + (sub_kana.height - sh) // 2 - 2
    draw.text((desc_x, desc_y), sub_desc, font=sub_font, fill=(50, 70, 95, 230))

    bbox = res.getbbox()
    if bbox:
        res = res.crop((max(0, bbox[0] - 12), max(0, bbox[1] - 12), min(total_w, bbox[2] + 12), min(total_h, bbox[3] + 12)))

    res.save(output_path, "PNG")
    print(f"Generated: {output_path}")

def generate_social_banner(output_path: str):
    """Variant 4: 1280x640 Social Banner (for X/Twitter header or README hero)."""
    W = 1280
    H = 640
    banner = Image.new("RGBA", (W, H), (18, 22, 30, 255))
    draw = ImageDraw.Draw(banner)

    # Background subtle manga dot pattern / grid accent
    for gx in range(0, W, 40):
        for gy in range(0, H, 40):
            draw.ellipse([gx - 1, gy - 1, gx + 1, gy + 1], fill=(35, 45, 60, 120))

    # Center soft ambient glow
    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    g_draw = ImageDraw.Draw(glow)
    g_draw.ellipse([W // 2 - 380, H // 2 - 260, W // 2 + 380, H // 2 + 200], fill=(0, 130, 240, 45))
    glow = glow.filter(ImageFilter.GaussianBlur(80))
    banner.paste(glow, (0, 0), glow)

    # 1. Book Icon
    icon_img = gen_icon.draw_icon_canvas(256, "漫")
    icon_sz = 170
    icon_resized = icon_img.resize((icon_sz, icon_sz), Image.Resampling.LANCZOS)
    
    # 2. Main title text
    title_img = draw_styled_text("MangaMeYeah!", font_size=115)

    # Calculate center horizontal lockup
    spacing = 25
    header_w = icon_sz + spacing + title_img.width
    header_x = (W - header_w) // 2
    header_y = 120

    banner.paste(icon_resized, (header_x, header_y + 15), icon_resized)
    banner.paste(title_img, (header_x + icon_sz + spacing, header_y), title_img)

    # 3. Subtitle / Tagline
    tagline_font = ImageFont.truetype(font_path, 26)
    tagline = "伝説の超高速ビューワー MangaMeeya の魂を受け継ぐ、新世代コミックビューワー"
    tb = draw.textbbox((0, 0), tagline, font=tagline_font)
    tw = tb[2] - tb[0]
    draw.text(((W - tw) // 2, 350), tagline, font=tagline_font, fill=(230, 240, 255, 255))

    # 4. Feature Badges (Pills)
    badges = ["[高速先読み]", "[ネスト書庫直接展開]", "[X分割画像自動結合]", "[MangaMeYeah.ini]", "[Windows 11 / .NET 8]"]
    badge_font = ImageFont.truetype(font_path, 16)
    
    badge_w_list = []
    for b in badges:
        bb = draw.textbbox((0, 0), b, font=badge_font)
        badge_w_list.append((bb[2] - bb[0]) + 30)

    gap = 14
    total_badges_w = sum(badge_w_list) + gap * (len(badges) - 1)
    bx = (W - total_badges_w) // 2
    by = 425

    for idx, b in enumerate(badges):
        bw = badge_w_list[idx]
        bh = 38
        # Pill background
        draw.rounded_rectangle([bx, by, bx + bw, by + bh], radius=19, fill=(28, 40, 60, 220), outline=(0, 160, 255, 180), width=1)
        bb = draw.textbbox((0, 0), b, font=badge_font)
        tx = bx + (bw - (bb[2] - bb[0])) // 2
        ty = by + (bh - (bb[3] - bb[1])) // 2 - 2
        draw.text((tx, ty), b, font=badge_font, fill=(240, 248, 255, 255))
        bx += bw + gap

    # 5. Version & GitHub text
    ver_font = ImageFont.truetype(font_path, 16)
    vtext = "v0.3.5 Public Pre-Release  |  github.com/emmmanime/MangaMeYeah"
    vb = draw.textbbox((0, 0), vtext, font=ver_font)
    vw = vb[2] - vb[0]
    draw.text(((W - vw) // 2, 530), vtext, font=ver_font, fill=(130, 160, 195, 255))

    banner.save(output_path, "PNG")
    print(f"Generated: {output_path}")

def generate_preview_sheet(output_path: str):
    """Creates a comprehensive preview sheet showcasing all logo variants."""
    W = 1200
    H = 920
    sheet = Image.new("RGBA", (W, H), (24, 26, 32, 255))
    draw = ImageDraw.Draw(sheet)

    title_font = ImageFont.truetype(font_path, 24)
    heading_font = ImageFont.truetype(font_path, 17)
    body_font = ImageFont.truetype(font_path, 13)

    draw.text((40, 25), "MangaMeYeah! ロゴデザイン プレビュー（アイコン書体 RocknRoll One × 明るい青系）", font=title_font, fill=(255, 255, 255, 255))
    draw.text((40, 62), "・アイコンの「アール付きブック」＆「RocknRoll One 明るい青グラデーション＋濃紺＆白フチ＋影」を踏襲", font=body_font, fill=(150, 185, 220, 255))

    # Card 1: Pure Title on White (#FFFFFF)
    draw.rounded_rectangle([40, 100, W - 40, 270], radius=10, fill=(255, 255, 255, 255), outline=(70, 80, 95, 255), width=1)
    draw.text((55, 110), "1. タイトル単体ロゴ（白背景視認性チェック）", font=heading_font, fill=(20, 30, 45, 255))
    logo1 = Image.open(os.path.join(logo_dir, "logo_title_only.png"))
    l1_scaled = logo1.resize((int(logo1.width * 0.55), int(logo1.height * 0.55)), Image.Resampling.LANCZOS)
    l1_x = (W - l1_scaled.width) // 2
    l1_y = 100 + (170 - l1_scaled.height) // 2 + 10
    sheet.paste(l1_scaled, (l1_x, l1_y), l1_scaled)

    # Card 2: Combined with Icon on Dark Theme (#1E232D)
    draw.rounded_rectangle([40, 290, W - 40, 500], radius=10, fill=(30, 35, 45, 255), outline=(70, 80, 95, 255), width=1)
    draw.text((55, 300), "2. アイコン＋タイトル横並びロゴ（ダーク背景視認性チェック）", font=heading_font, fill=(240, 245, 255, 255))
    logo2 = Image.open(os.path.join(logo_dir, "logo_with_icon.png"))
    l2_scaled = logo2.resize((int(logo2.width * 0.55), int(logo2.height * 0.55)), Image.Resampling.LANCZOS)
    l2_x = (W - l2_scaled.width) // 2
    l2_y = 290 + (210 - l2_scaled.height) // 2 + 12
    sheet.paste(l2_scaled, (l2_x, l2_y), l2_scaled)

    # Card 3: With Subtitle on Soft Light Gray (#E8ECEF)
    draw.rounded_rectangle([40, 520, W - 40, 750], radius=10, fill=(232, 236, 240, 255), outline=(70, 80, 95, 255), width=1)
    draw.text((55, 530), "3. 日本語（マンガミーヤー）＆ サブタイトル付きロゴ", font=heading_font, fill=(20, 30, 45, 255))
    logo3 = Image.open(os.path.join(logo_dir, "logo_with_subtitle.png"))
    l3_scaled = logo3.resize((int(logo3.width * 0.60), int(logo3.height * 0.60)), Image.Resampling.LANCZOS)
    l3_x = (W - l3_scaled.width) // 2
    l3_y = 520 + (230 - l3_scaled.height) // 2 + 10
    sheet.paste(l3_scaled, (l3_x, l3_y), l3_scaled)

    # Footer note
    draw.text((40, 770), "📁 生成ファイル一覧:", font=heading_font, fill=(255, 255, 255, 255))
    files_info = [
        "・docs/logo/logo_title_only.png    : 「MangaMeYeah!」単体透過ロゴ（透過PNG / 幅広）",
        "・docs/logo/logo_with_icon.png     : 「漫」本アイコン＋タイトルの水平コンビネーション（Web・ヘッダー用）",
        "・docs/logo/logo_with_subtitle.png : カタカナ＆英文サブタイトル付きロゴ",
        "・docs/logo/banner_social.png      : 1280x640 X(Twitter) / GitHub OGP ヒーローバナー",
    ]
    for idx, ftext in enumerate(files_info):
        draw.text((55, 805 + idx * 24), ftext, font=body_font, fill=(180, 205, 230, 255))

    sheet.save(output_path, "PNG")
    print(f"Generated preview sheet: {output_path}")

if __name__ == "__main__":
    generate_logo_title_only(os.path.join(logo_dir, "logo_title_only.png"))
    generate_logo_with_icon(os.path.join(logo_dir, "logo_with_icon.png"))
    generate_logo_with_subtitle(os.path.join(logo_dir, "logo_with_subtitle.png"))
    generate_social_banner(os.path.join(logo_dir, "banner_social.png"))
    generate_preview_sheet(os.path.join(logo_dir, "logo_preview_sheet.png"))
