// Per-project database locations under one data directory (the Claude Code plugin data
// directory). Each project gets <dataDir>/<hash>/graph.db, where the hash comes from the
// project path, and <dataDir>/index.json records which project each hash belongs to.
// Projects that were not used for a while are pruned.
//
// Plain Node (no dependencies) so one script runs unchanged on macOS, Linux and Windows.

import { createHash } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

export const DATABASE_FILE = 'graph.db';
export const INDEX_FILE = 'index.json';
export const LOCK_FILE = 'index.lock';

const MIN_HASH_LENGTH = 12;
const MAX_HASH_LENGTH = 64;
const LOCK_TIMEOUT_MS = 10_000;
const LOCK_STALE_MS = 30_000;
// The only folder names this module creates; anything else in the data dir is left alone.
const HASH_FOLDER = /^[0-9a-f]{12,64}$/;
const isWindows = process.platform === 'win32';

export function sha256Hex(text) {
  return createHash('sha256').update(text, 'utf8').digest('hex');
}

/** Absolute path without trailing separators or `.` / `..` segments. */
export function normalizePath(p) {
  const full = path.resolve(p);
  const root = path.parse(full).root;
  return full.length > root.length ? full.replace(/[\\/]+$/, '') : full;
}

function samePath(a, b) {
  return isWindows ? a.toLowerCase() === b.toLowerCase() : a === b;
}

function sleep(ms) {
  Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);
}

export class ProjectDataStore {
  /**
   * @param {string} dataDir folder that holds the index and the per-project folders
   * @param {{now?: () => number, hash?: (s: string) => string}} [options] test seams
   */
  constructor(dataDir, { now = Date.now, hash = sha256Hex } = {}) {
    this.dataDir = path.resolve(dataDir);
    this.now = now;
    this.hash = hash;
  }

  /**
   * Returns the database path for a project and registers it in the index (or refreshes its
   * last-used time). Creates the project's folder.
   */
  resolve(projectPath) {
    const normalized = normalizePath(projectPath);

    const key = this.#withIndex((index) => {
      // A registered project keeps its folder even if a shorter name became free again.
      let found = Object.keys(index.entries).find((k) => samePath(index.entries[k].path, normalized));
      if (found === undefined) {
        found = this.#freeKey(index, this.hash(isWindows ? normalized.toLowerCase() : normalized));
      }
      index.entries[found] = {
        name: path.basename(normalized) || normalized,
        path: normalized,
        lastUsed: new Date(this.now()).toISOString(),
      };
      return found;
    });

    const dir = path.join(this.dataDir, key);
    fs.mkdirSync(dir, { recursive: true });
    return path.join(dir, DATABASE_FILE);
  }

  /**
   * Deletes projects unused for longer than `retentionMs`, plus hash-named folders the index does
   * not know (a lost index) whose newest file is that old. A folder that cannot be deleted keeps
   * its entry and is retried next time. Returns the removed folder names.
   */
  prune(retentionMs) {
    const cutoff = this.now() - retentionMs;
    const removed = [];

    this.#withIndex((index) => {
      for (const [key, entry] of Object.entries(index.entries)) {
        if (Date.parse(entry.lastUsed) >= cutoff) continue;

        // Never trust a key read from disk as a path segment.
        if (!HASH_FOLDER.test(key)) {
          delete index.entries[key];
          continue;
        }
        if (this.#deleteFolder(key)) {
          delete index.entries[key];
          removed.push(key);
        }
      }

      if (fs.existsSync(this.dataDir)) {
        for (const dirent of fs.readdirSync(this.dataDir, { withFileTypes: true })) {
          if (!dirent.isDirectory() || !HASH_FOLDER.test(dirent.name)) continue;
          if (Object.hasOwn(index.entries, dirent.name)) continue;
          if (latestWrite(path.join(this.dataDir, dirent.name)) >= cutoff) continue;
          if (this.#deleteFolder(dirent.name)) removed.push(dirent.name);
        }
      }
    });

    return removed;
  }

  /** Registered projects: `{ [hash]: { name, path, lastUsed } }`. */
  entries() {
    return this.#withIndex((index) => ({ ...index.entries }));
  }

  #freeKey(index, digest) {
    // Shortest prefix not taken by another project; a collision gives the later project a longer name.
    for (let length = MIN_HASH_LENGTH; length <= Math.min(MAX_HASH_LENGTH, digest.length); length += 4) {
      const candidate = digest.slice(0, length);
      if (!Object.hasOwn(index.entries, candidate)) return candidate;
    }
    throw new Error('could not find a free folder name for the project');
  }

  #deleteFolder(name) {
    const dir = path.join(this.dataDir, name);
    try {
      fs.rmSync(dir, { recursive: true, force: true });
      return true;
    } catch (e) {
      console.error(`warning: could not delete ${dir}: ${e.message}`);
      return false;
    }
  }

  // Several Claude Code sessions can start at once, so each read-modify-write of the index
  // happens under an exclusive lock file and is committed with an atomic rename.
  #withIndex(action) {
    fs.mkdirSync(this.dataDir, { recursive: true });
    const release = this.#acquireLock();
    try {
      const index = this.#readIndex();
      const result = action(index);
      this.#writeIndex(index);
      return result;
    } finally {
      release();
    }
  }

  #acquireLock() {
    const lockPath = path.join(this.dataDir, LOCK_FILE);
    const deadline = Date.now() + LOCK_TIMEOUT_MS;
    for (;;) {
      try {
        fs.writeFileSync(lockPath, String(process.pid), { flag: 'wx' });
        return () => fs.rmSync(lockPath, { force: true });
      } catch (e) {
        if (e.code !== 'EEXIST') throw e;
        // A crashed process can leave its lock behind; an old one is stale.
        try {
          if (Date.now() - fs.statSync(lockPath).mtimeMs > LOCK_STALE_MS) {
            fs.rmSync(lockPath, { force: true });
            continue;
          }
        } catch {
          continue; // released between the two calls
        }
        if (Date.now() >= deadline) throw new Error(`timed out waiting for ${lockPath}`);
        sleep(25);
      }
    }
  }

  #readIndex() {
    const indexPath = path.join(this.dataDir, INDEX_FILE);
    if (!fs.existsSync(indexPath)) return { version: 1, entries: {} };
    try {
      const parsed = JSON.parse(fs.readFileSync(indexPath, 'utf8'));
      if (parsed && typeof parsed === 'object' && parsed.entries && typeof parsed.entries === 'object') {
        return { version: 1, entries: Object.assign(Object.create(null), parsed.entries) };
      }
    } catch {
      // fall through
    }
    // Keep the broken file for inspection and start over. Folders that lose their entry this
    // way are cleaned up by prune() once they are old.
    fs.renameSync(indexPath, `${indexPath}.corrupt`);
    return { version: 1, entries: {} };
  }

  #writeIndex(index) {
    const indexPath = path.join(this.dataDir, INDEX_FILE);
    const temp = `${indexPath}.${process.pid}.tmp`;
    fs.writeFileSync(temp, `${JSON.stringify({ version: 1, entries: { ...index.entries } }, null, 2)}\n`);
    fs.renameSync(temp, indexPath);
  }
}

function latestWrite(dir) {
  let latest = fs.statSync(dir).mtimeMs;
  for (const dirent of fs.readdirSync(dir, { withFileTypes: true, recursive: true })) {
    if (!dirent.isFile()) continue;
    try {
      latest = Math.max(latest, fs.statSync(path.join(dirent.parentPath, dirent.name)).mtimeMs);
    } catch {
      // vanished while scanning
    }
  }
  return latest;
}
