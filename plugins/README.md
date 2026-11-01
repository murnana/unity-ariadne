# plugins

AIエージェント向けプラグインの置き場です。**AIごとにディレクトリを分けます**(将来、他のAI向けを `plugins/<名前>/` として並置します)。

| ディレクトリ | 対象 | 配布 |
|---|---|---|
| [claude-code/](claude-code/) | Claude Code | Release に `unity-ariadne-<version>-claude-plugin.zip` を添付。marketplace は別リポジトリ([murnana/murnana-claude-code-plugin](https://github.com/murnana/murnana-claude-code-plugin))が持ち、このzip(`archive` ソース)またはタグ(`git-subdir` ソース)を参照します |

どのプラグインも `ariadne` コマンド(MSI / pkg インストーラーが PATH に入れます)だけに依存します。.NET SDK は不要です。使い方は [docs/manual/claude-code-plugin.md](../docs/manual/claude-code-plugin.md) をご覧ください。
