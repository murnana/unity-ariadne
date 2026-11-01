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
| MCP server | `Mcp/GraphMcpTools` + `serve` in `Program.cs` |

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

## Database and incremental update

- SQLite connection pooling is off everywhere (`Pooling=false`): a pooled handle would resurrect a deleted db file after delete-and-recreate.
- The change hash covers exactly the bytes analysis reads: the .meta always, the body only for parse candidates (YAML-sniffed / .shadergraph / .shadersubgraph / .cs). Binary bodies are never read.
- The code layer is never updated per file (type resolution depends on the global type index; Resources/Addressables on global path/key sets). `GraphUpdater` reruns it wholesale when any .cs changed, the asset-path set changed, or an Addressables group changed. ProjectSettings is redone wholesale, keyed on `settings_files`.
- Removed-but-referenced GUIDs become Unresolved in place; Unresolved/BuiltIn rows without incoming refs are dropped. Renames update the row without re-analysis.
- **Invariant: update result == fresh full build, row for row** (`GraphUpdaterTests` compares rowid-independent dumps).
- A db with a different `user_version` (or not SQLite) makes `TryOpenForUpdate` return null → `analyze` rebuilds. `CreateWriter` is one transaction, so a failed update rolls back.

## Export and MCP

- memory.jsonl: entities then relations, both sorted (identical graph ⇒ identical bytes); UTF-8 without BOM, `\n`, `UnsafeRelaxedJsonEscaping`. Relations carry a non-standard `ref_count` field, which the MCP memory server round-trips harmlessly. Default export keeps PackageCache assets as targets only; folders without edges are dropped.
- MCP: `ModelContextProtocol` + `Microsoft.Extensions.Hosting`; all logs go to stderr (stdout is the transport). Tools open the db **per call** (read-only) so concurrent calls are safe and an `analyze` rebuild is picked up.
- Name resolution: exact GUID → exact path → case-insensitive segment-boundary suffix; multiple hits are returned as candidates. LIKE wildcards in user input are escaped (`_` is common in asset paths). Lists cap at 200 + `truncated: true`.
- In-process MCP round-trip tests use two `System.IO.Pipelines.Pipe`s with the server's real DI wiring (`McpServerTests`).
