---
name: search-graph
description: Unity プロジェクトの依存関係グラフを、unity-ariadne の search ツールでオントロジー検索します。アセットの種類と参照の種類に加え、ファイル名・フォルダー名・C# の XML ドキュメントコメントも手がかりにして、「このスクリプトを継承したクラスを使っているプレハブは?」「ボスの行動に関係するスクリプトが付いたプレハブは?」「UI フォルダーで使われていないプレハブは?」のような質問に、根拠つきで答えます。
when_to_use: Unity のアセットやスクリプトの関係を問う質問全般。とくに「〜を使っている〜」「〜に含まれる〜」「〜を継承した〜」「〜経由で」「未使用の〜」「〜に関係する〜」「〜フォルダーの〜」「どのシーン/プレハブ/マテリアルが」のように、種類・関係・名前・意味で絞り込む必要があるとき。単にファイルの中身やコードのシンボルを探すだけなら grep のほうが速いので使いません。
---

# オントロジー検索

検索そのもの(候補探し、型付きの経路探索、0 件のときの条件の緩和、根拠の付与)は unity-ariadne の MCP ツール **`search`** が行います。このスキルの仕事は、**質問を `search` のクエリに直して 1 回呼び、返ってきた根拠を添えて答える**ことだけです。

## 1. 質問をクエリに直す

```text
{
  "from":  {"asset": "Enemy.cs"}                       // 特定のアセットから
       or  {"keywords": ["ボス", "boss"], "types": ["Script"], "path_contains": ["/UI/"], "source": "Assets"},
  "steps": [{"direction": "in", "kinds": ["inherits"], "types": ["Script"], "max_hops": 10,
             "include_self": true, "path_contains": ["/Enemies/"]}, ...],
  "unreferenced": true                                  // 「使われていない」とき
}
```

- **`from`**: ファイル名が分かっていれば `asset`。概念(「ボス」「ダメージ」)で言われていれば `keywords` に、**質問の言語と英語の両方**を入れます(識別子は英語、ドキュメントコメントは日本語のことが多いため)。全角・半角、ひらがな・カタカナ、大文字・小文字の違いは ariadne が吸収するので、表記ゆれのために同じ語を何通りも入れる必要はありません。活用(「攻撃した」)は吸収しないので語幹(「攻撃」)を、言い換え(「敵」と「エネミー」)は両方を入れます。種類は `types`、場所(「UI フォルダーの」)は `path_contains`
- **`steps`**: 関係の連鎖を順に並べます。向きは「**参照する側 → 参照される側**」が `out` で、「X を使っている Y」は X から見て `in` です。継承やプレハブの入れ子のように何段もたどる関係は `max_hops` を大きく、「そのクラス自身も含めて」は `include_self: true`。ステップが要らない質問(「ボスに関係するスクリプトは?」)では省略します
- 型名・関係名が分からないときだけ、先に `describe_ontology`(種類と関係の一覧と意味)を呼びます。場所の言い方がプロジェクトの実際のフォルダー名と合っているか怪しいときは `list_folders` で確かめます

| 質問 | クエリ |
|---|---|
| `Enemy.cs` かそのサブクラスが付いたプレハブは? | `from: {asset: "Enemy.cs"}`, `steps: [{direction: "in", kinds: ["inherits"], max_hops: 10, include_self: true}, {direction: "in", kinds: ["uses_script"], types: ["Prefab"]}]` |
| ボスの行動に関係するスクリプトが付いたプレハブは? | `from: {keywords: ["ボス", "boss"], types: ["Script"]}` と同じ 2 ステップ |
| `Hero.prefab` が置かれているシーンは? | `from: {asset: "Hero.prefab"}`, `steps: [{direction: "in", kinds: ["prefab_instance", "source_prefab"], types: ["Scene"], max_hops: 10}]` |
| UI フォルダーのプレハブが使っているスクリプトは? | `from: {types: ["Prefab"], path_contains: ["/UI/"]}`, `steps: [{direction: "out", kinds: ["uses_script"]}]` |
| 使われていないプレハブは? | `from: {types: ["Prefab"], source: "Assets"}`, `unreferenced: true` |
| ダメージを扱うコードは? | `from: {keywords: ["ダメージ", "damage"], types: ["Script"]}` |

## 2. `search` を呼ぶ

クエリをそのまま `search` に渡します。エラーが返ったら `hint` に従って直します(`ambiguous` なら `candidates` からフルパスを選ぶ、`not_found` なら `from.keywords` で探し直す)。結果が多すぎる(`truncated`)ときは `types` や `path_contains` を足して呼び直します。

## 3. 根拠を添えて答える

`relations` / `notes` / `relaxed` / `warnings` / `hint` などの説明文は英語で返ります。ユーザーの言語に直して伝えてください。アセット名・ドキュメントコメントの抜粋・手がかりの語は、日本語・韓国語・中国語などのまま返るので、**訳さずそのまま**引用します。

返ってきた各 `matches` を列挙し、次の根拠を**種類を区別して**添えます。

- **参照の根拠** `path`: `A -[kind]-> B` は A が B を参照、`A <-[kind]- B` は B が A を参照。関係の意味は応答の `relations` に(英語で)書いてあるので、それをユーザーの言語で説明します
- **名前・意味の根拠** `clues` / `doc`(経路の出発点についてのものは `from_clues` / `from_doc`): 「ドキュメントコメントに『ボスの行動を制御する』とあるため」のように、命名やコメントからの推定であることが分かる書き方にします
- **注意書き** `notes`: Editor 専用、Resources で名前ロードされうる、パッケージ由来、などの注記をそのまま伝えます。とくに `unreferenced` の結果に Resources の注記があれば、「参照が無くても使われている可能性がある」と必ず添えます
- **`relaxed`** があれば、条件をどう緩めて見つけたかを伝えます。**`warnings`** があれば、型名・関係名の誤りやグラフが古いことを伝えます

未使用の判定はグラフに見えている参照だけが根拠なので、「削除してよい」とは断言しません。結果が実際と合わない、ドキュメントコメントが検索されていないという `warnings` が出たときは、`update-graph` スキルでグラフを更新してから呼び直します。
