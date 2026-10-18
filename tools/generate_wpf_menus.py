import json
import os
import re
import html

repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
json_path = os.path.join(repo_root, "docs", "extracted_resources.json")
xaml_path = os.path.join(repo_root, "src", "MangaMeYeah", "MainWindow.xaml")

with open(json_path, "r", encoding="utf-8") as f:
    data = json.load(f)

def to_wpf_header(text: str) -> str:
    # Replace tab with space for clean header display
    text = re.sub(r"\t+", " ", text).strip()
    # Escape XML entities first, then convert (&X) -> (_X) for WPF AccessKey
    escaped = html.escape(text, quote=True)
    # In WPF, single underscore is access key, so any literal underscore should be doubled first
    escaped = escaped.replace("_", "__")
    # Convert (&amp;X) or （&amp;X） to (_X)
    escaped = re.sub(r"([\(（])&amp;([A-Za-z0-9])([\)）])", r"\1_\2\3", escaped)
    return escaped

def to_clean_feature_name(text: str) -> str:
    # Remove (&X) or （&X）
    s = re.sub(r"[\(（]&[A-Za-z0-9][\)）]", "", text)
    # Replace tabs with space
    s = re.sub(r"\t+", " ", s)
    # Remove trailing "..."
    s = re.sub(r"\.{3,}$", "", s).strip()
    return html.escape(s, quote=True)

def render_menu_items(items, indent=12):
    lines = []
    pad = " " * indent
    for idx, item in enumerate(items):
        itype = item.get("type")
        if itype == "separator":
            # Avoid duplicate consecutive separators
            if idx > 0 and items[idx - 1].get("type") == "separator":
                continue
            lines.append(f"{pad}<Separator />")
        elif itype == "popup":
            header = to_wpf_header(item.get("text", ""))
            children = item.get("children", [])
            if header:
                lines.append(f'{pad}<MenuItem Header="{header}">')
                lines.extend(render_menu_items(children, indent + 4))
                lines.append(f"{pad}</MenuItem>")
            else:
                # Root anonymous popup in context menus (e.g. Menu 295)
                lines.extend(render_menu_items(children, indent))
        else:
            mid = item.get("id", 0)
            raw_text = item.get("text", "")
            header = to_wpf_header(raw_text)
            feature_name = to_clean_feature_name(raw_text)
            if mid == 115:
                # バージョン情報
                lines.append(f'{pad}<MenuItem Header="{header}" Tag="{feature_name}" Click="VersionMenuItem_Click" />')
            elif mid == 127:
                # 終了
                lines.append(f'{pad}<MenuItem Header="{header}" Tag="{feature_name}" Click="ExitMenuItem_Click" />')
            else:
                lines.append(f'{pad}<MenuItem Header="{header}" Tag="{feature_name}" Click="UnimplementedMenuItem_Click" />')
    return lines

main_menu_lines = render_menu_items(data["menus"]["102"], indent=12)
context_menu_lines = render_menu_items(data["menus"]["295"], indent=16)

xaml_content = f"""<Window x:Class="MangaMeYeah.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d"
        Title="MangaMeYeah"
        Width="960"
        Height="720"
        WindowStartupLocation="CenterScreen">
    <DockPanel LastChildFill="True">
        <!-- メインメニューバー (MangaMeeya Resource ID: 102 完全再現) -->
        <Menu DockPanel.Dock="Top">
{chr(10).join(main_menu_lines)}
        </Menu>

        <!-- メインビューア領域 (背景色 #808080 は MangaMeeya 標準背景色) -->
        <Grid Background="#808080">
            <!-- 右クリックコンテキストメニュー (MangaMeeya Resource ID: 295 完全再現) -->
            <Grid.ContextMenu>
                <ContextMenu>
{chr(10).join(context_menu_lines)}
                </ContextMenu>
            </Grid.ContextMenu>
        </Grid>
    </DockPanel>
</Window>
"""

with open(xaml_path, "w", encoding="utf-8") as f:
    f.write(xaml_content)

print(f"Generated {xaml_path} ({len(xaml_content.splitlines())} lines)")
