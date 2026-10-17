import os
import struct
import json

exe_path = r"C:\win32\MangaMeeya_74b\MangaMeeya.exe"
folder_path = os.path.dirname(exe_path)

with open(exe_path, "rb") as f:
    data = f.read()

# Parse PE header
e_lfanew = struct.unpack_from("<I", data, 0x3C)[0]
sig = data[e_lfanew:e_lfanew+4]
assert sig == b"PE\x00\x00"

num_sections = struct.unpack_from("<H", data, e_lfanew + 6)[0]
size_of_optional = struct.unpack_from("<H", data, e_lfanew + 20)[0]
opt_offset = e_lfanew + 24
magic = struct.unpack_from("<H", data, opt_offset)[0]

# Resource directory RVA is at offset 112 (PE32) in optional header
rsrc_rva, rsrc_size = struct.unpack_from("<II", data, opt_offset + 112)

sections_offset = opt_offset + size_of_optional
sections = []
for i in range(num_sections):
    s_off = sections_offset + i * 40
    name = data[s_off:s_off+8].rstrip(b"\x00").decode("ascii", errors="replace")
    vsize, va, raw_size, raw_ptr = struct.unpack_from("<IIII", data, s_off + 8)
    sections.append((name, va, vsize, raw_ptr, raw_size))
    print(f"Section: {name}, VA={hex(va)}, VSize={hex(vsize)}, RawPtr={hex(raw_ptr)}, RawSize={hex(raw_size)}")

def rva_to_offset(rva):
    for name, va, vsize, raw_ptr, raw_size in sections:
        if va <= rva < va + max(vsize, raw_size):
            return raw_ptr + (rva - va)
    raise ValueError(f"RVA {hex(rva)} not found")

rsrc_base = rva_to_offset(rsrc_rva)
print(f"Resource base offset: {hex(rsrc_base)}")

def read_res_dir(offset):
    num_named, num_id = struct.unpack_from("<HH", data, offset + 12)
    entries = []
    pos = offset + 16
    for _ in range(num_named + num_id):
        name_or_id, offset_to_data = struct.unpack_from("<II", data, pos)
        pos += 8
        if name_or_id & 0x80000000:
            name_off = rsrc_base + (name_or_id & 0x7FFFFFFF)
            length = struct.unpack_from("<H", data, name_off)[0]
            name = data[name_off+2 : name_off+2+length*2].decode("utf-16le", errors="replace")
        else:
            name = name_or_id
        is_dir = bool(offset_to_data & 0x80000000)
        child_off = rsrc_base + (offset_to_data & 0x7FFFFFFF)
        entries.append((name, is_dir, child_off))
    return entries

def get_resources():
    tree = {}
    for type_id, is_dir, type_off in read_res_dir(rsrc_base):
        tree[type_id] = {}
        if is_dir:
            for res_id, is_dir2, id_off in read_res_dir(type_off):
                if is_dir2:
                    for lang_id, is_dir3, lang_off in read_res_dir(id_off):
                        if not is_dir3:
                            data_rva, size, codepage, _ = struct.unpack_from("<IIII", data, lang_off)
                            file_off = rva_to_offset(data_rva)
                            tree[type_id][res_id] = data[file_off:file_off+size]
    return tree

res_tree = get_resources()
print("Resource types found:", list(res_tree.keys()))

RT_MENU = 4
RT_DIALOG = 5
RT_STRING = 6
RT_ACCELERATOR = 9

def read_wstring(buf, pos):
    end = pos
    while end + 1 < len(buf):
        if buf[end] == 0 and buf[end+1] == 0:
            break
        end += 2
    s = buf[pos:end].decode("utf-16le", errors="replace")
    return s, end + 2

# 1. Parse RT_STRING
strings = {}
if RT_STRING in res_tree:
    for block_id, buf in res_tree[RT_STRING].items():
        pos = 0
        for i in range(16):
            if pos + 2 > len(buf):
                break
            length = struct.unpack_from("<H", buf, pos)[0]
            pos += 2
            if length > 0:
                s = buf[pos:pos+length*2].decode("utf-16le", errors="replace")
                sid = (block_id - 1) * 16 + i
                strings[sid] = s
                pos += length * 2

print(f"Extracted {len(strings)} strings.")

# 2. Parse RT_MENU
def parse_menu_Resource(buf):
    version, header_size = struct.unpack_from("<HH", buf, 0)
    pos = 4 + header_size
    if version == 0:
        # Standard MENUHEADER
        def parse_items(pos):
            items = []
            while pos < len(buf):
                flags = struct.unpack_from("<H", buf, pos)[0]
                pos += 2
                is_popup = bool(flags & 0x0010)
                is_end = bool(flags & 0x0080)
                if is_popup:
                    text, pos = read_wstring(buf, pos)
                    children, pos = parse_items(pos)
                    items.append({
                        "type": "popup",
                        "text": text,
                        "children": children
                    })
                else:
                    menu_id = struct.unpack_from("<H", buf, pos)[0]
                    pos += 2
                    text, pos = read_wstring(buf, pos)
                    is_sep = (menu_id == 0 and text == "") or bool(flags & 0x0800)
                    item = {
                        "type": "separator" if is_sep else "item",
                        "id": menu_id,
                        "text": "---" if is_sep else text,
                        "flags": flags
                    }
                    if not is_sep and menu_id in strings:
                        item["help"] = strings[menu_id]
                    items.append(item)
                if is_end:
                    break
            return items, pos
        items, _ = parse_items(pos)
        return items
    elif version == 1:
        # MENUEX_TEMPLATE_HEADER
        def align4(p):
            return (p + 3) & ~3
        def parse_ex_items(pos):
            items = []
            while pos < len(buf):
                pos = align4(pos)
                dwType, dwState, menuId, bResInfo = struct.unpack_from("<IIIH", buf, pos)
                pos += 14
                text, pos = read_wstring(buf, pos)
                pos = align4(pos)
                is_popup = bool(bResInfo & 0x01)
                is_end = bool(bResInfo & 0x80)
                is_sep = bool(dwType & 0x0800)
                if is_popup:
                    dwHelpId = struct.unpack_from("<I", buf, pos)[0]
                    pos += 4
                    children, pos = parse_ex_items(pos)
                    items.append({
                        "type": "popup",
                        "id": menuId,
                        "text": text,
                        "children": children
                    })
                else:
                    item = {
                        "type": "separator" if is_sep else "item",
                        "id": menuId,
                        "text": "---" if is_sep else text,
                    }
                    if not is_sep and menuId in strings:
                        item["help"] = strings[menuId]
                    items.append(item)
                if is_end:
                    break
            return items, pos
        items, _ = parse_ex_items(pos)
        return items

