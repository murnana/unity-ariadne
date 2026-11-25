// Launcher for the guidgraph MCP server (see .mcp.json).
//
// `guidgraph serve --db <path>` only serves an existing database. This script decides where that
// database lives (a per-project folder of the plugin data directory), builds it on the first
// start, cleans up data of projects that were not used for a while, then runs `guidgraph serve`.
// guidgraph itself knows nothing about any of this.
//
// `node launch.mjs --update [--data-dir <dir>] [--project <dir>]` is for people and for the
// update-graph skill: it builds the database (first time) or updates it
// incrementally, prints the analyze summary and exits without starting the server. Use it to
// pre-build a big project before the first Claude Code start, or to refresh after code changes.
//
// The data directory and the project come from --data-dir / --project, else from the environment
// Claude Code sets for the MCP server: CLAUDE_PLUGIN_DATA, CLAUDE_PROJECT_DIR.
// Optional: GUIDGRAPH_RETENTION_DAYS (default 30), GUIDGRAPH_BIN (path of the guidgraph executable).
//
// stdout belongs to the MCP transport: everything this script prints goes to stderr.

import { spawn, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

import { ProjectDataStore } from './lib/store.mjs';

const DEFAULT_RETENTION_DAYS = 30;
const DAY_MS = 24 * 60 * 60 * 1000;
const KEEP_ALIVE_MS = 6 * 60 * 60 * 1000;

function fail(message) {
  console.error(`guidgraph: ${message}`);
  process.exit(1);
}

export function retentionDays(env) {
  const raw = env.GUIDGRAPH_RETENTION_DAYS;
  if (raw === undefined || raw === '') return DEFAULT_RETENTION_DAYS;
  const days = Number(raw);
  if (!Number.isInteger(days) || days < 1) {
    throw new Error(`GUIDGRAPH_RETENTION_DAYS must be a whole number of days, 1 or more (got '${raw}')`);
  }
  return days;
}

/**
 * Builds the database with `guidgraph analyze` when it does not exist. The analysis goes to a
 * temporary file that is renamed on success, so a failed or interrupted run never leaves a
 * half-built `graph.db` that a later start would serve.
 */
export function ensureDatabase({ guidgraph, projectDir, dbPath }) {
  if (fs.existsSync(dbPath)) return;

  console.error(`guidgraph: building the graph of ${projectDir} (first start, this can take a while)`);
  const building = `${dbPath}.building`;
  fs.rmSync(building, { force: true });

  // analyze prints its summary on stdout, which must stay clean: send it to stderr.
  const result = spawnSync(guidgraph, ['analyze', projectDir, '--db', building], {
    stdio: ['ignore', process.stderr, 'inherit'],
  });
  if (result.error || result.status !== 0) {
    fs.rmSync(building, { force: true });
    throw new Error(result.error ? `cannot run ${guidgraph}: ${result.error.message}` : `analyze failed (exit ${result.status})`);
  }
  fs.renameSync(building, dbPath);
}

/** `--update`: build or incrementally update the database. Returns the process exit code. */
export function updateDatabase({ guidgraph, projectDir, dbPath }) {
  if (!fs.existsSync(dbPath)) {
    try {
      ensureDatabase({ guidgraph, projectDir, dbPath });
    } catch (e) {
      console.error(`guidgraph: ${e.message}`);
      return 1;
    }
    console.error(`guidgraph: database ready: ${dbPath}`);
    return 0;
  }

  // An existing database is updated in place (guidgraph analyze is incremental).
  const result = spawnSync(guidgraph, ['analyze', projectDir, '--db', dbPath], { stdio: 'inherit' });
  if (result.error) {
    console.error(`guidgraph: cannot run ${guidgraph}: ${result.error.message}`);
    return 1;
  }
  return result.status ?? 1;
}

/** `--update`, `--data-dir <dir>`, `--project <dir>`; anything else is an error. */
export function parseArgs(argv) {
  const options = { update: false, dataDir: undefined, project: undefined };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === '--update') {
      options.update = true;
    } else if ((arg === '--data-dir' || arg === '--project') && i + 1 < argv.length) {
      options[arg === '--data-dir' ? 'dataDir' : 'project'] = argv[++i];
    } else {
      throw new Error(`unexpected argument '${arg}'`);
    }
  }
  return options;
}

function main() {
  let options;
  try {
    options = parseArgs(process.argv.slice(2));
  } catch (e) {
    fail(`${e.message}\nusage: launch.mjs [--update] [--data-dir <dir>] [--project <dir>]`);
  }

  const dataDir = options.dataDir || process.env.CLAUDE_PLUGIN_DATA;
  const projectDir = options.project || process.env.CLAUDE_PROJECT_DIR || process.cwd();
  const guidgraph = process.env.GUIDGRAPH_BIN || 'guidgraph';

  if (!dataDir) fail('no data directory: pass --data-dir, or run this through Claude Code (CLAUDE_PLUGIN_DATA)');
  if (!fs.existsSync(path.join(projectDir, 'Assets'))) {
    fail(`'${projectDir}' does not look like a Unity project (no Assets/ directory); open Claude Code at the project root`);
  }

  let days;
  try {
    days = retentionDays(process.env);
  } catch (e) {
    fail(e.message);
  }

  const store = new ProjectDataStore(dataDir);
  let dbPath;
  try {
    dbPath = store.resolve(projectDir);
  } catch (e) {
    fail(`cannot use the data directory '${dataDir}': ${e.message}`);
  }

  // Housekeeping must never keep the server from starting.
  try {
    for (const removed of store.prune(days * DAY_MS)) {
      console.error(`guidgraph: removed data of a project unused for ${days} days (${removed})`);
    }
  } catch (e) {
    console.error(`guidgraph: warning: cleanup failed: ${e.message}`);
  }

  if (options.update) {
    process.exit(updateDatabase({ guidgraph, projectDir, dbPath }));
  }

  try {
    ensureDatabase({ guidgraph, projectDir, dbPath });
  } catch (e) {
    fail(e.message);
  }

  // A session can outlive the retention period: refresh the last-used time so another
  // session's cleanup does not delete the database this server is reading.
  const keepAlive = setInterval(() => {
    try {
      store.resolve(projectDir);
    } catch (e) {
      console.error(`guidgraph: warning: could not refresh project data: ${e.message}`);
    }
  }, KEEP_ALIVE_MS);
  keepAlive.unref();

  // The server inherits stdin/stdout, so the MCP stream does not pass through this process.
  const server = spawn(guidgraph, ['serve', '--db', dbPath, '--project', projectDir], { stdio: 'inherit' });
  server.on('error', (e) => fail(`cannot run ${guidgraph}: ${e.message}`));
  server.on('exit', (code, signal) => process.exit(code ?? (signal ? 1 : 0)));
  for (const signal of ['SIGINT', 'SIGTERM']) {
    process.on(signal, () => server.kill(signal));
  }
}

// Importable for tests; runs only when started directly.
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  main();
}
