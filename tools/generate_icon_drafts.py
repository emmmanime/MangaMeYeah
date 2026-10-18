import os
import struct
import io
import urllib.request
from PIL import Image, ImageDraw, ImageFont, ImageFilter

repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
fonts_dir = os.path.join(repo_root, "tools", "fonts")
assets_dir = os.path.join(repo_root, "src", "MangaMeYeah", "Assets")
os.makedirs(fonts_dir, exist_ok=True)
os.makedirs(assets_dir, exist_ok=True)

# 漫画・コミック向けとして著名なオープンソース・フリーフォント (SIL Open Font License 1.1)
FONT_SOURCES = [
    (
        "A. Reggae One (少年漫画・エッジ系)",
        "ReggaeOne-Regular.ttf",
        "https://raw.githubusercontent.com/google/fonts/main/ofl/reggaeone/ReggaeOne-Regular.ttf",
    ),
    (
        "B. Dela Gothic One (極太コミック見出し)",
        "DelaGothicOne-Regular.ttf",
        "https://raw.githubusercontent.com/google/fonts/main/ofl/delagothicone/DelaGothicOne-Regular.ttf",
    ),
    (
        "C. RocknRoll One (王道コミック・ポップ)",
        "RocknRollOne-Regular.ttf",
        "https://raw.githubusercontent.com/google/fonts/main/ofl/rocknrollone/RocknRollOne-Regular.ttf",
    ),
    (
        "D. Mochiy Pop One (吹き出し・丸ゴシック系)",
        "MochiyPopOne-Regular.ttf",
        "https://raw.githubusercontent.com/google/fonts/main/ofl/mochiypopone/MochiyPopOne-Regular.ttf",
    ),
    (
        "E. Yuji Syuku (本格毛筆・楷書明朝系)",
        "YujiSyuku-Regular.ttf",
        "https://raw.githubusercontent.com/google/fonts/main/ofl/yujisyuku/YujiSyuku-Regular.ttf",
    ),
    (
        "F. Potta One (筆致コミックロゴ系)",
        "PottaOne-Regular.ttf",
        "https://raw.githubusercontent.com/google/fonts/main/ofl/pottaone/PottaOne-Regular.ttf",
    ),
]


def ensure_fonts():
    loaded = []
    for label, filename, url in FONT_SOURCES:
        local_path = os.path.join(fonts_dir, filename)
        if not os.path.exists(local_path):
            try:
                print(f"Downloading {filename}...")
                req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
                with urllib.request.urlopen(req, timeout=15) as resp:
                    with open(local_path, "wb") as f:
                        f.write(resp.read())
            except Exception as ex:
                print(f"Failed to download {filename}: {ex}")
        if os.path.exists(local_path):
            loaded.append((label, local_path))
    return loaded


