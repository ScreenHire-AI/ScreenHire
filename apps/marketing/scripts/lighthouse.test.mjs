import { EventEmitter } from 'node:events';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { stopChrome, withTimeout } from './lighthouse.mjs';

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
