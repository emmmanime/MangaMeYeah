# MangaMeYeah! アーキテクチャおよびコンポーネント設計書 (Architecture & Components)

本書は、**MangaMeYeah!** の内部構造、モジュール分割、主要クラス・サービス群、およびインターフェース・定数仕様を定義する技術設計書です。

---

## 1. 全体アーキテクチャ概要

MangaMeYeah! は、C# .NET 8 および Windows Presentation Foundation (WPF) をベースとした高パフォーマンス・デスクトップアプリケーションです。

```
┌────────────────────────────────────────────────────────┐
│                   UI Layer (WPF XAML)                  │
│   MainWindow  /  PreferencesWindow  /  VersionWindow   │
│   ImageInfoWindow  /  RegistrySettingsWindow           │
└───────────────────────────┬────────────────────────────┘
                            │
┌───────────────────────────▼────────────────────────────┐
│               ViewModel & Presentation Logic           │
│   MainWindow.xaml.cs  /  ThumbnailItemViewModel        │
│   MouseGestureEngine  /  OSD Notification Controller   │
└───────────────────────────┬────────────────────────────┘
                            │
┌───────────────────────────▼────────────────────────────┐
│                    Service Layer                       │
│  ├─ PageCacheEngine (デコード・LRU・先読みスレッド)       │
│  ├─ ArchiveImageService (SharpCompress 書庫展開)       │
│  ├─ ConnectedImageDetector (24通り分割画像自動結合)      │
│  ├─ ImageFolderService (階層自然順グループソート)       │
│  ├─ QuickSaveService (QS1〜QS4 ファイルコピー・移動)    │
│  └─ ExplorerContextMenuService (HKCU レジストリ登録)   │
└───────────────────────────┬────────────────────────────┘
                            │
┌───────────────────────────▼────────────────────────────┐
│                    Model & Settings                    │
│   ViewerSettings (INI 永続化・レジューム情報管理)       │
│   ImageEntry / PageDisplaySlot / GestureCommand        │
└────────────────────────────────────────────────────────┘
```

---

## 2. ディレクトリおよびプロジェクト構造

```
src/MangaMeYeah/
 ├── App.xaml / App.xaml.cs            # アプリケーションエントリ、単一インスタンス IPC、例外補足
 ├── MainWindow.xaml / .cs             # メインキャンバス、メニュー、ルーペ、イベントハンドラ
 ├── PreferencesWindow.xaml / .cs      # 環境設定ダイアログ (Tab 1〜5)
 ├── ImageInfoWindow.xaml / .cs        # モーダレス画像情報ダイアログ
 ├── RegistrySettingsWindow.xaml / .cs # エクスプローラー右クリックメニュー設定ダイアログ
 ├── QuickSaveSettingsWindow.xaml / .cs# クイックセーブ保存先パス設定
 ├── VersionWindow.xaml / .cs          # バージョン情報モーダル
 ├── Assets/                           # アイコン (.ico), ロゴ画像 (.png)
 ├── Models/
 │    ├── ViewerSettings.cs            # 全設定プロパティ、MangaMeYeah.ini パーサー/シリアライザー
 │    ├── ImageEntry.cs                # 画像メタデータ（パス、書庫内エントリ、原寸サイズ）
 │    └── PageDisplaySlot.cs           # 描画スロット（単ページ／見開き左右／結合パズル）
 └── Services/
      ├── PageCacheEngine.cs           # 先読み・LRUキャッシュ管理エンジン
      ├── ArchiveImageService.cs       # ZIP/CBZ, RAR, 7z, TAR, ネスト書庫オンメモリストリーム展開
      ├── ConnectedImageDetector.cs    # 連番画像の境界色差解析・24順列全探索結合
      ├── ImageFolderService.cs        # 階層自然順ソート・兄弟フォルダ先行探索
      ├── MouseGestureEngine.cs        # 右ドラッグ軌跡解析・コード操作判定
      ├── QuickSaveService.cs          # クイックセーブ実行エンジン
      ├── ExplorerContextMenuService.cs# HKCU レジストリ登録・解除
      └── NativeColorDialogService.cs  # Win32 P/Invoke カラーピッカー
```

---

## 3. コアモジュール・サービス設計

