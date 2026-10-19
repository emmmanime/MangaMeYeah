import os
import math
from PIL import Image, ImageDraw, ImageFont

repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
out_dir = os.path.join(repo_root, "test_images", "2x2")
os.makedirs(out_dir, exist_ok=True)

# 1600x1600 composite (each quadrant 800x800)
W, H = 1600, 1600
half_w, half_h = 800, 800

# Base canvas with colorful comic background
img = Image.new("RGB", (W, H), (245, 245, 250))
draw = ImageDraw.Draw(img)

# Diagonal gradient background
for y in range(H):
    r = int(230 - 60 * (y / H))
    g = int(240 - 40 * (y / H))
    b = int(255 - 20 * (y / H))
    draw.line([(0, y), (W, y)], fill=(r, g, b))

# Font
label_font_path = r"C:\Windows\Fonts\meiryob.ttc"
if not os.path.exists(label_font_path):
    label_font_path = os.path.join(repo_root, "tools", "fonts", "RocknRollOne-Regular.ttf")

huge_font = ImageFont.truetype(label_font_path, 48)
title_font = ImageFont.truetype(label_font_path, 36)
quad_font = ImageFont.truetype(label_font_path, 28)
sub_font = ImageFont.truetype(label_font_path, 20)

# Radiating speedlines from exact center (800, 800) crossing all seams
cx, cy = 800, 800
num_rays = 48
for i in range(num_rays):
    angle = (i / num_rays) * 2 * math.pi
    ex = cx + int(1150 * math.cos(angle))
    ey = cy + int(1150 * math.sin(angle))
    col = (180, 205, 235) if i % 2 == 0 else (205, 225, 248)
    draw.line([(cx, cy), (ex, ey)], fill=col, width=3)

# Concentric circles crossing the vertical and horizontal center seams
circles = [
    (140, (0, 140, 245), 8),
    (260, (255, 120, 0), 10),
    (420, (30, 60, 120), 12),
    (580, (0, 180, 120), 10),
    (720, (180, 50, 140), 12),
]
for radius, color, lw in circles:
    draw.ellipse([cx - radius, cy - radius, cx + radius, cy + radius], outline=color, width=lw)

# Prominent diagonal cross ribbons spanning across center
ribbon_color = (20, 35, 65, 230)
draw.polygon([(0, 680), (1600, 860), (1600, 920), (0, 740)], fill=(255, 215, 0))
draw.polygon([(0, 700), (1600, 880), (1600, 900), (0, 720)], fill=(20, 40, 80))

# Center emblem
draw.ellipse([cx - 100, cy - 100, cx + 100, cy + 100], fill=(255, 255, 255), outline=(20, 40, 80), width=6)
draw.text((cx - 72, cy - 30), "2×2", font=huge_font, fill=(0, 140, 245))

# Quadrant decorative manga frames
def draw_quad_decorations():
    # TL
    draw.rectangle([60, 60, 740, 740], outline=(0, 140, 245), width=6)
    draw.text((80, 80), "【左上 Top-Left】", font=quad_font, fill=(0, 90, 180))
    # TR
    draw.rectangle([860, 60, 1540, 740], outline=(255, 120, 0), width=6)
    draw.text((880, 80), "【右上 Top-Right】", font=quad_font, fill=(200, 80, 0))
    # BL
    draw.rectangle([60, 860, 740, 1540], outline=(0, 180, 120), width=6)
    draw.text((80, 880), "【左下 Bottom-Left】", font=quad_font, fill=(0, 130, 80))
    # BR
    draw.rectangle([860, 860, 1540, 1540], outline=(180, 50, 140), width=6)
    draw.text((880, 880), "【右下 Bottom-Right】", font=quad_font, fill=(130, 30, 100))

draw_quad_decorations()

# Horizontal connecting banner text across left-right seam
draw.text((450, 760), "★ 縦横連結画像チェック用テスト原稿 ★", font=title_font, fill=(255, 255, 255))
draw.text((450, 762), "★ 縦横連結画像チェック用テスト原稿 ★", font=title_font, fill=(10, 30, 60))

