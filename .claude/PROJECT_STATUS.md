# PROJECT STATUS

> Claude Codeがプロジェクトの進捗・現在地・人間による対応事項を復元するための管理ファイルです。作業開始時・完了時・中断時に確認・更新してください。

---

# 1. プロジェクト情報

| 項目 | 内容 |
| --- | --- |
| プロジェクト名 | ExplorerAlternative（Windows Explorer代替のWPFアプリ） |
| 開発方式 | Claude Codeによる自律開発 |
| リポジトリ | https://github.com/Jukiii/ExplorerAlternative (public) |
| 配布 | GitHub Releases（zip＋インストーラー `Setup.exe`（Inno Setup）。タグのプッシュで自動作成） |
| 最新リリース | v1.5.3 |
| 正式な仕様書 | `docs/仕様.md`（70章構成） |
| 最終更新日 | 2026-10-03 |
| 現在の状態 | IN_PROGRESS（フェーズ1〜10は実装済み。以降は Issue 単位で改善） |

---

# 2. ステータス定義

| Status | 意味 |
| --- | --- |
| `NOT_STARTED` | まだ開始していない |
| `IN_PROGRESS` | Claude Codeが作業中 |
| `WAITING_HUMAN` | 人間による対応待ち |
| `BLOCKED` | 問題により作業継続不可 |
| `COMPLETED` | 完了 |

---

# 3. フェーズ進捗（`docs/仕様.md` 67章）

| Phase | 内容 | Status | 備考 |
| --- | --- | --- | --- |
| Phase 1 | Explorer基盤 | COMPLETED | |
| Phase 2 | タブ・分割 | COMPLETED | ペインのドラッグリサイズは実装済み（境界のドラッグ・ダブルクリックで半分ずつ・ワークスペースに保存） |
| Phase 3 | プレビュー | COMPLETED | PDFはWindows標準描画で、全ページを縦に並べてスクロールで見る（遅延描画） |
| Phase 4 | ターミナル | COMPLETED | VS Codeと同じ単一画面構造・ANSIカラー・Ctrl+C・Tab補完。全画面TUIは非対応（ConPTYは断念。Issue【保留】） |
| Phase 5 | Version Control | COMPLETED | `.git`と`.svn`の併存も認識（右ペインのボタンで表示・操作する方を切り替え） |
| Phase 6 | SSH | COMPLETED | SFTPブラウザは独立ウィンドウ（リモートのプレビュー・パスフレーズ付き鍵に対応。メインペインでの統合は見送り） |
| Phase 7 | 高度な操作 | COMPLETED | クイックコピー/移動（60章）・Redoも実装済み |
| Phase 8 | Windows連携・カスタマイズ | COMPLETED | 「常にこのアプリで開く」はアプリ内だけの関連付けとして実装（Windowsの関連付けは変更しない。決定ログ0004） |
| Phase 9 | プロジェクト・ワークスペース | COMPLETED | 最大化・ターミナル・展開状態・ペインの比率も保存。SSH接続状態・Quick Look固定状態は、保存しない方針（決定ログ0002） |
| Phase 10 | 最終調整 | IN_PROGRESS | Releaseビルド・配布・リリース自動化・インストーラー（Inno Setup）は完了。コード署名・winget登録・Windows 11での統合確認は、アプリの完成後に検討（Issue【保留】） |

未実装・既知の制限の一覧と進捗は、GitHub Issue を参照してください。

---

# 4. 現在の作業

| 項目 | 内容 |
| --- | --- |
| ブランチ | なし（待機中。Jさんの実機確認の結果待ち） |
| 直近の完了 | v1.5.3 のリリース（ターミナル同期の`Set-Location`連打の修正、タッチスクロール、Logの+/-の背景色、左ペインの整理）。単体テストは約810件 |
| 状態 | WAITING_HUMAN（実機確認の結果待ち。進められる【対応不要】のIssueは無い） |

---

# 5. 人間による対応待ち

| 項目 | 状態 |
| --- | --- |
| 実機での確認（折りたたみボタンが矢印だけか、ターミナルが広がらないか、SFTP関連（接続できるサーバー待ち）、ターミナル・ファイルロックほか。Issue #36） | WAITING_HUMAN（Issue【要対応】） |
| SSHターミナルのメニュー・パスのドラッグ（#27）、Tab補完（#20）の実機確認 | WAITING_HUMAN（確認が取れたらクローズ） |
| Chrome（ChatGPT）に誤って入力された文字列の確認・削除（2026-09-19の自動操作の事故） | WAITING_HUMAN |
| 時期待ち（Issue【保留】）: #32 インストーラー・署名（アプリの完成後）、#21 全画面TUI（ConPTYが使えるようになるまで） | 保留 |
