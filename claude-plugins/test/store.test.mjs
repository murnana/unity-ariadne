import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, test } from 'node:test';

import { INDEX_FILE, ProjectDataStore, normalizePath } from '../scripts/lib/store.mjs';

const DAY = 24 * 60 * 60 * 1000;

describe('ProjectDataStore', () => {
  let root;
  let dataDir;
  let clock;
  const store = (opts = {}) => new ProjectDataStore(dataDir, { now: () => clock.t, ...opts });

  beforeEach(() => {
    root = fs.mkdtempSync(path.join(os.tmpdir(), 'ariadne-store-'));
    dataDir = path.join(root, 'data');
    clock = { t: Date.UTC(2026, 0, 1) };
  });
  afterEach(() => fs.rmSync(root, { recursive: true, force: true }));

  const project = (name) => path.join(root, name);
  const folderOf = (db) => path.basename(path.dirname(db));

  test('puts the database in a hash folder of the data dir', () => {
    const db = store().resolve(project('MyGame'));

    assert.equal(path.basename(db), 'graph.db');
    assert.match(folderOf(db), /^[0-9a-f]{12}$/);
    assert.equal(path.dirname(path.dirname(db)), dataDir);
    assert.ok(fs.existsSync(path.dirname(db)));
  });

  test('is stable for a project and differs between projects', () => {
    const s = store();
    const a = s.resolve(project('A'));
    assert.equal(s.resolve(project('A')), a);
    assert.equal(store().resolve(project('A')), a);
    assert.notEqual(s.resolve(project('B')), a);
  });

  test('ignores trailing separators and dot segments', () => {
    const s = store();
    const expected = s.resolve(project('MyGame'));
    assert.equal(s.resolve(project('MyGame') + path.sep), expected);
    assert.equal(s.resolve(path.join(root, 'Other', '..', 'MyGame')), expected);
    assert.equal(normalizePath(project('MyGame') + path.sep), project('MyGame'));
  });

  test('records name and path in the index file', () => {
    const s = store();
    const key = folderOf(s.resolve(project('MyGame')));

    assert.deepEqual(s.entries(), {
      [key]: { name: 'MyGame', path: project('MyGame'), lastUsed: new Date(clock.t).toISOString() },
    });
    const onDisk = JSON.parse(fs.readFileSync(path.join(dataDir, INDEX_FILE), 'utf8'));
    assert.equal(onDisk.entries[key].name, 'MyGame');
  });

  test('refreshes lastUsed on every resolve', () => {
    const s = store();
    s.resolve(project('A'));
    clock.t += 3 * DAY;
    s.resolve(project('A'));
    assert.equal(Object.values(s.entries())[0].lastUsed, new Date(clock.t).toISOString());
  });

  test('a prefix collision gives the later project a longer name, and each keeps its own', () => {
    const s = store({ hash: () => 'a'.repeat(64) });
    const a = s.resolve(project('A'));
    const b = s.resolve(project('B'));

    assert.equal(folderOf(a).length, 12);
    assert.equal(folderOf(b).length, 16);
    assert.equal(s.resolve(project('A')), a);
    assert.equal(s.resolve(project('B')), b);
    assert.equal(Object.keys(s.entries()).length, 2);
  });

  test('a project keeps its folder when a shorter name becomes free again', () => {
    const s = store({ hash: () => 'a'.repeat(64) });
    s.resolve(project('A'));
    clock.t += 20 * DAY;
    const b = s.resolve(project('B'));

    s.prune(15 * DAY); // A is gone, its 12-character name is free

    assert.equal(s.resolve(project('B')), b);
    assert.equal(Object.keys(s.entries()).length, 1);
  });

  test('prune removes projects unused for longer than the retention, keeps the rest', () => {
    const s = store();
    const old = s.resolve(project('Old'));
    clock.t += 25 * DAY;
    const recent = s.resolve(project('Recent'));
    fs.writeFileSync(old, 'x');
    fs.writeFileSync(recent, 'x');
    clock.t += 10 * DAY; // Old: 35 days, Recent: 10 days

    assert.deepEqual(s.prune(30 * DAY), [folderOf(old)]);
    assert.ok(!fs.existsSync(path.dirname(old)));
    assert.ok(fs.existsSync(recent));
    assert.deepEqual(Object.values(s.entries()).map((e) => e.name), ['Recent']);
  });

  test('prune keeps everything within the retention', () => {
    const s = store();
    const db = s.resolve(project('A'));
    fs.writeFileSync(db, 'x');
    clock.t += 29 * DAY;
    assert.deepEqual(s.prune(30 * DAY), []);
    assert.ok(fs.existsSync(db));
  });

  test('prune removes old hash folders the index does not know, and touches nothing else', () => {
    clock.t = Date.now(); // folder times come from the real file system
    const s = store();
    s.resolve(project('A'));
    const orphan = path.join(dataDir, '0123456789abcdef');
    const foreign = path.join(dataDir, 'notes');
    fs.mkdirSync(orphan);
    fs.mkdirSync(foreign);
    fs.writeFileSync(path.join(orphan, 'graph.db'), 'x');
    fs.writeFileSync(path.join(foreign, 'keep.txt'), 'x');

    assert.deepEqual(s.prune(30 * DAY), []); // recent orphan: kept
    assert.ok(fs.existsSync(orphan));

    clock.t += 60 * DAY;
    s.prune(30 * DAY);
    assert.ok(!fs.existsSync(orphan));
    assert.ok(fs.existsSync(path.join(foreign, 'keep.txt')));
  });

  test('prune never deletes outside the data dir, even with a tampered index', () => {
    const outside = path.join(root, 'outside');
    fs.mkdirSync(outside);
    fs.writeFileSync(path.join(outside, 'keep.txt'), 'x');
    fs.mkdirSync(dataDir);
    fs.writeFileSync(
      path.join(dataDir, INDEX_FILE),
      JSON.stringify({ version: 1, entries: { '../outside': { name: 'x', path: '/x', lastUsed: '2000-01-01T00:00:00Z' } } }),
    );

    const s = store();
    assert.deepEqual(s.prune(30 * DAY), []);
    assert.ok(fs.existsSync(path.join(outside, 'keep.txt')));
    assert.deepEqual(s.entries(), {});
  });

  test('recovers from a corrupt index and keeps the broken file', () => {
    fs.mkdirSync(dataDir);
    fs.writeFileSync(path.join(dataDir, INDEX_FILE), '{ not json');

    const s = store();
    const db = s.resolve(project('A'));

    assert.equal(s.resolve(project('A')), db);
    assert.equal(Object.keys(s.entries()).length, 1);
    assert.ok(fs.existsSync(path.join(dataDir, `${INDEX_FILE}.corrupt`)));
  });

  test('replaces a stale lock left by a crashed process', () => {
    fs.mkdirSync(dataDir);
    const lock = path.join(dataDir, 'index.lock');
    fs.writeFileSync(lock, '99999');
    const old = new Date(Date.now() - 60_000);
    fs.utimesSync(lock, old, old);

    store().resolve(project('A'));
    assert.ok(!fs.existsSync(lock));
  });

  test('concurrent processes do not lose entries', async () => {
    const script = `
      import { ProjectDataStore } from ${JSON.stringify(new URL('../scripts/lib/store.mjs', import.meta.url).href)};
      new ProjectDataStore(process.argv[1]).resolve(process.argv[2]);`;
    const { spawn } = await import('node:child_process');
    const count = 12;
    await Promise.all(
      Array.from({ length: count }, (_, i) =>
        new Promise((resolve, reject) => {
          const child = spawn(process.execPath, ['--input-type=module', '-e', script, dataDir, project(`P${i}`)], { stdio: 'inherit' });
          child.on('exit', (code) => (code === 0 ? resolve() : reject(new Error(`child ${i} exited ${code}`))));
        }),
      ),
    );

    const names = Object.values(store().entries()).map((e) => e.name).sort();
    assert.equal(names.length, count);
    assert.equal(new Set(names).size, count);
  });
});