# Crop into 4 quadrants
tile_TL = img.crop((0, 0, half_w, half_h))
tile_TR = img.crop((half_w, 0, W, half_h))
tile_BL = img.crop((0, half_h, half_w, H))
tile_BR = img.crop((half_w, half_h, W, H))

def stamp_badge(tile: Image.Image, text1: str, text2: str, bg_color) -> Image.Image:
    res = tile.copy()
    d = ImageDraw.Draw(res)
    # Badge box at bottom center of each tile
    bw, bh = 680, 80
    bx = (half_w - bw) // 2
    by = half_h - bh - 20
    d.rounded_rectangle([bx, by, bx + bw, by + bh], radius=10, fill=bg_color, outline=(255, 255, 255), width=2)
    d.text((bx + 16, by + 10), text1, font=sub_font, fill=(255, 255, 255))
    d.text((bx + 16, by + 42), text2, font=sub_font, fill=(255, 240, 200))
    return res

# 1. 正順セット (seijun_縦横2×2枚チェック用画像_)
# Xデフォルト: 1=TL(左上), 2=TR(右上), 3=BL(左下), 4=BR(右下)
seijun_files = [
    ("seijun_縦横2×2枚チェック用画像_1.jpg", stamp_badge(tile_TL, "ファイル: 1番 [正順]", "Xの配置位置: 【左上 Top-Left】", (0, 100, 200))),
    ("seijun_縦横2×2枚チェック用画像_2.jpg", stamp_badge(tile_TR, "ファイル: 2番 [正順]", "Xの配置位置: 【右上 Top-Right】", (220, 100, 0))),
    ("seijun_縦横2×2枚チェック用画像_3.jpg", stamp_badge(tile_BL, "ファイル: 3番 [正順]", "Xの配置位置: 【左下 Bottom-Left】", (0, 140, 90))),
    ("seijun_縦横2×2枚チェック用画像_4.jpg", stamp_badge(tile_BR, "ファイル: 4番 [正順]", "Xの配置位置: 【右下 Bottom-Right】", (150, 40, 110))),
]

for fn, t_img in seijun_files:
    fp = os.path.join(out_dir, fn)
    t_img.save(fp, format="JPEG", quality=95)
    print(f"Created: {fp}")

# 2. バラバラセット (barabara_縦横2×2枚チェック用画像_)
# トリック投稿等で連番と実際のXグリッド配置がシャッフルされた状態
# 例: 1番=左下(BL), 2番=右上(TR), 3番=左上(TL), 4番=右下(BR)
barabara_files = [
    ("barabara_縦横2×2枚チェック用画像_1.jpg", stamp_badge(tile_BL, "ファイル: 1番 [バラバラ・トリック投稿]", "本来の正しい位置: 【左下 Bottom-Left】 (シャッフル配置)", (0, 140, 90))),
    ("barabara_縦横2×2枚チェック用画像_2.jpg", stamp_badge(tile_TR, "ファイル: 2番 [バラバラ・トリック投稿]", "本来の正しい位置: 【右上 Top-Right】 (シャッフル配置)", (220, 100, 0))),
    ("barabara_縦横2×2枚チェック用画像_3.jpg", stamp_badge(tile_TL, "ファイル: 3番 [バラバラ・トリック投稿]", "本来の正しい位置: 【左上 Top-Left】 (シャッフル配置)", (0, 100, 200))),
    ("barabara_縦横2×2枚チェック用画像_4.jpg", stamp_badge(tile_BR, "ファイル: 4番 [バラバラ・トリック投稿]", "本来の正しい位置: 【右下 Bottom-Right】 (シャッフル配置)", (150, 40, 110))),
]

for fn, t_img in barabara_files:
    fp = os.path.join(out_dir, fn)
    t_img.save(fp, format="JPEG", quality=95)
    print(f"Created: {fp}")

# Save full composite preview for documentation
comp_preview = os.path.join(out_dir, "00_composite_preview.jpg")
img.save(comp_preview, format="JPEG", quality=95)
print(f"Created composite preview: {comp_preview}")
