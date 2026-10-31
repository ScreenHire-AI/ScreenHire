// The snippet's script (src/embed/embed.js) runs on a company's own page. It is plain browser JavaScript with no imports,
// so it runs here against a small stand-in for that page: what it adds, and which messages may size the frame.
import { describe, expect, it } from 'vitest';
import source from '../embed/embed.js?raw';

interface FakeFrame {
  className: string;
  title: string;
  src: string;
  style: Record<string, string>;
  contentWindow: object;
  top: number;
  scrolled: boolean;
  getBoundingClientRect(): { top: number };
  scrollIntoView(): void;
}

function hostPage(scriptSrc: string, pageOrigin = 'https://www.example.com') {
  const inserted: FakeFrame[] = [];
  const listeners: ((event: { origin: string; source: unknown; data: unknown }) => void)[] = [];
  const script = {
    src: scriptSrc,
    nextSibling: null,
    parentNode: { insertBefore: (node: FakeFrame) => inserted.push(node) },
  };
  const document = {
    currentScript: script,
    createElement: (): FakeFrame => ({
      className: '',
      title: '',
      src: '',
      style: {},
      contentWindow: {},
      top: 0,
      scrolled: false,
      getBoundingClientRect() {
        return { top: this.top };
      },
      scrollIntoView() {
        this.scrolled = true;
      },
    }),
  };
  const window = { location: { origin: pageOrigin }, addEventListener: (_: string, listener: (typeof listeners)[number]) => listeners.push(listener) };
  new Function('document', 'window', source)(document, window);
  const frame = inserted[0]!;
  const post = (data: unknown, origin = 'https://acme.careers.example', from: unknown = frame.contentWindow) =>
    listeners.forEach((listener) => listener({ origin, source: from, data }));
  return { frame, inserted, post };
}

describe('the embed script', () => {
  it('adds one frame after itself, showing the board with the page it is on', () => {
    const { frame, inserted } = hostPage('https://acme.careers.example/embed.js');

    expect(inserted).toHaveLength(1);
    expect(frame.className).toBe('screenhire-careers');
    expect(frame.title).toBe('Jobs');
    expect(frame.src).toBe('https://acme.careers.example/?embed=https%3A%2F%2Fwww.example.com');
    expect(frame.style).toMatchObject({ width: '100%', height: '600px' });
  });

  it('frames a board whose organization is in the path', () => {
    expect(hostPage('https://careers.example/acme/embed.js?v=1').frame.src).toBe('https://careers.example/acme?embed=https%3A%2F%2Fwww.example.com');
  });

  it('sizes and names the frame from the page inside it', () => {
    const { frame, post } = hostPage('https://acme.careers.example/embed.js');

    post({ type: 'screenhire:frame', height: 1234.2, title: 'Jobs at Acme', page: false });

    expect(frame.style.height).toBe('1235px');
    expect(frame.title).toBe('Jobs at Acme');
    expect(frame.scrolled).toBe(false);
  });

  it('brings a new page into view when the visitor had scrolled past the top of the frame', () => {
    const { frame, post } = hostPage('https://acme.careers.example/embed.js');
    frame.top = -400;

    post({ type: 'screenhire:frame', height: 900, title: 'Apply', page: true });

    expect(frame.scrolled).toBe(true);
  });

  it('ignores messages from another origin, from another window, or that are not a size', () => {
    const { frame, post } = hostPage('https://acme.careers.example/embed.js');

    post({ type: 'screenhire:frame', height: 50 }, 'https://evil.example.net');
    post({ type: 'screenhire:frame', height: 50 }, 'https://acme.careers.example', {});
    post({ type: 'other', height: 50 });
    post({ type: 'screenhire:frame', height: 'tall' });
    post({ type: 'screenhire:frame', height: -5 });
    post(null);

    expect(frame.style.height).toBe('600px');
    expect(frame.title).toBe('Jobs');
  });
});
