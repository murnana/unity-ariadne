# Unity Ariadne

A CLI tool that analyzes a Unity project without the Unity Editor, builds a dependency graph in SQLite, and exports memory.jsonl / serves it over MCP. User manuals (Japanese) live in `docs/manual/`; implementation facts live in `docs/development/internals.md` — read it before changing a parser or the update logic. Open work is tracked in GitHub issues.

## Commands

```sh
dotnet build
dotnet test                                    # fixture tests skip unless ARIADNE_FIXTURES is set
ARIADNE_FIXTURES=/path/to/fixtures dotnet test # full suite, see docs/development/fixtures.md
dotnet run --project src/Analyzer -- analyze <unity-project-path> --db graph.db   # existing db → incremental update; --full rebuilds
dotnet run --project src/Analyzer -- export --db graph.db --jsonl out.jsonl       # --include-packages for PackageCache nodes+edges
dotnet run --project src/Analyzer -- report --db graph.db                         # unresolved GUIDs
dotnet run --project src/Analyzer -- serve --db graph.db                          # MCP server (stdio)
```

If `dotnet` is not on PATH: `export PATH="$HOME/.dotnet:$PATH"` (.NET 10).

## Project layout

- `src/Graph` — SQLite schema, writer, queries, jsonl exporter (Microsoft.Data.Sqlite)
- `src/Analyzer` — CLI, scanners and parsers (YamlDotNet, Roslyn), MCP server. References Graph
- `tests/UnityAriadne.Tests` — xUnit. References both projects
- `installer/` — MSI (WiX) / pkg builders used by `.github/workflows/release.yml`
- `docs/manual/` — user manuals; keep in sync when CLI options, MCP tools, ref_kinds or the schema change
- `docs/development/` — internals, fixtures, benchmark, release process

## Constraints

- **Do not use the Unity Editor API. Parse files directly.**
- **No Unity fixture content in this repository.** Test fixtures are third-party (Asset Store) content provided externally via `ARIADNE_FIXTURES`; tests that need them use `[FixtureFact]` / `[FixtureTheory]`. Never commit fixture files themselves (assets, .meta, YAML dumps); referring to fixture paths/GUIDs inside gated tests is fine.
- Fixture YAML is real Unity Editor output — **never generate or edit it**. Small inline YAML snippets in unit tests are fine.
- Do not remove the differential tests (`RegexRefOracle` vs. full parser) — they are the regression net.
- Schema changes bump `PRAGMA user_version`; the incremental update must stay equal to a full rebuild row for row.

## Key facts about Unity YAML

- Header: `%YAML 1.1` + `%TAG !u! tag:unity3d.com,2011:`
- Document separator: `--- !u!<classId> &<fileID>`; the `stripped` suffix is invalid YAML — strip it in preprocessing and keep it as a flag
- One file can hold thousands of documents
- Reference shape `{fileID: n, guid: g, type: t}`; no guid = same-file reference; `{fileID: 0}` = null
- `m_Script: {fileID: 11500000, guid: …}` is a script reference (DLL scripts use a different fileID)
- PrefabInstance is classId **1001**; `m_SourcePrefab` → source prefab, `m_Modifications[*].target` → override target

## Schema and ref_kinds

See `docs/manual/schema.md` (tables `assets` / `objects` / `refs` / `settings_files`, views `asset_edges` / `unresolved_guids`, and the full ref_kind table). The classifier lives in `src/Analyzer/Yaml/RefKindClassifier.cs`, constants in `RefKind.cs`.

## Session workflow

One work item per session. Commit at each boundary once tests are green.

### Branching

- Always branch from `develop`; never start work from `main`.
- If `develop` does not exist yet (check `git ls-remote --heads origin develop`), create it from `main` and push it first, then branch from it.
- PRs target `develop`.
