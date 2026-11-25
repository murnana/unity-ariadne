# Implementation internals

Non-obvious facts about Unity's file formats and about how the analyzer handles them. Read this before changing a parser or the update logic.

## Pipeline

| Stage | Code |
|---|---|
| GUID dictionary (.meta scan, asset type classification) | `src/Analyzer/Scanning/` — `MetaFileInfo`, `AssetTypeClassifier`, `UnityProjectScanner` → `GuidDictionary` |
| .meta refs (importer external objects, scripted importers) | `Scanning/MetaReferenceExtractor` |
| Unity YAML (scenes, prefabs, assets) | `src/Analyzer/Yaml/` — `UnityYamlReader`, `ReferenceExtractor`, `RefKindClassifier` |
| Regex oracle for differential tests (never delete) | `Yaml/RegexRefOracle` |
| ProjectSettings | `Settings/ProjectSettingsScanner` |
| JSON-embedded refs (.shadergraph, .vfx scalars) | `Json/JsonRefScanner` |
| Code layer (Roslyn syntax only) | `Code/CSharpSourceParser`, `Code/CodeScanner`, `Code/AddressableGroupScanner` |
| Full build / incremental update | `Storage/GraphBuilder`, `Storage/GraphUpdater`, `Storage/ContentHasher` |
| Schema, writer, queries, export | `src/Graph/` — `GraphDatabase`, `GraphWriter`, `GraphQueries`, `MemoryJsonlExporter` |
| Whole-graph tools (`expanded_count`, `unreachable`) | `src/Graph/ExpandedCount.cs`, `src/Graph/Unreachable.cs` |
| Ontology search (MCP `search` + CLI `search`) | `src/Graph/GraphSearch.cs` (query model, start ranking, relaxation, evidence), `Ontology.cs` (relation meanings, type categories, folder-convention notes) |
| MCP server | `Mcp/GraphMcpTools`, `Mcp/GraphFreshness` (`meta`) + `serve` in `Program.cs` |

## Scanning and .meta files

- Unity 6 **omits the importer section** from .meta files whose settings are all default — classify by extension first, importer as fallback.
- Unity does not import path segments starting with `.` or ending with `~` (`Samples~`, `Documentation~`). The scanner skips them; otherwise package samples collide with their imported copies under `Assets/Samples/`.
- `.asset` type: NativeFormatImporter `mainObjectFileID` 11400000 → ScriptableObject, 15600000 → TerrainData.
- .meta files are standard single-document YAML and go through YamlDotNet whole. Exception: PluginImporter `platformData` has an empty block-mapping key (`: Any`) that makes `YamlStream.Load` throw — `MetaReferenceExtractor.ExtractFile` falls back to `RegexRefOracle` (refs get an empty property path).
- .meta refs hang off a pseudo-object row (`file_id = 0, class_id = 0`; real anchors are never 0), created only when the .meta has refs.
- Asset paths contain spaces, `&` and non-ASCII characters — handle paths Unicode-safely and NUL-separate them in shell pipelines.

## Unity YAML

- Document headers are parsed line-based (`--- !u!<classId> &<fileID>[ stripped]`, fileID can be negative); only each document **body** goes through YamlDotNet. This sidesteps the `%TAG` directive, the invalid `stripped` keyword, and anchors repeated across documents.
- Detect Unity YAML by sniffing the `%YAML` header, not by extension (`.vfx`/`.mixer`/`.mesh`/`.terrainlayer` are YAML; `.shadergraph` is JSON). Files may be CRLF.
- A reference is a mapping whose keys ⊆ {fileID, guid, type} with fileID present; `{fileID: 0}` is null and skipped. Property paths are exact (`m_Modification.m_Modifications[3].target`) — classify by suffix.
- **Prefab variant detection needs file context**: a variant's root PrefabInstance and a scene-root PrefabInstance both have `m_TransformParent: {fileID: 0}`; only inside a .prefab is it a variant (→ `source_prefab`), everything else is `prefab_instance`.
- `m_CorrespondingSourceObject` has its own kind `corresponding_source` (it would swamp `generic`); excluded from export and queries.
- Bare-GUID shapes invisible to the mapping rule and to the oracle: Addressables group entries (`m_SerializeEntries[i]` with a 32-hex `m_GUID` **and** an `m_Address` sibling — the group's own `m_GUID` has no `m_Address`) and `m_AssetGUID` (AssetReference fields; also used by URP's ProbeVolumeStreamableAsset).

## ProjectSettings

- Ordinary Unity YAML. Only EditorBuildSettings `m_Scenes` entries (`{enabled, path, guid}`, plain-scalar guid) need dedicated extraction (fileID 0, path `m_Scenes[i]`); everything in EditorBuildSettings is `build_settings`.
- ProjectSettings assets have no GUID: `assets.guid` is the project-relative path, asset_type = source = `ProjectSettings`.
- Built-in GUIDs resolve to virtual paths with `source='BuiltIn'`: `…d000…` → `Library/unity default resources`, `…e000…` → `Library/unity editor resources`, `…f000…` → `Resources/unity_builtin_extra`.

## JSON-embedded references

