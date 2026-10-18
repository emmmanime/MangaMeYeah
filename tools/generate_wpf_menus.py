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
    text = re.sub(r"\t+", " ", text).strip()
    escaped = html.escape(text, quote=True)
    escaped = escaped.replace("_", "__")
    escaped = re.sub(r"([\(（])&amp;([A-Za-z0-9])([\)）])", r"\1_\2\3", escaped)
    return escaped

def to_clean_feature_name(text: str) -> str:
    s = re.sub(r"[\(（]&[A-Za-z0-9][\)）]", "", text)
    s = re.sub(r"\t+", " ", s)
    s = re.sub(r"\.{3,}$", "", s).strip()
    return html.escape(s, quote=True)

def render_menu_items(items, indent=12, is_context_menu=False):
    lines = []
    pad = " " * indent
    for idx, item in enumerate(items):
        itype = item.get("type")
        if itype == "separator":
            if idx > 0 and items[idx - 1].get("type") == "separator":
                continue
            lines.append(f"{pad}<Separator />")
        elif itype == "popup":
            header = to_wpf_header(item.get("text", ""))
            children = item.get("children", [])
            if header:
                lines.append(f'{pad}<MenuItem Header="{header}">')
                lines.extend(render_menu_items(children, indent + 4, is_context_menu))
                lines.append(f"{pad}</MenuItem>")
            else:
                lines.extend(render_menu_items(children, indent, is_context_menu))
        else:
            mid = item.get("id", 0)
            raw_text = item.get("text", "")
            header = to_wpf_header(raw_text)
            feature_name = to_clean_feature_name(raw_text)
            ctx_prefix = "CTX_" if is_context_menu else "MAIN_"
            uid = f"{ctx_prefix}{mid}"
            if mid == 115:
                lines.append(f'{pad}<MenuItem Uid="{uid}" Header="{header}" Tag="{feature_name}" Click="VersionMenuItem_Click" />')
            elif mid == 127:
                lines.append(f'{pad}<MenuItem Uid="{uid}" Header="{header}" Tag="{feature_name}" Click="ExitMenuItem_Click" />')
            else:
                lines.append(f'{pad}<MenuItem Uid="{uid}" Header="{header}" Tag="{feature_name}" Click="MenuItem_Click" />')
                if mid == 40010:
                    subpad = " " * (indent + 4)
                    lines.append(f'{pad}<MenuItem Header="複数枚・連結表示(_M)">')
                    lines.append(f'{subpad}<MenuItem Uid="{ctx_prefix}40010" Header="横2枚表示 (標準見開き)" Tag="横2枚表示" Click="MenuItem_Click" />')
                    lines.append(f'{subpad}<MenuItem Uid="{ctx_prefix}40201" Header="縦2枚表示" Tag="縦2枚表示" Click="MenuItem_Click" />')
                    lines.append(f'{subpad}<MenuItem Uid="{ctx_prefix}40202" Header="横3枚表示" Tag="横3枚表示" Click="MenuItem_Click" />')
                    lines.append(f'{subpad}<MenuItem Uid="{ctx_prefix}40203" Header="縦3枚表示" Tag="縦3枚表示" Click="MenuItem_Click" />')
                    lines.append(f'{subpad}<MenuItem Uid="{ctx_prefix}40204" Header="横4枚表示" Tag="横4枚表示" Click="MenuItem_Click" />')
                    lines.append(f'{subpad}<MenuItem Uid="{ctx_prefix}40205" Header="縦4枚表示" Tag="縦4枚表示" Click="MenuItem_Click" />')
                    lines.append(f'{subpad}<MenuItem Uid="{ctx_prefix}40206" Header="縦横2×2枚表示 (4枚)" Tag="縦横2×2枚表示" Click="MenuItem_Click" />')
                    lines.append(f'{pad}</MenuItem>')
    return lines

main_menu_lines = render_menu_items(data["menus"]["102"], indent=12, is_context_menu=False)
context_menu_lines = render_menu_items(data["menus"]["295"], indent=16, is_context_menu=True)

