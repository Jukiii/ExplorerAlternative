# 0001 NOLITOとClaude・GitHubの運用ルールを共通にする

日付: 2026-10-01

## 決定

運営者の指示により、`Jukiii/NOLITO` と同じ運用ルールを、このリポジトリにも適用する。

| # | 項目 | 決定 | 反映先 |
| - | ---- | ---- | ------ |
| 1 | Claudeの自律開発指示書 | NOLITOの `.claude/CLAUDE.md` に合わせる。フェーズの出典・テスト・コマンドだけをこのプロジェクト向けに置き換える | `.claude/CLAUDE.md` |
| 2 | 進捗管理 | `.claude/PROJECT_STATUS.md` にフェーズごとの状態を記録する | `.claude/PROJECT_STATUS.md` |
| 3 | 作業ルール・ブランチ・Issueの題名と閉じ方 | NOLITOの `CLAUDE.md` の3節と同じ。【要対応】【対応不要】【保留】【依頼】の運用、`@Jukiii-claude` へのメンション、`gh` は `Jukiii` で使う | `CLAUDE.md` |
| 4 | ブランチ・PR | `main` へ直接コミットしない。PRで取り込み、通常マージ。マージ後もブランチを削除しない | `CLAUDE.md` / ブランチ保護 |
| 5 | コミットメッセージ | `feat:` / `fix:` / `test:` / `refactor:` / `docs:` / `chore:` で始める | `CLAUDE.md` |
| 6 | CI | PRと `main` への push で、ビルド・テスト・依存の脆弱性チェックを実行。ジョブ名は `check` | `.github/workflows/ci.yml` |
| 7 | 依存の自動更新 | Dependabot（NuGet・GitHub Actions）。毎週月曜。マイナー・パッチは1つにまとめる | `.github/dependabot.yml` |
| 8 | PRテンプレート | NOLITOと同じ見出し構成 | `.github/PULL_REQUEST_TEMPLATE.md` |
| 9 | Issueテンプレート | NOLITOと同じ3種類（Phaseタスク・質問・依頼） | `.github/ISSUE_TEMPLATE/`（先行して導入済み） |
| 10 | 決定ログ | `docs/decisions/` に、番号付きで記録する | このファイル |
| 11 | GitHubのブランチ保護（`main`） | PR必須（承認数0）、ステータスチェック `check` 必須、管理者にも適用、強制プッシュ・削除の禁止 | GitHub設定（`gh api`） |

## このリポジトリ固有の置き換え

- テスト・ビルド: NOLITOの `npm run check` / `npm run audit` に相当するものとして、`dotnet build` / `dotnet test` / `dotnet list package --vulnerable` を使う。
- CIの実行環境: WPFアプリ（`net10.0-windows`）のため、`ubuntu-latest` ではなく `windows-latest`。
- フェーズ: NOLITOは `docs/` の各フェーズ仕様書だが、このリポジトリは `docs/仕様.md` 67章（Phase 1〜10）。フェーズ1〜10は実装済みなので、以降の作業は Issue 単位で、ブランチは `phase-NN-<name>` または `fix-` / `feat-` / `chore-` / `docs-<name>`。
- `.gitattributes` は、Windows向けのため `* text=auto` のまま（NOLITOの `eol=lf` は適用しない）。

## 過去の運用との違い（移行）

- 2026-10-01までは、`main` へ直接マージ・プッシュしていた。以降は、PR経由にする。
- 2026-10-01までは、Issueの題名を【依頼】で統一していた。以降は、運営者の作業が要るかどうかで【要対応】【対応不要】【保留】を使い、【依頼】は運営者が書いたものにだけ使う。
- 以前の「Issue・PRは削除せずクローズする」「ブランチを削除しない」は、そのまま維持する。

## 未確定

- なし
