# ExplorerAlternative

Windows Explorer代替のWPFデスクトップアプリケーションです。ファイル操作に加えて、
Visual Studio Code風の統合ターミナル、macOSのQuick Lookに近いプレビュー、Git/SVN
情報表示・操作などを1つのウィンドウに統合しています。

日本語UIを基本としています。

## 主な機能

### 表示・ナビゲーション
- パンくず形式のアドレスバー（各階層のクリック移動・ドロップダウンからの兄弟フォルダ選択・パスの部分編集）
- 詳細表示／階層表示（フォルダとファイルを1つの階層構造で表示）
- 左ペイン：お気に入り・タグ・ドライブ・ワークスペース・最近使った場所・最近のプロジェクト
- タブによる複数フォルダの並行操作、メイン領域の分割ビュー（縦/横分割・アクティブペイン表示）
- Space キーによるQuick Look風プレビュー（Markdownのレンダリング表示、フォルダのプレビューにも対応）

### ファイル操作
- コピー・移動・名前変更・削除・複製・一括名前変更
- ドラッグ＆ドロップ（Shift=移動 / Ctrl=コピー（同一フォルダ内は複製） / Alt=ショートカット作成、コピー先に同名ファイルがある場合は競合解決ダイアログ）
- 操作履歴（Undo）、ファイル操作キュー
- 外部ツール連携（任意の実行ファイル・引数を登録して右クリックメニューから起動）

### ターミナル・シェル連携
- 画面下部の統合PowerShellターミナル（複数タブ対応）
- Explorer側フォルダとターミナルのカレントディレクトリを同期するON/OFF切替
- プロジェクトルート／Gitルート／ソリューションルートへのジャンプ

### バージョン管理（Git / SVN）
- 現在フォルダの祖先方向を探索してGit/SVN管理下かどうかを自動判定し、状態を表示
- ステージ・コミット・プッシュ・プル・Fetch・ブランチ切替・新規ブランチ作成・Merge・Rebase・Stash・Clone・Initialize・変更の破棄
- 資格情報プロンプト等でUIをブロックしないよう、実行は統合ターミナルへ委譲
- Patchの作成・適用

### そのほか
- SSH接続の管理（Windows Credential Managerと連携した認証情報保存）
- ワークスペース（ウィンドウ状態・タブ構成の保存/復元）
- システムトレイ常駐・グローバルホットキー・タスクバーのジャンプリスト
- コマンドパレット、ショートカットチートシート
- ライト/ダーク/システム追従テーマ

## 動作環境

- Windows
- ビルドして実行する場合：.NET 10 SDK
- [Releases](../../releases) からダウンロードする場合：自己完結型ビルドのため追加のランタイムインストールは不要です

## ビルド・実行方法

```bash
# 開発実行
dotnet run --project ExplorerAlternative/ExplorerAlternative.csproj

# リリースビルド（自己完結型・単一ファイル）
dotnet publish ExplorerAlternative/ExplorerAlternative.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

テストの実行:

```bash
dotnet test ExplorerAlternative.Tests
```

## プロジェクト構成

```
ExplorerAlternative/       アプリ本体（WPF, MVVM）
  Views/                   ダイアログ等のView
  ViewModels/               ViewModel
  Services/                 ファイルシステム・Git/SVN・ターミナル等のロジック
  Themes/                    ライト/ダークテーマのリソースディクショナリ
ExplorerAlternative.Tests/ 単体テスト
docs/仕様.md                正式な仕様書（最新・詳細）
CLAUDE.md                   開発初期の縮小版仕様書
```

## ドキュメント

- 詳細な仕様は [`docs/仕様.md`](docs/仕様.md) を参照してください。
- アプリ内の「☰ メニュー」→「ショートカット一覧」から、実装済みのキーボードショートカット一覧を確認できます。