- `.vfx` hides refs in JSON-in-YAML string scalars (`m_SerializableObject: '{"obj":{"fileID":…,"guid":"…"}}'`); some textures are referenced only this way. The scalar scan runs on every YAML string scalar.
- In shadergraphs **only two JSON keys carry asset GUIDs**: `guid` and `m_FunctionSource`. Other 32-hex values (`m_Id`, `m_ObjectId`) are graph-internal node ids. JSON nested in JSON strings uses multi-level escaping — `JsonRefScanner` matches the keys at any escape depth and captures a directly preceding `"fileID":n`.
- JSON-file refs share the .meta pseudo-object of their asset.

## Things that are never visible from source files

- `LightingData.asset` and `TerrainData.asset` are **always binary**, even with Force Text (scene ↔ lighting data, terrain layers).
- Some Unity dependencies exist only in import results: importer-assigned sub-asset scripts (.inputactions → InputActionAsset.cs, shadergraph → *Metadata.cs) and `.shader` name-string dependencies. These are documented in `docs/manual/limitations.md`.

## Code layer

- Code refs hang off a per-script MonoScript object (`file_id 11500000`, `class_id 115`, name = first non-nested declared type), created only for scripts with ≥1 resolved ref. Targets come from the GUID dictionary, so code refs never create unresolved assets — except `addressables_load`, which may target a dangling entry GUID (`EnsureTargetAsset`).
- Resolution is syntax-only and strict: simple-name index over declared types; unqualified names must match a non-nested type whose namespace is in scope (enclosing chain + usings + alias substitution); qualified names match by FullName suffix. ≥2 matching FullNames → ambiguous, skipped. Partial types get an edge per declaring file; same-file resolutions are skipped. DLL-only types produce no edge.
- Files parse with `UNITY_EDITOR` + cumulative `UNITY_*_OR_NEWER` symbols, so `#if !UNITY_EDITOR` branches are invisible.
- Serialized-field rule: public or `[SerializeField]`/`[SerializeReference]`, never static/const/readonly; everything else is `nonserialized_field`.
- `Resources.Load*` literals match any asset under any `Resources/` folder, extension stripped, case-insensitive (folders included). `Addressables.Load*/InstantiateAsync/DownloadDependenciesAsync` literal keys match address + label tables of every group **case-sensitively**; instance calls on AssetReference fields are skipped (already an asset-layer edge).
- Roslyn's `Microsoft.CodeAnalysis.RefKind` clashes with our `RefKind` — alias it.
- `code_symbols` (schema v3, keyword-search clues): every declared type of every .cs (even without refs or docs) plus members (method/constructor/property/indexer/field/event/enum member/operator) that carry an XML doc comment. Doc text = `<summary>` + `<remarks>` (the whole comment when it has neither), `<see cref>`/`<paramref name>`/langword replaced by their names, whitespace collapsed, capped at 1000 chars. Both `///` and `/** */` work because the default `DocumentationMode.Parse` structures doc trivia. The table belongs to the code layer: written by `WriteCodeRefs`, cleared by `DeleteCodeLayer`, so a doc-only edit (a .cs hash change) refreshes it with the rest of the layer.
- Readers (`serve`, `export`) must tolerate a pre-v3 db without `code_symbols` (`GraphDatabase.HasCodeSymbols`): the plugin serves an existing db until the next analyze rebuilds it.

## Ontology search

- One engine for both front ends: `GraphSearch.Run` takes a `SearchQuery` (the MCP tool's arguments, or the CLI's `--query` JSON — snake_case, `JsonPropertyName` on the records) and returns the JSON both print. The Claude Code skill holds no search logic: it only translates the question into a query.
- Starts with keywords are ranked (`GraphQueries.RankByKeywords`) and `Traverse` visits starts in that order, so a match's witness path comes from the most relevant start that reaches it; results keep discovery order.
- Relaxation is cumulative and only runs while the (unreferenced-filtered) result is empty: start `match_all` → any, start `path_contains` dropped, then steps' `path_contains` dropped → kinds dropped → `max_hops` ≥ 5. Direction is never flipped (that changes the meaning); the answer says so instead.
- Keyword and `path_contains` matching go through `TextMatch`: both sides are folded (NFKC → lower case → hiragana as katakana) and compared as substrings, plus one plural rule (`-y` also matches `-ie…`, so `enemy` finds `Enemies`). `Fold` keeps the NFKC length, so an index found in the folded doc is valid in the NFKC (display) doc used for excerpts. No tokenizer: inflections and synonyms are the caller's job.
- Explanations (relation meanings, folder notes, clue labels, relaxed/warning/hint messages) are English only — translating them is the calling agent's job. Asset names, keywords, clue targets and doc excerpts are user data: never translate them, and keep `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` so CJK/Hangul stay raw UTF-8 in the JSON.
- `Ontology.cs` is the fixed half of the ontology (relation meanings, type categories, folder notes); keep it in sync with `RefKind.cs` and `AssetTypeClassifier`.

## Database and incremental update