def draw_open_book_icon(size: int, font_path: str, char_text: str, theme="emerald") -> Image.Image:
    """
    常に4倍のスーパーサンプリング解像度で描画してから Lanczos 縮小することで
    16x16 / 24x24 でもジャギーのないシャープなアイコンを生成する
    """
    ss = 4 if size < 128 else 2
    W = size * ss
    H = size * ss
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    # カラーテーマ設定
    if theme == "emerald":
        cover_fill = (18, 120, 72, 255)      # MangaMeeya風の深いグリーン表紙
        cover_border = (8, 68, 38, 255)
        char_fill = (220, 38, 38, 255)       # 視認性の高い朱赤〜クリムゾン
        char_outline = (255, 255, 255, 255)  # 白フチ
        char_shadow = (25, 25, 25, 220)
    elif theme == "cobalt":
        cover_fill = (28, 78, 168, 255)
        cover_border = (14, 42, 96, 255)
        char_fill = (255, 196, 0, 255)
        char_outline = (20, 30, 50, 255)
        char_shadow = (10, 15, 25, 220)
    else:
        cover_fill = (155, 34, 38, 255)
        cover_border = (84, 16, 18, 255)
        char_fill = (30, 45, 120, 255)
        char_outline = (255, 255, 255, 255)
        char_shadow = (20, 20, 20, 220)

    pad = int(W * 0.02)
    top_y = int(H * 0.14)
    bot_y = int(H * 0.89)
    mid_x = W // 2
    spine_dip = int(H * 0.075)

    # 1. 外装カバー（開いた本の背表紙・ハードカバー）
    cover_margin = max(2, int(W * 0.035))
    cover_poly = [
        (pad, top_y + cover_margin),
        (mid_x, top_y + spine_dip + cover_margin),
        (W - pad, top_y + cover_margin),
        (W - pad, bot_y + cover_margin),
        (mid_x, bot_y + spine_dip + cover_margin),
        (pad, bot_y + cover_margin),
    ]
    border_w = max(2, int(W * 0.028))
    draw.polygon(cover_poly, fill=cover_fill, outline=cover_border, width=border_w)

    # 2. 開いた左右の白い紙面（少しクリーム色がかった漫画原稿・コミック紙）
    page_inset = max(3, int(W * 0.048))
    p_left = pad + page_inset
    p_right = W - pad - page_inset
    p_top = top_y - int(H * 0.01)
    p_bot = bot_y - int(H * 0.035)

    left_page = [
        (p_left, p_top),
        (mid_x, p_top + spine_dip),
        (mid_x, p_bot + spine_dip),
        (p_left, p_bot),
    ]
    right_page = [
        (mid_x, p_top + spine_dip),
        (p_right, p_top),
        (p_right, p_bot),
        (mid_x, p_bot + spine_dip),
    ]

    page_fill_l = (255, 253, 245, 255)
    page_fill_r = (245, 241, 228, 255)
    page_edge = (75, 75, 75, 255)
    page_line_w = max(2, int(W * 0.022))

    draw.polygon(left_page, fill=page_fill_l, outline=page_edge, width=page_line_w)
    draw.polygon(right_page, fill=page_fill_r, outline=page_edge, width=page_line_w)

    # 中央の綴じ目ライン
    draw.line(
        [(mid_x, p_top + spine_dip), (mid_x, p_bot + spine_dip)],
        fill=(120, 115, 105, 255),
        width=max(2, int(W * 0.02)),
    )

    # 高解像度時は紙面にさりげないコマ割り枠線を入れる
    if size >= 48:
        panel_color = (185, 180, 170, 255)
        pw = max(1, int(W * 0.012))
        # 左ページコマ枠
        lx1, lx2 = p_left + int(W * 0.05), mid_x - int(W * 0.04)
        ly1, ly2 = p_top + int(H * 0.10), p_bot - int(H * 0.07)
        draw.rectangle([lx1, ly1, lx2, (ly1 + ly2) // 2 - int(H * 0.02)], outline=panel_color, width=pw)
        draw.rectangle([lx1, (ly1 + ly2) // 2 + int(H * 0.02), lx2, ly2], outline=panel_color, width=pw)
        # 右ページコマ枠
        rx1, rx2 = mid_x + int(W * 0.04), p_right - int(W * 0.05)
        draw.rectangle([rx1, ly1, rx2, (ly1 + ly2) // 2 - int(H * 0.02)], outline=panel_color, width=pw)
        draw.rectangle([rx1, (ly1 + ly2) // 2 + int(H * 0.02), rx2, ly2], outline=panel_color, width=pw)

    # 3. 文字（「漫」または「マ」）を中央前面にバランス良く描画
    font_px = int(W * (0.56 if char_text == "漫" else 0.62))
    font = ImageFont.truetype(font_path, font_px)

    bbox = draw.textbbox((0, 0), char_text, font=font)
    tw = bbox[2] - bbox[0]
    th = bbox[3] - bbox[1]
    tx = (W - tw) // 2 - bbox[0]
    ty = (H - th) // 2 - bbox[1] - int(H * 0.01)

    stroke_outer = max(2, int(W * 0.036))
    shadow_offset = max(2, int(W * 0.022))

    # ドロップシャドウ
    draw.text(
        (tx + shadow_offset, ty + shadow_offset),
        char_text,
        font=font,
        fill=char_shadow,
        stroke_width=stroke_outer,
        stroke_fill=char_shadow,
    )
    # 白フチ＋メイン文字
    draw.text(
        (tx, ty),
        char_text,
        font=font,
        fill=char_fill,
        stroke_width=stroke_outer,
        stroke_fill=char_outline,
    )

    return img.resize((size, size), Image.Resampling.LANCZOS)


def save_multi_size_ico(font_path: str, ico_path: str, theme="emerald"):
    """
    高解像度 (256, 128, 64, 48, 32) は「漫」、
    低解像度 (24, 16) は視認性重視の「マ」を格納した正規 Windows .ico を生成する
    """
    sizes = [256, 128, 64, 48, 32, 24, 16]
    png_blobs = []

    for sz in sizes:
        ch = "漫" if sz >= 32 else "マ"
        im = draw_open_book_icon(sz, font_path, ch, theme=theme)
        buf = io.BytesIO()
        im.save(buf, format="PNG")
        png_blobs.append((sz, buf.getvalue()))

    # ICO バイナリ構造の組み立て (ICONDIR + ICONDIRENTRY[] + PNG payloads)
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
            w_byte,       # width (0 means 256)
            h_byte,       # height (0 means 256)
            0,            # color count
            0,            # reserved
            1,            # color planes
            32,           # bits per pixel
            len(blob),    # size of image data
            offset,       # offset of image data
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


def build_comparison_sheet(loaded_fonts, output_png_path: str):
    """
    選抜した6種類の漫画・コミック系フォントごとに
    [256px「漫」] [48px「漫」] [32px「漫」] [24px「マ」] [16px「マ」(実寸 & 4倍拡大)]
    を一覧比較できるドラフトシートを生成する
    """
    cols = 3
    rows = (len(loaded_fonts) + cols - 1) // cols
    card_w = 420
    card_h = 270
    sheet_w = card_w * cols + 40
    sheet_h = card_h * rows + 90

    sheet = Image.new("RGBA", (sheet_w, sheet_h), (30, 32, 36, 255))
    draw = ImageDraw.Draw(sheet)

    # Windows 標準のメイリオでラベル描画
    label_font_path = r"C:\Windows\Fonts\meiryob.ttc"
    if not os.path.exists(label_font_path):
        label_font_path = loaded_fonts[0][1]

    title_font = ImageFont.truetype(label_font_path, 22)
    card_title_font = ImageFont.truetype(label_font_path, 15)
    sub_font = ImageFont.truetype(label_font_path, 12)

    draw.text(
        (24, 18),
        "MangaMeYeah アイコン ドラフト比較 — 高解像度「漫」(256〜32px) ／ 低解像度「マ」(24・16px)",
        font=title_font,
        fill=(240, 240, 240, 255),
    )
    draw.text(
        (24, 50),
        "※すべて商用・オープンソース同梱可能なフリーフォント (SIL Open Font License 1.1) から選抜",
        font=sub_font,
        fill=(180, 210, 190, 255),
    )

    for idx, (label, fpath) in enumerate(loaded_fonts):
        r = idx // cols
        c = idx % cols
        x0 = 20 + c * card_w
        y0 = 80 + r * card_h

        # カード背景
        draw.rounded_rectangle(
            [x0 + 6, y0 + 6, x0 + card_w - 6, y0 + card_h - 6],
            radius=10,
            fill=(45, 48, 54, 255),
            outline=(85, 90, 100, 255),
            width=2,
        )

        draw.text((x0 + 18, y0 + 14), label, font=card_title_font, fill=(100, 255, 160, 255))

        # 160px プレビュー (高解像度「漫」)
        icon_hi = draw_open_book_icon(160, fpath, "漫", theme="emerald")
        sheet.alpha_composite(icon_hi, (x0 + 18, y0 + 48))
        draw.text((x0 + 55, y0 + 214), "高解像度「漫」", font=sub_font, fill=(210, 210, 210, 255))

        # 48px「漫」 & 32px「漫」
        icon_48 = draw_open_book_icon(48, fpath, "漫", theme="emerald")
        icon_32 = draw_open_book_icon(32, fpath, "漫", theme="emerald")
        sheet.alpha_composite(icon_48, (x0 + 205, y0 + 55))
        sheet.alpha_composite(icon_32, (x0 + 268, y0 + 71))
        draw.text((x0 + 205, y0 + 108), "48px / 32px「漫」", font=sub_font, fill=(200, 200, 200, 255))

        # 低解像度 24px「マ」 & 16px「マ」 (実寸 + ピクセル確認用3倍拡大)
        icon_24 = draw_open_book_icon(24, fpath, "マ", theme="emerald")
        icon_16 = draw_open_book_icon(16, fpath, "マ", theme="emerald")
        icon_16_zoom = icon_16.resize((64, 64), Image.Resampling.NEAREST)
        icon_24_zoom = icon_24.resize((64, 64), Image.Resampling.NEAREST)

        sheet.alpha_composite(icon_24, (x0 + 205, y0 + 148))
        sheet.alpha_composite(icon_16, (x0 + 240, y0 + 156))
        sheet.alpha_composite(icon_24_zoom, (x0 + 272, y0 + 138))
        sheet.alpha_composite(icon_16_zoom, (x0 + 344, y0 + 138))

        draw.text((x0 + 202, y0 + 214), "低解像度 24/16px「マ」(右は拡大)", font=sub_font, fill=(255, 220, 140, 255))

    sheet.save(output_png_path, format="PNG")
    print(f"Saved comparison sheet to {output_png_path}")


if __name__ == "__main__":
    fonts = ensure_fonts()
    if fonts:
        comp_path = os.path.join(repo_root, "docs", "icon_drafts_comparison.png")
        build_comparison_sheet(fonts, comp_path)

        # デフォルトの第1案 (C. RocknRoll One または先頭フォント) でマルチサイズ .ico を生成
        default_font = next((p for lbl, p in fonts if "RocknRoll" in lbl or "Reggae" in lbl), fonts[0][1])
        ico_out = os.path.join(assets_dir, "MangaMeYeah.ico")
        save_multi_size_ico(default_font, ico_out, theme="emerald")
        print(f"Generated multi-resolution icon: {ico_out}")
