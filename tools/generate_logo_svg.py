import os
import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageChops

repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
font_path = os.path.join(repo_root, "tools", "fonts", "RocknRollOne-Regular.ttf")
logo_dir = os.path.join(repo_root, "docs", "logo")

import sys
sys.path.append(os.path.join(repo_root, "tools"))
import generate_logo_a_and_a2 as gla2

def mask_to_svg_path(mask_np, epsilon=0.45, offset_x=0, offset_y=0):
    """Converts a binary numpy mask into SVG path data with fill-rule='evenodd'."""
    contours, hierarchy = cv2.findContours(mask_np, cv2.RETR_TREE, cv2.CHAIN_APPROX_TC89_KCOS)
    if hierarchy is None:
        return ""

    path_data = []
    for cnt in contours:
        if len(cnt) < 3:
            continue
        approx = cv2.approxPolyDP(cnt, epsilon, True)
        pts = approx.reshape(-1, 2)
        d_str = "M " + " L ".join(f"{p[0] - offset_x:.1f},{p[1] - offset_y:.1f}" for p in pts) + " Z"
        path_data.append(d_str)

    return " ".join(path_data)

def generate_svg_logo(output_path: str):
    ss = 2
    base_sz = 140 * ss
    w_stroke = max(2, int(base_sz * 0.040)) # 11
    n_stroke = max(2, int(base_sz * 0.075)) # 21
    s_offset = max(2, int(base_sz * 0.045)) # 13
    margin = (w_stroke + n_stroke + s_offset) * 2 + 50

    char_layouts, baseline_y, total_w, total_h = gla2.compute_layout(base_sz, margin=margin)

    navy_mask = Image.new("L", (total_w, total_h), 0)
    d_navy = ImageDraw.Draw(navy_mask)
    white_mask = Image.new("L", (total_w, total_h), 0)
    d_white = ImageDraw.Draw(white_mask)
    punch_mask = Image.new("L", (total_w, total_h), 0)
    blue_mask = Image.new("L", (total_w, total_h), 0)
    d_blue = ImageDraw.Draw(blue_mask)

    # Extra white rim mask for letter 1 ('a') to cleanly overlay over letter 0 ('M')
    a_white_layer = Image.new("L", (total_w, total_h), 0)
    d_aw = ImageDraw.Draw(a_white_layer)

    for idx, item in enumerate(char_layouts):
        ch = item["char"]
        font = item["font"]
        cx = item["x"]
        d_navy.text((cx, baseline_y), ch, font=font, stroke_width=n_stroke, fill=255, anchor="ls")
        d_white.text((cx, baseline_y), ch, font=font, stroke_width=w_stroke, fill=255, anchor="ls")
        d_blue.text((cx, baseline_y), ch, font=font, fill=255, anchor="ls")

        if idx == 1:
            d_aw.text((cx, baseline_y), ch, font=font, stroke_width=w_stroke, fill=255, anchor="ls")

        if item["is_target"]:
            h_local, (ox, oy) = gla2.get_char_counter_holes(ch, font)
            r_erode = max(2, int(w_stroke * 0.95))
            k = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (r_erode * 2 + 1, r_erode * 2 + 1))
            h_eroded = cv2.erode(np.array(h_local), k)
            punch_mask.paste(Image.fromarray(h_eroded), (cx + ox, baseline_y + oy), Image.fromarray(h_eroded))

    white_mask = ImageChops.subtract(white_mask, punch_mask)
    navy_mask = ImageChops.subtract(navy_mask, punch_mask)
    blue_mask = ImageChops.subtract(blue_mask, punch_mask)
    a_white_layer = ImageChops.subtract(a_white_layer, punch_mask)

    # Calculate bounding box
    bbox = navy_mask.getbbox()
    pad = 24
    x0 = max(0, bbox[0] - pad)
    y0 = max(0, bbox[1] - pad)
    x1 = min(total_w, bbox[2] + pad + s_offset)
    y1 = min(total_h, bbox[3] + pad + s_offset)
    view_w = x1 - x0
    view_h = y1 - y0

    # Vectorize masks
    navy_path = mask_to_svg_path(np.array(navy_mask), offset_x=x0, offset_y=y0)
    white_path = mask_to_svg_path(np.array(white_mask), offset_x=x0, offset_y=y0)
    a_white_path = mask_to_svg_path(np.array(a_white_layer), offset_x=x0, offset_y=y0)
    blue_path = mask_to_svg_path(np.array(blue_mask), offset_x=x0, offset_y=y0)

    # Scale view box down by ss=2 for clean logical dimensions
    target_w = int(view_w / ss)
    target_h = int(view_h / ss)

    svg_content = f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {view_w} {view_h}" width="{target_w}" height="{target_h}">
  <defs>
    <!-- Soft Drop Shadow Filter -->
    <filter id="manga-drop-shadow" x="-10%" y="-10%" width="130%" height="130%">
      <feDropShadow dx="{s_offset}" dy="{int(s_offset * 1.15)}" stdDeviation="{int(s_offset * 0.45)}" flood-color="#0f1c34" flood-opacity="0.78" />
    </filter>
  </defs>

  <!-- 1. Dark Navy Outer Rim with Drop Shadow -->
  <path fill="#0a234b" fill-rule="evenodd" filter="url(#manga-drop-shadow)" d="{navy_path}" />

  <!-- 2. Crisp White Rim (Uniform 5.5px) -->
  <path fill="#ffffff" fill-rule="evenodd" d="{white_path}" />

  <!-- 3. Overlaid White Rim for letter 1 ('a') over 'M' -->
  <path fill="#ffffff" fill-rule="evenodd" d="{a_white_path}" />

  <!-- 4. Pure Vivid Blue Letters (#008CF5) -->
  <path fill="#008cf5" fill-rule="evenodd" d="{blue_path}" />
</svg>
'''
    with open(output_path, "w", encoding="utf-8") as f:
        f.write(svg_content)
    print(f"Generated Vector SVG Logo: {output_path}")

if __name__ == "__main__":
    out_svg = os.path.join(logo_dir, "logo_official.svg")
    generate_svg_logo(out_svg)
