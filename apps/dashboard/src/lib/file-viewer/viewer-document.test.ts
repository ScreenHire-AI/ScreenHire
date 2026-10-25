import { createHash } from 'node:crypto';
import { viewerDocument } from 'virtual:file-viewer';
import { describe, expect, it } from 'vitest';
import { readFrameMessage } from './protocol.js';

// The document the resume preview's sandboxed frame runs, as file-viewer-plugin.ts builds it (a real bundle of pdf.js
// and frame.ts). The frame's origin is opaque because of the iframe's sandbox (file-frame.test.ts); this checks the
// second wall, the policy: the viewer's own script and style by hash, and nothing else.
const parsed = new DOMParser().parseFromString(viewerDocument, 'text/html');
const sha256 = (text: string) => `'sha256-${createHash('sha256').update(text, 'utf8').digest('base64')}'`;

function directives(): Map<string, string[]> {
  const policy = parsed.querySelector('meta[http-equiv="Content-Security-Policy"]')?.getAttribute('content') ?? '';
  return new Map(
    policy
      .split(';')
      .map((part) => part.trim().split(/\s+/))
      .filter((tokens) => tokens[0])
      .map(([name, ...sources]) => [name!, sources]),
  );
}

describe('the file viewer document', () => {
  it('allows its one inline script and stylesheet by hash, and no network, eval or other source', () => {
    const scripts = parsed.querySelectorAll('script');
    const styles = parsed.querySelectorAll('style');
    expect(scripts).toHaveLength(1);
    expect(scripts[0]!.hasAttribute('src')).toBe(false);
    expect(styles).toHaveLength(1);

    const policy = directives();
    expect(policy.get('default-src')).toEqual(["'none'"]);
    expect(policy.get('script-src')).toEqual([sha256(scripts[0]!.textContent!)]);
    expect(policy.get('style-src')).toEqual([sha256(styles[0]!.textContent!)]);
    expect(policy.get('base-uri')).toEqual(["'none'"]);
    expect(policy.get('form-action')).toEqual(["'none'"]);
    // default-src covers connect, img, font, frame, worker and the rest; nothing widens it.
    expect([...policy.keys()].sort()).toEqual(['base-uri', 'default-src', 'form-action', 'script-src', 'style-src']);
    expect(viewerDocument).not.toMatch(/unsafe-|wasm-unsafe-eval|strict-dynamic/);
  });

  it('keeps the whole script inside its element', () => {
    // Bytes lost to an early `</script>` would show up as text in the body.
    expect(parsed.body.children).toHaveLength(2);
    expect(parsed.body.querySelector('main')).not.toBeNull();
    expect(parsed.body.textContent).toBe(parsed.querySelector('script')!.textContent);
  });
});

describe('messages from the frame', () => {
  it.each([
    [{ type: 'ready' }, { type: 'ready' }],
    [{ type: 'failed', extra: '<img>' }, { type: 'failed' }],
    [{ type: 'size', height: 757 }, { type: 'size', height: 757 }],
    [{ type: 'rendered', pageCount: 2 }, { type: 'rendered', pageCount: 2 }],
  ])('reads %o', (data, expected) => {
    expect(readFrameMessage(data)).toEqual(expected);
  });

  it.each([
    [null],
    ['ready'],
    [{ type: 'navigate', url: 'https://example.com' }],
    [{ type: 'size', height: -1 }],
    [{ type: 'size', height: 1.5 }],
    [{ type: 'size', height: 1e9 }],
    [{ type: 'rendered', pageCount: 0 }],
    [{ type: 'rendered', pageCount: '2' }],
  ])('ignores %o', (data) => {
    expect(readFrameMessage(data)).toBeUndefined();
  });
});