- SQLite connection pooling is off everywhere (`Pooling=false`): a pooled handle would resurrect a deleted db file after delete-and-recreate.
- The change hash covers exactly the bytes analysis reads: the .meta always, the body only for parse candidates (YAML-sniffed / .shadergraph / .shadersubgraph / .cs). Binary bodies are never read.
- The code layer is never updated per file (type resolution depends on the global type index; Resources/Addressables on global path/key sets). `GraphUpdater` reruns it wholesale when any .cs changed, the asset-path set changed, or an Addressables group changed. ProjectSettings is redone wholesale, keyed on `settings_files`.
- Removed-but-referenced GUIDs become Unresolved in place; Unresolved/BuiltIn rows without incoming refs are dropped. Renames update the row without re-analysis.
- **Invariant: update result == fresh full build, row for row** (`GraphUpdaterTests` compares rowid-independent dumps).
- A graph db with a different `user_version` makes `TryOpenForUpdate` return null → `analyze` rebuilds. Any other non-empty file (not SQLite, another application's db, locked or corrupt) is refused by `GraphDatabase.CanReplace`, with or without `--full`, because a rebuild deletes the file. "Graph db" means `user_version` > 0 plus the `assets`/`objects`/`refs` tables and both views: a schema change that drops or renames one of them must keep old databases recognised. `CreateWriter` is one transaction, so a failed update rolls back.

## Claude Code plugin launcher (not part of `guidgraph`)

- `claude-plugins/scripts/launch.mjs` (+ `lib/store.mjs`, Node ≥ 20.12, no dependencies) is the plugin's MCP `command`. `guidgraph serve --db` still only opens an existing file; location, first build and cleanup are plugin concerns, so the plugin works with any `guidgraph` that has `serve --db`.
- Layout: `${CLAUDE_PLUGIN_DATA}/<hash>/graph.db` + `index.json` (`hash → {name, path, lastUsed}`). Hash = SHA-256 of the normalized absolute path (trailing separators trimmed, lower-cased on Windows only), first 12 hex chars. The index, not the hash, is the source of truth: a registered path keeps its folder; a new path whose prefix is taken gets 16, 20, … chars. Tests force collisions through the injectable hash.
- Every read-modify-write of `index.json` holds `index.lock` (exclusive create, 10 s timeout, locks older than 30 s are stale) and commits via temp file + rename. A corrupt index is moved to `index.json.corrupt` and rebuilt.
- `prune` runs on every launch: drops entries with `lastUsed` older than `GUIDGRAPH_RETENTION_DAYS` (default 30) and hash-named folders missing from the index whose newest file is that old. Index keys are matched against `^[0-9a-f]{12,64}$` before being used as a path segment; other folders are never touched. A folder that cannot be deleted keeps its entry. Failures are warnings only.
- First start: `guidgraph analyze <project> --db <db>.building`, renamed on success (a failed run never leaves a db that a later start would serve). The analyze summary goes to stderr: stdout is the MCP transport, and `serve` inherits stdin/stdout directly. A 6-hourly timer refreshes `lastUsed` for long sessions. `launch.mjs --update` (for humans and the `update-graph` skill) builds or incrementally updates the db and exits without serving; `--data-dir`/`--project` override the environment because skill bash does not get `CLAUDE_PLUGIN_*` (only skill text is substituted) and `VAR=x cmd` does not work in PowerShell; a timed-out MCP start kills the launcher and its analyze, so big projects are pre-built this way.
- Tests: `cd claude-plugins && node --test` (CI runs it on all three OSes; the fake-`guidgraph` tests need a POSIX shell and skip on Windows).

## Export and MCP

- memory.jsonl: entities then relations, both sorted (identical graph ⇒ identical bytes); UTF-8 without BOM, `\n`, `UnsafeRelaxedJsonEscaping`. Relations carry a non-standard `ref_count` field, which the MCP memory server round-trips harmlessly. Default export keeps PackageCache assets as targets only; folders without edges are dropped.
- MCP: `ModelContextProtocol` + `Microsoft.Extensions.Hosting`; all logs go to stderr (stdout is the transport). Tools open the db **per call** (read-only) so concurrent calls are safe and an `analyze` rebuild is picked up.
- Name resolution: exact GUID → exact path → case-insensitive segment-boundary suffix; multiple hits are returned as candidates. LIKE wildcards in user input are escaped (`_` is common in asset paths). Lists cap at 200 + `truncated: true`.
- In-process MCP round-trip tests use two `System.IO.Pipelines.Pipe`s with the server's real DI wiring (`McpServerTests`).
- `placements` / `placement_removals` (schema v4) are derived wholesale from `refs`/`objects` by `GraphWriter.RebuildPlacements` at the end of every analyze and update, so an update equals a full rebuild by construction (`GraphUpdaterTests.Dump` includes both tables). `expanded_count` (`ExpandedCounter`) multiplies `contain` edges level by level (`max_depth` bounds it, no cycle bookkeeping) and subtracts the removals it can resolve. Removal targets inside nested prefabs/models carry Unity-derived fileIDs that are not rows of `objects` (not a plain XOR with the instance id; the sample has almost only these), so they are counted as `unresolved_removals`, never guessed.
