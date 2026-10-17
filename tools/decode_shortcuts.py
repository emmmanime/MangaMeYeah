import os
import re

folder = r"C:\win32\MangaMeeya_74b"
ini_path = os.path.join(folder, "MangaMeeya.ini")

with open(ini_path, "r", encoding="cp932", errors="replace") as f:
    ini_text = f.read()

# Load command names from コマンド番号.txt
cmd_names = {}
for fn in os.listdir(folder):
    if "コマンド" in fn and fn.endswith(".txt"):
        with open(os.path.join(folder, fn), "r", encoding="cp932", errors="replace") as f:
            for line in f:
                line = line.strip()
                m = re.match(r"^(\d+)\s+(.+)$", line)
                if m:
                    cmd_names[int(m.group(1))] = m.group(2).strip()
                m2 = re.match(r"^(\d+)-(\d+)\s+(.+)$", line)
                if m2:
                    s, e, label = int(m2.group(1)), int(m2.group(2)), m2.group(3).strip()
                    for idx, cid in enumerate(range(s, e + 1), 1):
                        cmd_names[cid] = f"{label} ({idx})"

vk_names = {
    8: "BackSpace", 9: "Tab", 13: "Enter", 19: "Pause", 27: "Esc",
    32: "Space", 33: "PageUp", 34: "PageDown", 35: "End", 36: "Home",
    37: "←", 38: "↑", 39: "→", 40: "↓",
    45: "Insert", 46: "Delete",
    96: "Num 0", 97: "Num 1", 98: "Num 2", 99: "Num 3", 100: "Num 4",
    101: "Num 5", 102: "Num 6", 103: "Num 7", 104: "Num 8", 105: "Num 9",
    106: "Num *", 107: "Num +", 109: "Num -", 110: "Num .", 111: "Num /",
    186: ":", 187: ";", 188: ",", 189: "-", 190: ".", 191: "/", 192: "@",
    219: "[", 220: "\\", 221: "]", 222: "^", 226: "\\ (アンダースコア)"
}
for i in range(48, 58):
    vk_names[i] = chr(i)
for i in range(65, 91):
    vk_names[i] = chr(i)
for i in range(112, 124):
    vk_names[i] = f"F{i - 111}"

def decode_key(val):
    if val == 0:
        return None
    vk = val & 0xFF
    mods = []
    if val & 0x0200:
        mods.append("Ctrl")
    if val & 0x0400:
        mods.append("Alt")
    if val & 0x0100:
        mods.append("Shift")
    kname = vk_names.get(vk, f"VK_{vk}")
    return " + ".join(mods + [kname])

print("=== Decoded Keyboard Shortcuts ===")
for line in ini_text.splitlines():
    m = re.match(r"^CMD(\d+)=(\d+)$", line.strip())
    if m:
        cid, val = int(m.group(1)), int(m.group(2))
        if val != 0:
            kstr = decode_key(val)
            cname = cmd_names.get(cid, f"Command {cid}")
            print(f"CMD{cid:3d} ({cname}): {kstr}")

# Decode MouseKeyConfig
button_groups = {
    1: "左ボタン (第1ボタン)",
    2: "右ボタン (第2ボタン)",
    3: "ホイール↑",
    4: "ホイール↓",
    5: "中ボタン (第3ボタン)",
    6: "第4ボタン (XButton1)",
    7: "第5ボタン (XButton2)",
}

# For buttons 1, 2, 5, 6, 7:
# sub 1=クリック, 2=ダブルクリック, 3=ドラッグ, 4=長押し, 5=+他ボタン1, 6=+他ボタン2, 7=+他ボタン3, 8=+他ボタン4
# For wheel 3, 4:
# sub 1=単体, 2=+第1(左), 3=+第2(右), 4=+第4(Shift), 5=+第5(Ctrl)
special_mouse_cmds = {
    512: "ドラッグスクロール(1)",
    513: "ドラッグスクロール(2)",
    514: "指定範囲ズーム",
    515: "マウスジェスチャ / ポップアップメニュー",
    516: "ルーペ移動",
    773: "コンテキストメニュー表示"
}

print("\n=== Decoded Mouse Bindings ===")
for line in ini_text.splitlines():
    m = re.match(r"^Button_(\d+)_(\d+)=(\d+)$", line.strip())
    if m:
        b, sub, val = int(m.group(1)), int(m.group(2)), int(m.group(3))
        if val != 0:
            cname = cmd_names.get(val, special_mouse_cmds.get(val, f"Special({val})"))
            print(f"Button_{b}_{sub} ({button_groups.get(b, b)} #{sub}): {cname} (val={val})")

# Decode Gestures:
# Let's see how Gesture_N=val is encoded:
# Wait: Gesture_1=200727 -> hex(200727) = 0x31017 -> low 16 bits or 12 bits is CMD (0x17 = 23 = フルスクリーン切り替え!), upper bits = directions!
print("\n=== Decoded Mouse Gestures ===")
dir_map = {1: "↑", 2: "↓", 3: "←", 4: "→"}
for line in ini_text.splitlines():
    m = re.match(r"^Gesture_(\d+)=(\d+)$", line.strip())
    if m:
        gid, val = int(m.group(1)), int(m.group(2))
        cmd = val & 0xFFFF
        dirs_val = val >> 16
        # Check nibbles or base-5 of dirs_val
        cname = cmd_names.get(cmd, f"CMD({cmd})")
        print(f"Gesture_{gid}: val={val} (hex={hex(val)}), cmd={cmd} ({cname}), upper={hex(dirs_val)} (oct={oct(dirs_val)})")
