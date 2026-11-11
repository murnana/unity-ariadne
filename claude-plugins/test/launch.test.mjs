import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, test } from 'node:test';

import { ProjectDataStore } from '../scripts/lib/store.mjs';
import { ensureDatabase, parseArgs, retentionDays } from '../scripts/launch.mjs';

const LAUNCH = new URL('../scripts/launch.mjs', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1');
const DAY = 24 * 60 * 60 * 1000;
// The fake ariadne is a shell script.
const posix = process.platform !== 'win32';

describe('retentionDays', () => {
  test('defaults to 30 and accepts whole days', () => {
    assert.equal(retentionDays({}), 30);
    assert.equal(retentionDays({ ARIADNE_RETENTION_DAYS: '' }), 30);
    assert.equal(retentionDays({ ARIADNE_RETENTION_DAYS: '7' }), 7);
  });

  test('rejects anything else', () => {
    for (const bad of ['0', '-1', '1.5', 'abc']) {
      assert.throws(() => retentionDays({ ARIADNE_RETENTION_DAYS: bad }), /ARIADNE_RETENTION_DAYS/);
    }
  });
});

describe('parseArgs', () => {
  test('reads the flags', () => {
    assert.deepEqual(parseArgs([]), { update: false, dataDir: undefined, project: undefined });
    assert.deepEqual(parseArgs(['--update', '--data-dir', '/d', '--project', '/p']), {
      update: true,
      dataDir: '/d',
      project: '/p',
    });
  });

  test('rejects unknown flags and a flag without its value', () => {
    assert.throws(() => parseArgs(['--nope']), /unexpected argument '--nope'/);
    assert.throws(() => parseArgs(['--project']), /unexpected argument '--project'/);
  });
});

describe('launch.mjs with a fake ariadne', { skip: !posix }, () => {
  let root;
  let project;
  let dataDir;
  let log;
  let fake;

  // Stand-in for ariadne: logs its arguments; `analyze` writes the --db file (unless told to
  // fail), `serve` prints one line on stdout and exits with the requested code.
  beforeEach(() => {
    root = fs.mkdtempSync(path.join(os.tmpdir(), 'ariadne-launch-'));
    project = path.join(root, 'MyGame');
    dataDir = path.join(root, 'data');
    log = path.join(root, 'calls.log');
    fake = path.join(root, 'fake-ariadne');
    fs.mkdirSync(path.join(project, 'Assets'), { recursive: true });
    fs.writeFileSync(
      fake,
      `#!/bin/sh
echo "$@" >> "${log}"
case "$1" in
  analyze) echo "analyze summary on stdout"
           [ -n "$FAKE_ANALYZE_FAIL" ] && exit 3
           db="$4"; echo graph > "$db" ;;
  serve)   echo "MCP-STDOUT"; exit "\${FAKE_SERVE_EXIT:-0}" ;;
esac
`,
      { mode: 0o755 },
    );
  });
  afterEach(() => fs.rmSync(root, { recursive: true, force: true }));

  const run = (extraEnv = {}, projectDir = project) =>
    spawnSync(process.execPath, [LAUNCH], {
      encoding: 'utf8',
      env: {
        ...process.env,
        CLAUDE_PLUGIN_DATA: dataDir,
        CLAUDE_PROJECT_DIR: projectDir,
        ARIADNE_BIN: fake,
        ...extraEnv,
      },
    });
  const calls = () => (fs.existsSync(log) ? fs.readFileSync(log, 'utf8').trim().split('\n') : []);

  test('first start: analyzes into the per-project folder, then serves it; stdout stays the server\'s', () => {
    const r = run();
    const entries = new ProjectDataStore(dataDir).entries();
    const [key] = Object.keys(entries);
    const db = path.join(dataDir, key, 'graph.db');

    assert.equal(r.status, 0, r.stderr);
    assert.equal(r.stdout, 'MCP-STDOUT\n'); // the analyze summary did not leak into the MCP stream
    assert.match(r.stderr, /analyze summary on stdout/);
    assert.deepEqual(calls(), [`analyze ${project} --db ${db}.building`, `serve --db ${db}`]);
    assert.ok(fs.existsSync(db));
    assert.ok(!fs.existsSync(`${db}.building`));
    assert.equal(entries[key].name, 'MyGame');
  });

  test('later start: serves the existing database without analyzing again', () => {
    run();
    fs.rmSync(log);
    const r = run();

    assert.equal(r.status, 0, r.stderr);
    assert.deepEqual(calls().map((c) => c.split(' ')[0]), ['serve']);
  });

  test('a failed analyze leaves no database behind and fails the start', () => {
    const r = run({ FAKE_ANALYZE_FAIL: '1' });
    const [key] = Object.keys(new ProjectDataStore(dataDir).entries());

    assert.notEqual(r.status, 0);
    assert.match(r.stderr, /analyze failed \(exit 3\)/);
    assert.deepEqual(fs.readdirSync(path.join(dataDir, key)), []);
    assert.deepEqual(calls().map((c) => c.split(' ')[0]), ['analyze']); // serve never started
  });

  test('--update builds a missing database and exits without serving', () => {
    const r = spawnSync(process.execPath, [LAUNCH, '--update'], {
      encoding: 'utf8',
      env: { ...process.env, CLAUDE_PLUGIN_DATA: dataDir, CLAUDE_PROJECT_DIR: project, ARIADNE_BIN: fake },
    });
    const [key] = Object.keys(new ProjectDataStore(dataDir).entries());
    const db = path.join(dataDir, key, 'graph.db');

    assert.equal(r.status, 0, r.stderr);
    assert.deepEqual(calls(), [`analyze ${project} --db ${db}.building`]);
    assert.ok(fs.existsSync(db));
    assert.match(r.stderr, /database ready/);
  });

  test('--update refreshes an existing database in place and exits with the analyze status', () => {
    run(); // first start builds it
    fs.rmSync(log);
    const env = { ...process.env, CLAUDE_PLUGIN_DATA: dataDir, CLAUDE_PROJECT_DIR: project, ARIADNE_BIN: fake };
    const [key] = Object.keys(new ProjectDataStore(dataDir).entries());
    const db = path.join(dataDir, key, 'graph.db');

    const r = spawnSync(process.execPath, [LAUNCH, '--update'], { encoding: 'utf8', env });
    assert.equal(r.status, 0, r.stderr);
    assert.deepEqual(calls(), [`analyze ${project} --db ${db}`]); // in place, no .building, no serve

    const failing = spawnSync(process.execPath, [LAUNCH, '--update'], { encoding: 'utf8', env: { ...env, FAKE_ANALYZE_FAIL: '1' } });
    assert.equal(failing.status, 3);
    assert.ok(fs.existsSync(db)); // a failed refresh keeps the old database
  });

  test('passes the exit code of the server through', () => {
    assert.equal(run({ FAKE_SERVE_EXIT: '5' }).status, 5);
  });

  test('prunes projects that were not used for the retention period', () => {
    const stale = new ProjectDataStore(dataDir, { now: () => Date.now() - 40 * DAY });
    const oldDb = stale.resolve(path.join(root, 'OldGame'));
    fs.writeFileSync(oldDb, 'x');

    const r = run();

    assert.equal(r.status, 0, r.stderr);
    assert.ok(!fs.existsSync(path.dirname(oldDb)));
    assert.match(r.stderr, /removed data of a project unused for 30 days/);
    assert.deepEqual(Object.values(new ProjectDataStore(dataDir).entries()).map((e) => e.name), ['MyGame']);
  });

  test('ARIADNE_RETENTION_DAYS changes the period', () => {
    const stale = new ProjectDataStore(dataDir, { now: () => Date.now() - 10 * DAY });
    const oldDb = stale.resolve(path.join(root, 'OldGame'));

    assert.equal(run().status, 0);
    assert.ok(fs.existsSync(path.dirname(oldDb))); // 10 days < default 30

    assert.equal(run({ ARIADNE_RETENTION_DAYS: '7' }).status, 0);
    assert.ok(!fs.existsSync(path.dirname(oldDb)));
  });

  test('refuses a folder that is not a Unity project, and registers nothing', () => {
    const notUnity = path.join(root, 'Docs');
    fs.mkdirSync(notUnity);
    const r = run({}, notUnity);

    assert.notEqual(r.status, 0);
    assert.match(r.stderr, /does not look like a Unity project/);
    assert.ok(!fs.existsSync(dataDir));
  });

  test('refuses to run without a data directory', () => {
    const r = run({ CLAUDE_PLUGIN_DATA: '' });
    assert.notEqual(r.status, 0);
    assert.match(r.stderr, /no data directory/);
  });

  test('--data-dir and --project win over the environment (as the update-graph skill passes them)', () => {
    const elsewhere = path.join(root, 'elsewhere');
    const r = spawnSync(process.execPath, [LAUNCH, '--update', '--data-dir', dataDir, '--project', project], {
      encoding: 'utf8',
      env: { ...process.env, CLAUDE_PLUGIN_DATA: elsewhere, CLAUDE_PROJECT_DIR: path.join(root, 'Nope'), ARIADNE_BIN: fake },
    });

    assert.equal(r.status, 0, r.stderr);
    assert.deepEqual(Object.values(new ProjectDataStore(dataDir).entries()).map((e) => e.name), ['MyGame']);
    assert.ok(!fs.existsSync(elsewhere));
  });

  test('rejects an unknown argument', () => {
    const r = spawnSync(process.execPath, [LAUNCH, '--bogus'], { encoding: 'utf8', env: process.env });
    assert.notEqual(r.status, 0);
    assert.match(r.stderr, /unexpected argument '--bogus'/);
  });
});

describe('ensureDatabase', () => {
  test('does nothing when the database exists', () => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ariadne-ensure-'));
    const db = path.join(dir, 'graph.db');
    fs.writeFileSync(db, 'x');
    try {
      ensureDatabase({ ariadne: '/nonexistent/ariadne', projectDir: dir, dbPath: db });
      assert.equal(fs.readFileSync(db, 'utf8'), 'x');
    } finally {
      fs.rmSync(dir, { recursive: true, force: true });
    }
  });

  test('reports a missing ariadne clearly', () => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ariadne-ensure-'));
    try {
      assert.throws(
        () => ensureDatabase({ ariadne: path.join(dir, 'nope'), projectDir: dir, dbPath: path.join(dir, 'graph.db') }),
        /cannot run/,
      );
    } finally {
      fs.rmSync(dir, { recursive: true, force: true });
    }
  });
});
