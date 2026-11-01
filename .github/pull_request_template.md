## 概要

<!-- 何をなぜ変えたか -->

関連 Issue: <!-- 例) closes #123 -->

## チェックリスト

- [ ] `dotnet test` が通る
- [ ] 第三者のフィクスチャ(アセット、.meta、YAML のダンプ)を含めていない
- [ ] CLI オプション・MCP ツール・ref_kind・スキーマを変えた場合、`docs/manual/` を更新した
- [ ] スキーマを変えた場合、`PRAGMA user_version` を上げた(差分更新とフル再構築の結果が一致する)
- [ ] NuGet 依存を変えた場合、`scripts/generate-third-party-notices.sh` を実行して `THIRD-PARTY-NOTICES.md` をコミットした
