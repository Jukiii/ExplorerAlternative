# PROJECT STATUS

> Claude Codeがプロジェクトの進捗・現在地・人間による対応事項を復元するための管理ファイルです。作業開始時・完了時・中断時に確認・更新してください。

---

# 1. プロジェクト情報

| 項目 | 内容 |
| --- | --- |
| プロジェクト名 | ExplorerAlternative（Windows Explorer代替のWPFアプリ） |
| 開発方式 | Claude Codeによる自律開発 |
| リポジトリ | https://github.com/Jukiii/ExplorerAlternative (public) |
| 配布 | GitHub Releases（自己完結型・単一ファイルのexeをzip化） |
| 最新リリース | v1.3.2 |
| 正式な仕様書 | `docs/仕様.md`（70章構成） |
| 最終更新日 | 2026-10-01 |
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
| Phase 3 | プレビュー | COMPLETED | PDFのページ表示などは未実装（Issue） |
| Phase 4 | ターミナル | COMPLETED | VS Codeと同じ単一画面構造・ANSIカラー・Ctrl+C。全画面TUIは非対応（ConPTYは断念） |
| Phase 5 | Version Control | COMPLETED | `.git`と`.svn`の併存は未対応（Issue） |
| Phase 6 | SSH | COMPLETED | SFTPブラウザは独立ウィンドウ（統合は Issue） |
| Phase 7 | 高度な操作 | COMPLETED | クイックコピー/移動（60章）・Redoは未実装（Issue） |
| Phase 8 | Windows連携・カスタマイズ | COMPLETED | 「常にこのアプリ」は未実装（Issue） |
| Phase 9 | プロジェクト・ワークスペース | COMPLETED | 最大化・ターミナル・展開状態・ペインの比率も保存。SSH接続状態・Quick Look固定状態は、保存しない方針（決定ログ0002） |
| Phase 10 | 最終調整 | IN_PROGRESS | Releaseビルド・配布は完了。インストーラー・Windows 11での統合確認・リリース自動化が未着手（Issue） |

未実装・既知の制限の一覧と進捗は、GitHub Issue を参照してください。

---

# 4. 現在の作業

| 項目 | 内容 |
| --- | --- |
| ブランチ | なし（待機中。次は Issue【対応不要】の中から着手する） |
| 直近の完了 | NOLITOとClaude・GitHubの運用ルールを共通化（PR #37。CI・Dependabot・PRテンプレート・ブランチ保護・Issue題名ルール） |
| 状態 | COMPLETED |

---

# 5. 人間による対応待ち

| 項目 | 状態 |
| --- | --- |
| 実機での確認（ターミナルの高さのドラッグ、ドラッグ&ドロップでのパス入力、ファイルロックの表示 ほか） | WAITING_HUMAN（Issue【要対応】） |
| Chrome（ChatGPT）に誤って入力された文字列の確認・削除（2026-09-19の自動操作の事故） | WAITING_HUMAN |