### 3-1. `PageCacheEngine` (画像デコード・先読み・メモリ管理)
- **役割**:
  ディスクまたは書庫ストリームから画像バイト列を取得し、表示用ビットマップ（`BitmapSource` / `WriteableBitmap`）へデコードしてメモリにキャッシュする。
- **主要メンバー**:
  - `void RequestPage(int pageIndex, PagePriority priority)`: 指定ページのキャッシュ要求を発行。
  - `BitmapSource? GetCachedImage(int pageIndex)`: キャッシュ済みビットマップの即時取得。
  - `void Clear()`: 全キャッシュの破棄。
  - `void SetThreadPriority(int level)`: バックグラウンド先読みスレッドの優先度（0: Lowest 〜 4: Highest）を制御。
- **メモリ管理原則**:
  - デコード一時バッファに `System.Buffers.ArrayPool<byte>.Shared` を使用し、GC（ガベージコレクション）による LOH 断片化を防止。
  - キャッシュ合計バイト数が `ViewerSettings.MaxCacheMemoryMB`（デフォルト 2048 MB）を超過した場合、現在ページから最も離れたページを LRU 順に破棄。

### 3-2. `ArchiveImageService` (書庫展開エンジン)
- **役割**:
  各種書庫（Zip, CBZ, RAR, CBR, 7z, TAR）を一時ファイル化することなく、メモリ上のストリームとして直接展開・走査する。
- **ネスト書庫（書庫内書庫）処理**:
  - 書庫内のエントリ拡張子が `.zip`, `.cbz` 等である場合、親書庫の展開ストリームから `MemoryStream` を生成して再帰的にアーカイブリーダーを開く。
  - ディスクへの書き込みが一切発生しないため、高速かつディスク I/O 寿命を浪費しない。

### 3-3. `ConnectedImageDetector` (X 分割画像自動結合エンジン)
- **役割**:
  同一フォルダ内に存在する連番画像（4枚組グリッド、縦横2〜4分割）を画像処理により自動検知し、単一の結合画像として組み立てる。
- **主要アルゴリズム**:
  - `bool TryDetectGrid2x2(IReadOnlyList<ImageEntry> entries, ...)`: 4枚の候補画像を $4! = 24$ 通りの順列に並べ替え、上下左右の接合境界（シーム）の平均絶対誤差（MAE）を評価。
  - `double CalculateSeamColorVariance(BitmapSource img, Edge edge)`: シーム付近の標準偏差が `7.0` 以上であることを検証し、純白や単色背景の誤結合を防止。

### 3-4. `ImageFolderService` (ファイル走査・階層自然順グループソート)
- **役割**:
  指定パス（フォルダまたはファイル）を受け取り、画像ファイルリストを構築する。
- **階層自然順グループソート**:
  ```csharp
  // ① 親フォルダ直下の画像
  // ② サブフォルダ（自然順）配下の画像
  // ③ 書庫ファイル（自然順）配下の画像
  ```
- **兄弟フォルダ先行探索**:
  閲覧中のフォルダと同じ親を持つ隣接フォルダ（前後の巻）をバックグラウンドスレッドで先行取得し、巻末到達時の遅延を 0ms に抑える。

### 3-5. `ViewerSettings` (INI 永続化エンジン)
- **ファイル名**: `MangaMeYeah.ini`（UTF-8, BOMなし）
- **主要セクション**:
  - `[Config]`: 表示モード、スケール、自動見開き、ソート順、複数起動禁止等
  - `[Cache]`: 展開キャッシュ数、先読み数、先読み優先度、最大メモリ容量
  - `[Loupe]`: ルーペ形状（円/矩形）、サイズ、初期倍率
  - `[QuickSave]`: QS1〜QS4 保存先フォルダパス
  - `[Resume]`: フォルダ/書庫パスごとの最終閲覧ページ番号

---

## 4. 定数および主要識別子

### 4-1. IPC パイプ名およびミューテックス名
```csharp
public const string SingleInstanceMutexName = @"Global\MangaMeYeah_SingleInstance_Mutex";
public const string IpcPipeName = "MangaMeYeah_IpcPipe";
```

### 4-2. エクスプローラー レジストリキーパス (HKCU)
```csharp
private const string FolderShellKey = @"Software\Classes\Directory\shell\MangaMeYeah";
private const string FolderBgShellKey = @"Software\Classes\Directory\Background\shell\MangaMeYeah";
private const string FileShellKey = @"Software\Classes\*\shell\MangaMeYeah";
```
