---
name: update-graph
description: Unity プロジェクトの依存関係グラフ(graph.db)を最新の状態に更新します。Unity のアセットやスクリプトを追加・変更・削除・移動したあとに、unity-ariadne の MCP ツールで照会する前や、結果が古い・アセットが見つからないときに使います。
when_to_use: 「グラフを更新して」「DB を更新して」「依存関係を再解析して」「ariadne analyze を実行して」と頼まれたとき。コードやアセットを変更した直後に find_references / impact_analysis などの結果が実際と合わないとき。
---

# 依存関係グラフの更新

unity-ariadne のグラフ(SQLite の `graph.db`)を差分更新します。プラグインのデータディレクトリにあるこのプロジェクト専用の DB が対象で、DB が無ければ新しく作ります。MCP サーバーの再起動は要りません(ツールは呼び出しごとに DB を開き直します)。

## 手順

1. Bash で次のコマンドを実行します。`${CLAUDE_...}` は Claude Code が展開済みのパスなので、そのまま実行してください。パスに空白が入ることがあるため、引用符は外さないでください。

   ```sh
   node "${CLAUDE_PLUGIN_ROOT}/scripts/launch.mjs" --update --data-dir "${CLAUDE_PLUGIN_DATA}" --project "${CLAUDE_PROJECT_DIR}"
   ```

   - 差分更新なので、変更が少なければ数秒で終わります。DB が無いとき(初回)や大きなプロジェクトでは数分かかることがあります。Bash の `timeout` は長め(例: 600000 ミリ秒)にしてください
   - 他のプロジェクトの DB や、データディレクトリの中身には触れません

2. 出力の次の行を読み取り、結果を短く報告します。
   - `Incremental update: N added, N changed, N moved, N removed, N unchanged` → 差分更新の内訳
   - `database ready: <path>` → DB を新しく作った(初回)
   - `Code layer: re-analyzed (global resolution)` → .cs の変更で C# 由来の参照を全体で再解析した(正常です)

3. 更新後、ユーザーが調べたかったこと(参照元の確認や影響範囲など)があれば、unity-ariadne の MCP ツールで続けます。

## 失敗したとき

終了コードが 0 以外なら、出力のメッセージをそのまま伝え、原因に応じて次を案内します。

| メッセージ | 原因と対処 |
|---|---|
| `does not look like a Unity project (no Assets/ directory)` | Claude Code が Unity プロジェクトのルートで起動されていません。ルートで起動し直してもらいます |
| `cannot run ariadne` | `ariadne` が PATH にありません。インストーラーで入れ、ターミナルと Claude Code を再起動してもらいます |
| `analyze failed` | 解析エラーです。メッセージを共有し、再実行しても直らなければ報告してもらいます。失敗しても元の DB は残ります |
| `timed out waiting for ...index.lock` | 他のセッションが保存先の管理ファイルを使用中です。少し待って再実行します |

`node` が見つからない場合は、Node.js 20.12 以降のインストールが必要だと伝えてください。