menus = {}
if RT_MENU in res_tree:
    for menu_id, buf in res_tree[RT_MENU].items():
        menus[menu_id] = parse_menu_Resource(buf)

print(f"Extracted {len(menus)} menus: {list(menus.keys())}")

# 3. Parse RT_ACCELERATOR
accels = {}
if RT_ACCELERATOR in res_tree:
    for acc_id, buf in res_tree[RT_ACCELERATOR].items():
        pos = 0
        acc_list = []
        while pos + 8 <= len(buf):
            fVirt, key, cmd, pad = struct.unpack_from("<HHHH", buf, pos)
            pos += 8
            acc_list.append({
                "fVirt": fVirt,
                "key": key,
                "cmd": cmd,
                "help": strings.get(cmd, "")
            })
            if fVirt & 0x80:
                break
        accels[acc_id] = acc_list

print(f"Extracted {len(accels)} accelerator tables.")

# 4. Parse RT_DIALOG
def align4(p):
    return (p + 3) & ~3

def read_sz_or_ord(buf, pos):
    w = struct.unpack_from("<H", buf, pos)[0]
    if w == 0x0000:
        return None, pos + 2
    elif w == 0xFFFF:
        ord_val = struct.unpack_from("<H", buf, pos + 2)[0]
        return ord_val, pos + 4
    else:
        s, pos = read_wstring(buf, pos)
        return s, pos

dialogs = {}
if RT_DIALOG in res_tree:
    for dlg_id, buf in res_tree[RT_DIALOG].items():
        try:
            ver, sig = struct.unpack_from("<HH", buf, 0)
            if sig == 0xFFFF and ver == 1:
                # DLGTEMPLATEEX
                helpID, exStyle, style, cDlgItems, x, y, cx, cy = struct.unpack_from("<IIIHhhhh", buf, 4)
                pos = 26
                menu, pos = read_sz_or_ord(buf, pos)
                wndClass, pos = read_sz_or_ord(buf, pos)
                title, pos = read_wstring(buf, pos)
                if style & 0x40: # DS_SETFONT
                    pointsize, weight, italic, charset = struct.unpack_from("<HHBB", buf, pos)
                    pos += 6
                    font, pos = read_wstring(buf, pos)
                controls = []
                for _ in range(cDlgItems):
                    pos = align4(pos)
                    c_helpID, c_exStyle, c_style, c_x, c_y, c_cx, c_cy, c_id = struct.unpack_from("<IIIhhhhI", buf, pos)
                    pos += 24
                    c_class, pos = read_sz_or_ord(buf, pos)
                    c_title, pos = read_sz_or_ord(buf, pos)
                    extra_count = struct.unpack_from("<H", buf, pos)[0]
                    pos += 2 + extra_count
                    controls.append({
                        "id": c_id & 0xFFFF,
                        "class": c_class,
                        "title": c_title,
                        "style": hex(c_style)
                    })
                dialogs[dlg_id] = {"title": title, "controls": controls}
            else:
                # Standard DLGTEMPLATE
                style, exStyle, cDlgItems, x, y, cx, cy = struct.unpack_from("<IIHhhhh", buf, 0)
                pos = 18
                menu, pos = read_sz_or_ord(buf, pos)
                wndClass, pos = read_sz_or_ord(buf, pos)
                title, pos = read_wstring(buf, pos)
                if style & 0x40: # DS_SETFONT
                    pointsize = struct.unpack_from("<H", buf, pos)[0]
                    pos += 2
                    font, pos = read_wstring(buf, pos)
                controls = []
                for _ in range(cDlgItems):
                    pos = align4(pos)
                    c_style, c_exStyle, c_x, c_y, c_cx, c_cy, c_id = struct.unpack_from("<IIhhhhH", buf, pos)
                    pos += 18
                    c_class, pos = read_sz_or_ord(buf, pos)
                    c_title, pos = read_sz_or_ord(buf, pos)
                    extra_count = struct.unpack_from("<H", buf, pos)[0]
                    pos += 2 + extra_count
                    controls.append({
                        "id": c_id,
                        "class": c_class,
                        "title": c_title,
                        "style": hex(c_style)
                    })
                dialogs[dlg_id] = {"title": title, "controls": controls}
        except Exception as e:
            dialogs[dlg_id] = {"error": str(e)}

print(f"Extracted {len(dialogs)} dialogs.")

out_dir = os.path.dirname(__file__)
with open(os.path.join(out_dir, "extracted_resources.json"), "w", encoding="utf-8") as f:
    json.dump({
        "menus": menus,
        "dialogs": dialogs,
        "strings": strings,
        "accelerators": accels
    }, f, ensure_ascii=False, indent=2)

print("Successfully saved to extracted_resources.json")
