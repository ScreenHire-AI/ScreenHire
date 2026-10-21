import { EventEmitter } from 'node:events';
import { existsSync, mkdtempSync, readFileSync, rmSync, utimesSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { acquireLock, stopChrome, withTimeout } from './lighthouse.mjs';

/** What chrome-launcher's launch() returns, with a child process that exits only when `exitsOnKill` says so. */
function fakeChrome({ exitsOnKill }) {
  const child = Object.assign(new EventEmitter(), { exitCode: null, signalCode: null });
  const exit = () => {
    child.exitCode = 1;
    child.emit('exit', 1, null);
  };
  return { pid: 4242, process: child, kill: vi.fn(() => exitsOnKill && exit()), exit };
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe('withTimeout', () => {
  it('settles with the promise when it is in time', async () => {
    await expect(withTimeout(Promise.resolve('done'), 1_000, 'work')).resolves.toBe('done');
  });

  it('rejects, naming the work, when the promise never settles', async () => {
    await expect(withTimeout(new Promise(() => {}), 20, 'Lighthouse on /')).rejects.toThrow('Lighthouse on / did not finish within 0.02 s');
  });
});

describe('stopChrome', () => {
  it('does nothing more when chrome-launcher stops Chrome', async () => {
    const chrome = fakeChrome({ exitsOnKill: true });
    const kill = vi.spyOn(process, 'kill');

    await expect(stopChrome(chrome, 50)).resolves.toBe(true);
    expect(chrome.kill).toHaveBeenCalledOnce();
    expect(kill).not.toHaveBeenCalled();
  });

  it('kills Chrome directly when it outlives chrome-launcher\'s kill', async () => {
    const chrome = fakeChrome({ exitsOnKill: false });
    const kill = vi.spyOn(process, 'kill').mockImplementation(() => {
      chrome.exit();
      return true;
    });

    await expect(stopChrome(chrome, 50)).resolves.toBe(true);
    expect(kill).toHaveBeenCalledWith(4242);
  });

  it('reports a Chrome that survives both kills', async () => {
    const chrome = fakeChrome({ exitsOnKill: false });
    vi.spyOn(process, 'kill').mockImplementation(() => true);

    await expect(stopChrome(chrome, 20)).resolves.toBe(false);
  });

  it('leaves a Chrome that already exited alone', async () => {
    const chrome = fakeChrome({ exitsOnKill: true });
    chrome.exit();

    await expect(stopChrome(chrome, 20)).resolves.toBe(true);
    expect(chrome.kill).not.toHaveBeenCalled();
  });
});

describe('acquireLock', () => {
  let folder;
  let lock;
  const quiet = () => {};

  beforeEach(() => {
    folder = mkdtempSync(join(tmpdir(), 'lighthouse-lock-'));
    lock = join(folder, 'lighthouse.lock');
  });

  afterEach(() => {
    rmSync(folder, { recursive: true, force: true });
  });

  it('takes a free lock and frees it on release', async () => {
    const release = await acquireLock(lock, { staleMs: 60_000, waitMs: 1_000, log: quiet });
    expect(JSON.parse(readFileSync(lock, 'utf8')).pid).toBe(process.pid);

    release();
    expect(existsSync(lock)).toBe(false);
  });

  it('waits while another run holds the lock, then takes it', async () => {
    const releaseFirst = await acquireLock(lock, { staleMs: 60_000, waitMs: 1_000, log: quiet });
    const log = vi.fn();
    let second = false;
    const waiting = acquireLock(lock, { staleMs: 60_000, waitMs: 5_000, pollMs: 10, log }).then((release) => {
      second = true;
      return release;
    });

    await new Promise((resolveWait) => setTimeout(resolveWait, 50));
    expect(second).toBe(false);
    expect(log).toHaveBeenCalledOnce();
    expect(log.mock.calls[0][0]).toContain(`pid ${process.pid}`);

    releaseFirst();
    const releaseSecond = await waiting;
    expect(existsSync(lock)).toBe(true);
    releaseSecond();
    expect(existsSync(lock)).toBe(false);
  });

  it('takes over a lock older than the stale limit', async () => {
    writeFileSync(lock, JSON.stringify({ pid: 4242, since: '2026-10-21T00:00:00.000Z', token: 'dead' }));
    const past = new Date(Date.now() - 120_000);
    utimesSync(lock, past, past);

    const release = await acquireLock(lock, { staleMs: 60_000, waitMs: 1_000, pollMs: 10, log: quiet });
    expect(JSON.parse(readFileSync(lock, 'utf8')).pid).toBe(process.pid);
    release();
  });

  it('gives up, naming the holder, when the lock stays held past the wait limit', async () => {
    writeFileSync(lock, JSON.stringify({ pid: 4242, since: '2026-10-21T00:00:00.000Z', token: 'held' }));

    await expect(acquireLock(lock, { staleMs: 60_000, waitMs: 50, pollMs: 10, log: quiet })).rejects.toThrow(
      'another Lighthouse run (pid 4242, since 2026-10-21T00:00:00.000Z) still held',
    );
    expect(existsSync(lock)).toBe(true);
  });

  it('leaves a lock that another run has taken since alone', async () => {
    const release = await acquireLock(lock, { staleMs: 60_000, waitMs: 1_000, log: quiet });
    writeFileSync(lock, JSON.stringify({ pid: 4242, since: '2026-10-21T00:00:00.000Z', token: 'other' }));

    release();
    expect(JSON.parse(readFileSync(lock, 'utf8')).token).toBe('other');
  });
});
