# ScreenshotCutter

指定したモニターのスクリーンショットを、あらかじめ設定した矩形で切り出して保存する常駐型ツールです。

- グローバルホットキー 1 つで、**UI を一切表示せずに**「撮影 → 切り出し → 保存 / クリップボードコピー」まで完了します
- タスクトレイに常駐し、設定はトレイから行います
- インストーラー不要。フォルダーごとコピーするだけで動作します

---

## 動作環境

| 項目 | 内容 |
|---|---|
| OS | Windows 11 x64 のみ |
| ランタイム | 不要（self-contained 配置のため同梱） |

---

## 使い方

1. [Releases](../../releases) から zip をダウンロードし、任意のフォルダーへ展開します
2. `ScreenshotCutter.exe` を起動します（初回は設定ウィンドウが開きます）
3. 撮影対象のモニターと切り出し領域を設定します
4. 既定のホットキー **`Ctrl + Alt + S`** で撮影します

インストール作業はありません。不要になったらフォルダーごと削除してください
（自動起動を有効にした場合のみ、先に設定画面でオフにしてからにしてください）。

トレイアイコンを**ダブルクリック**すると設定ウィンドウ、**右クリック**でメニューが開きます。

---

## 開発

### 必要なもの

- .NET 10 SDK
- Visual Studio 2026（`ScreenshotCutter.slnx` を開いてください）

### ビルド

```bash
dotnet build ScreenshotCutter.slnx
```

### テスト

```bash
dotnet test tests/ScreenshotCutter.Tests/ScreenshotCutter.Tests.csproj
```

### 配布物の作成

```bash
dotnet publish src/ScreenshotCutter/ScreenshotCutter.csproj -p:PublishProfile=win-x64
```

`publish/win-x64/` に self-contained + ReadyToRun の一式が出力されます。これを zip に固めて配布します。

### 配布用 zip の作成

```bash
powershell -ExecutionPolicy Bypass -File tools/make-release.ps1
```

テスト実行 → 発行 → `dist/ScreenshotCutter-v<version>-win-x64.zip` の作成までを行い、
リリースノートに貼る SHA256 を表示します。zip は `ScreenshotCutter/` フォルダーを
1 つだけ含む構成なので、展開してもファイルが散らばりません。

### アイコンの再生成

```bash
powershell -ExecutionPolicy Bypass -File tools/generate-icon.ps1
```

---

## プロジェクト構成

```
ScreenshotCutter/
├─ ScreenshotCutter.slnx
├─ src/ScreenshotCutter/
│  ├─ Interop/      … Win32 P/Invoke（外部パッケージを増やさないため自前定義）
│  ├─ Models/       … 設定モデルとモニター情報
│  ├─ Services/     … 設定 I/O・キャプチャ・保存・通知・ホットキー・トレイ
│  ├─ ViewModels/   … 設定画面の状態
│  └─ Views/        … 設定ウィンドウ・切り出しオーバーレイ・識別オーバーレイ
├─ tests/ScreenshotCutter.Tests/
└─ tools/
   ├─ generate-icon.ps1  … アプリアイコンの生成
   └─ make-release.ps1   … 配布用 zip の作成
```

---

## 設定ファイル

`settings.json` は **exe と同じフォルダー**に置かれます。

```json
{
  "version": 1,
  "hotkey": { "modifiers": ["Ctrl", "Alt"], "key": "S" },
  "capture": {
    "monitorId": "DISPLAY#ACM1234#5&1a2b3c4d&0&UID256",
    "monitorDeviceName": "DISPLAY1",
    "monitorFriendlyName": "Sample Monitor A",
    "crop": { "enabled": true, "x": 243, "y": 32, "width": 2208, "height": 1344 }
  },
  "output": {
    "folder": "C:/Users/YourName/Pictures/ScreenshotCutter",
    "fileNameTemplate": "ScreenShot_{yyyyMMdd}_{HHmmss}",
    "saveToFile": true,
    "copyToClipboard": true
  },
  "notification": { "toast": true, "shutterSound": false },
  "startup": { "runAtLogon": false }
}
```

`capture.crop` の `x` / `y` は**対象モニターの左上を (0,0) とする**物理ピクセル座標です。
仮想デスクトップ座標（負の値を取りうる）ではないため、モニターの配置を変えても矩形はずれません。

### ファイル名テンプレート

`{}` の中身は .NET の日付書式として展開されます。`{seq}` だけは連番として扱われます。

| 記述 | 展開結果 |
|---|---|
| `{yyyyMMdd}` | `20260825` |
| `{HHmmss}` | `143052` |
| `{seq}` | `1`, `2`, `3`... |
| `{seq:000}` | `001`, `002`... |

`{seq}` を書かない場合、同名ファイルがあると末尾に `_001` から連番が付きます。

---

## 既知の制約

| 内容 |
|---|
| 保護コンテンツ（Netflix 等）やフルスクリーン排他モードのアプリは、`BitBlt` の制約により黒く映る場合があります（サポート対象外） |
| 管理者権限で動作するウィンドウが最前面にあるとき、ホットキーが届かないことがあります（UIPI の制約） |
| コード署名をしていないため、初回実行時に SmartScreen の警告が出ます |
| 通知は Windows 11 本来のトーストではなくバルーン通知です。アクションセンターには残りません（コピーのみで動作させるための選択です） |

---

## ライセンス

MIT License です。詳細は [LICENSE](LICENSE) を参照してください。

同梱のサードパーティ製コンポーネント（.NET ランタイム・CommunityToolkit.Mvvm）については
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) を参照してください。
**再配布する場合はこのファイルも一緒に配布してください。**

---

## 連絡先
不具合や追加要望等あれば「`https://x.com/04vani20`」までどうぞ