xaml_content = f"""<Window x:Class="MangaMeYeah.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d"
        Title="MangaMeYeah"
        Icon="pack://application:,,,/Assets/MangaMeYeah.ico"
        Width="1120"
        Height="800"
        WindowStartupLocation="CenterScreen"
        AllowDrop="True"
        Drop="Window_Drop"
        DragOver="Window_DragOver"
        PreviewKeyDown="Window_PreviewKeyDown"
        SizeChanged="Window_SizeChanged"
        Closing="Window_Closing">
    <Grid>
        <!-- メインビューア領域 (通常時は上部メニューバー分 Margin="0,22,0,0"、フルスクリーン時は Margin="0" で画面全体を使用しメニューがオーバーレイでプルダウン) -->
        <Grid x:Name="ViewerRootGrid"
              Margin="0,22,0,0"
              Background="#808080"
              ClipToBounds="True"
              Focusable="True"
              MouseDown="ViewerRootGrid_MouseDown"
              MouseMove="ViewerRootGrid_MouseMove"
              MouseUp="ViewerRootGrid_MouseUp"
              PreviewMouseWheel="ViewerRootGrid_PreviewMouseWheel">

            <!-- 右クリックコンテキストメニュー (MangaMeeya Resource ID: 295 完全再現) -->
            <Grid.ContextMenu>
                <ContextMenu x:Name="ViewerContextMenu">
{chr(10).join(context_menu_lines)}
                </ContextMenu>
            </Grid.ContextMenu>

            <!-- 初期ガイド表示 (画像未読込時) -->
            <StackPanel x:Name="EmptyStatePanel"
                        VerticalAlignment="Center"
                        HorizontalAlignment="Center"
                        IsHitTestVisible="False">
                <TextBlock Text="MangaMeYeah"
                           Foreground="#D8D8D8"
                           FontSize="28"
                           FontWeight="Bold"
                           HorizontalAlignment="Center"
                           Margin="0,0,0,10" />
                <TextBlock Text="画像ファイルまたはフォルダをここにドラッグ＆ドロップ、またはエクスプローラー右クリック「MangaMeYeahで開く」"
                           Foreground="#CFCFCF"
                           FontSize="13"
                           HorizontalAlignment="Center"
                           Margin="0,0,0,6" />
                <TextBlock Text="左クリック: 次へ ｜ 右クリック: 前へ ｜ 右ドラッグ: マウスジェスチャ (↖↗↙↘ クイックセーブ1〜4 / → サムネイル選別) ｜ 中クリック: ルーペ"
                           Foreground="#B8B8B8"
                           FontSize="12"
                           HorizontalAlignment="Center" />
            </StackPanel>

            <!-- スクロール＆見開き表示コンテナ -->
            <ScrollViewer x:Name="ImageScrollViewer"
                          HorizontalScrollBarVisibility="Hidden"
                          VerticalScrollBarVisibility="Hidden"
                          Focusable="False"
                          PanningMode="Both">
                <Grid x:Name="PageCanvasGrid"
                      HorizontalAlignment="Center"
                      VerticalAlignment="Center">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>

                    <!-- 左側画像スロット -->
                    <Image x:Name="LeftPageImage"
                           Grid.Column="0"
                           Stretch="Uniform"
                           RenderOptions.BitmapScalingMode="HighQuality"
                           SnapsToDevicePixels="True"
                           UseLayoutRounding="True" />

                    <!-- 右側画像スロット -->
                    <Image x:Name="RightPageImage"
                           Grid.Column="1"
                           Stretch="Uniform"
                           RenderOptions.BitmapScalingMode="HighQuality"
                           SnapsToDevicePixels="True"
                           UseLayoutRounding="True" />
                </Grid>
            </ScrollViewer>

            <!-- ルーペ (拡大鏡) オーバーレイ -->
            <Canvas x:Name="LoupeOverlayCanvas"
                    Visibility="Collapsed"
                    IsHitTestVisible="False">
                <Border x:Name="LoupeBorder"
                        Width="280"
                        Height="280"
                        BorderBrush="#00FF66"
                        BorderThickness="2"
                        Background="#202020"
                        ClipToBounds="True">
                    <Border.Effect>
                        <DropShadowEffect BlurRadius="12" ShadowDepth="3" Opacity="0.6" />
                    </Border.Effect>
                    <Canvas ClipToBounds="True">
                        <Rectangle x:Name="LoupeViewportRect" Width="280" Height="280">
                            <Rectangle.Fill>
                                <VisualBrush x:Name="LoupeVisualBrush"
                                             Visual="{{Binding ElementName=PageCanvasGrid}}"
                                             ViewboxUnits="Absolute"
                                             Stretch="None" />
                            </Rectangle.Fill>
                        </Rectangle>
                    </Canvas>
                </Border>
            </Canvas>

            <!-- マウスジェスチャ軌跡＆コマンド名オーバーレイ -->
            <Canvas x:Name="GestureOverlayCanvas"
                    Visibility="Collapsed"
                    IsHitTestVisible="False">
                <Polyline x:Name="GestureTrailPolyline"
                          Stroke="#00FF66"
                          StrokeThickness="3"
                          StrokeLineJoin="Round"
                          StrokeStartLineCap="Round"
                          StrokeEndLineCap="Round" />
                <Border x:Name="GestureBadgeBorder"
                        Canvas.Left="24"
                        Canvas.Top="52"
                        Background="#CC181818"
                        BorderBrush="#00FF66"
                        BorderThickness="1"
                        CornerRadius="4"
                        Padding="12,6">
                    <TextBlock x:Name="GestureStatusText"
                               Foreground="#00FF66"
                               FontSize="16"
                               FontWeight="Bold" />
                </Border>
            </Canvas>

            <!-- MangaMeeya風 左上ステータスメッセージ OSD -->
            <Border x:Name="OsdStatusBorder"
                    HorizontalAlignment="Left"
                    VerticalAlignment="Top"
                    Margin="16,28,0,0"
                    Background="#BF141414"
                    BorderBrush="#4000FF66"
                    BorderThickness="1"
                    CornerRadius="4"
                    Padding="10,5"
                    Visibility="Collapsed"
                    IsHitTestVisible="False">
                <TextBlock x:Name="OsdStatusText"
                           Foreground="#00FF66"
                           FontSize="14"
                           FontWeight="SemiBold" />
            </Border>

            <!-- サムネイル一覧＆複数選択コレクション画面 (ライトテーブル) -->
            <Grid x:Name="ThumbnailOverlayGrid"
                  Background="#F21E1E1E"
                  Visibility="Collapsed">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="*" />
                </Grid.RowDefinitions>

                <!-- サムネイル操作＆一括クイックセーブバー -->
                <Border Grid.Row="0"
                        Background="#2D2D30"
                        BorderBrush="#444444"
                        BorderThickness="0,0,0,1"
                        Padding="12,8">
                    <DockPanel LastChildFill="False">
                        <TextBlock x:Name="ThumbSummaryText"
                                   DockPanel.Dock="Left"
                                   Foreground="White"
                                   FontWeight="Bold"
                                   VerticalAlignment="Center"
                                   Margin="0,0,16,0"
                                   Text="サムネイル一覧" />

                        <Button Content="全選択"
                                DockPanel.Dock="Left"
                                Padding="8,3"
                                Margin="0,0,6,0"
                                Click="ThumbSelectAll_Click" />
                        <Button Content="選択解除"
                                DockPanel.Dock="Left"
                                Padding="8,3"
                                Margin="0,0,16,0"
                                Click="ThumbClearSelection_Click" />

                        <TextBlock Text="一括クイックセーブ:"
                                   DockPanel.Dock="Left"
                                   Foreground="#CCCCCC"
                                   VerticalAlignment="Center"
                                   Margin="0,0,6,0" />
                        <Button x:Name="BatchQs1Button"
                                Content="QS1へ保存"
                                DockPanel.Dock="Left"
                                Padding="8,3"
                                Margin="0,0,4,0"
                                Click="BatchQuickSave1_Click" />
                        <Button x:Name="BatchQs2Button"
                                Content="QS2へ保存"
                                DockPanel.Dock="Left"
                                Padding="8,3"
                                Margin="0,0,4,0"
                                Click="BatchQuickSave2_Click" />
                        <Button x:Name="BatchQs3Button"
                                Content="QS3へ保存"
                                DockPanel.Dock="Left"
                                Padding="8,3"
                                Margin="0,0,4,0"
                                Click="BatchQuickSave3_Click" />
                        <Button x:Name="BatchQs4Button"
                                Content="QS4へ保存"
                                DockPanel.Dock="Left"
                                Padding="8,3"
                                Margin="0,0,12,0"
                                Click="BatchQuickSave4_Click" />
                        <Button Content="QS設定..."
                                DockPanel.Dock="Left"
                                Padding="8,3"
                                Click="OpenQuickSaveSettings_Click" />

                        <Button Content="閉じる (Esc)"
                                DockPanel.Dock="Right"
                                Padding="12,3"
                                Click="CloseThumbnailView_Click" />

                        <StackPanel DockPanel.Dock="Right"
                                    Orientation="Horizontal"
                                    VerticalAlignment="Center"
                                    Margin="0,0,16,0">
                            <TextBlock Text="サイズ:"
                                       Foreground="#CCCCCC"
                                       VerticalAlignment="Center"
                                       Margin="0,0,6,0" />
                            <Slider x:Name="ThumbSizeSlider"
                                    Width="110"
                                    Minimum="100"
                                    Maximum="400"
                                    Value="200"
                                    VerticalAlignment="Center"
                                    ValueChanged="ThumbSizeSlider_ValueChanged" />
                        </StackPanel>
                    </DockPanel>
                </Border>

                <!-- サムネイル一覧リスト -->
                <ScrollViewer Grid.Row="1"
                              VerticalScrollBarVisibility="Auto"
                              HorizontalScrollBarVisibility="Disabled"
                              Padding="10">
                    <ItemsControl x:Name="ThumbnailItemsControl">
                        <ItemsControl.ItemsPanel>
                            <ItemsPanelTemplate>
                                <WrapPanel Orientation="Horizontal" />
                            </ItemsPanelTemplate>
                        </ItemsControl.ItemsPanel>
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Border Width="{{Binding DataContext.ThumbnailCardWidth, RelativeSource={{RelativeSource AncestorType=Window}}}}"
                                        Height="{{Binding DataContext.ThumbnailCardHeight, RelativeSource={{RelativeSource AncestorType=Window}}}}"
                                        Margin="6"
                                        CornerRadius="4"
                                        Background="#262626"
                                        BorderThickness="2"
                                        MouseLeftButtonDown="ThumbnailCard_MouseLeftButtonDown">
                                    <Border.Style>
                                        <Style TargetType="Border">
                                            <Setter Property="BorderBrush" Value="#3E3E42" />
                                            <Style.Triggers>
                                                <DataTrigger Binding="{{Binding IsCurrentPage}}" Value="True">
                                                    <Setter Property="BorderBrush" Value="#00AAFF" />
                                                </DataTrigger>
                                                <DataTrigger Binding="{{Binding IsSelected}}" Value="True">
                                                    <Setter Property="BorderBrush" Value="#00FF66" />
                                                    <Setter Property="Background" Value="#1E3A28" />
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </Border.Style>
                                    <Grid Margin="4">
                                        <Grid.RowDefinitions>
                                            <RowDefinition Height="*" />
                                            <RowDefinition Height="Auto" />
                                        </Grid.RowDefinitions>
                                        <Image Grid.Row="0"
                                               Source="{{Binding ThumbnailBitmap}}"
                                               Stretch="Uniform"
                                               RenderOptions.BitmapScalingMode="HighQuality" />
                                        <CheckBox Grid.Row="0"
                                                  IsChecked="{{Binding IsSelected, Mode=TwoWay}}"
                                                  HorizontalAlignment="Left"
                                                  VerticalAlignment="Top"
                                                  Margin="4"
                                                  Click="ThumbnailCheckBox_Click">
                                            <CheckBox.LayoutTransform>
                                                <ScaleTransform ScaleX="1.25" ScaleY="1.25" />
                                            </CheckBox.LayoutTransform>
                                        </CheckBox>
                                        <TextBlock Grid.Row="1"
                                                   Foreground="#E0E0E0"
                                                   FontSize="11"
                                                   TextTrimming="CharacterEllipsis"
                                                   HorizontalAlignment="Center"
                                                   Margin="2,4,2,0">
                                            <Run Text="{{Binding DisplayPageNumber, Mode=OneWay}}" FontWeight="Bold" Foreground="#00FF66" />
                                            <Run Text=": " />
                                            <Run Text="{{Binding FileName, Mode=OneWay}}" />
                                        </TextBlock>
                                    </Grid>
                                </Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ScrollViewer>
            </Grid>
        </Grid>

        <!-- メインメニューバー (最前面の上部に配置、フルスクリーン時はマウスを上端に寄せるとプルダウン表示) -->
        <Menu x:Name="MainMenuBar"
              VerticalAlignment="Top"
              Height="22"
              Panel.ZIndex="100"
              MouseLeave="MainMenuBar_MouseLeave">
{chr(10).join(main_menu_lines)}
        </Menu>
    </Grid>
</Window>
"""

with open(xaml_path, "w", encoding="utf-8") as f:
    f.write(xaml_content)

print(f"Generated {xaml_path} ({len(xaml_content.splitlines())} lines)")
