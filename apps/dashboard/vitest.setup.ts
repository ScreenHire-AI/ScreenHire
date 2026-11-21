// DOM matchers (toBeInTheDocument, toHaveTextContent, ...) on Vitest's expect, with their types.
import '@testing-library/jest-dom/vitest';

// jsdom does no layout. The candidate table's virtualizer sizes its window from the scroll element's client size and
// watches it with ResizeObserver, so give every element a desktop-sized box and a ResizeObserver that never fires.
class NoResizeObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
}
globalThis.ResizeObserver ??= NoResizeObserver;
Object.defineProperty(HTMLElement.prototype, 'clientHeight', { configurable: true, get: () => 640 });
Object.defineProperty(HTMLElement.prototype, 'clientWidth', { configurable: true, get: () => 1024 });

// jsdom has no media queries. The sidebar asks whether the window is phone-sized (Svelte's MediaQuery); answer "no",
// so it renders as on a desktop.
globalThis.matchMedia ??= (query: string) =>
  ({
    matches: false,
    media: query,
    onchange: null,
    addEventListener() {},
    removeEventListener() {},
    addListener() {},
    removeListener() {},
    dispatchEvent: () => false,
  }) as MediaQueryList;

// jsdom does no layout, and ProseMirror (the rich text editor) measures where the caret is when it focuses or scrolls a
// selection into view. Give ranges and the page empty boxes and no-op scrolling, so the editor runs as in a browser.
const emptyRect = () => DOMRect.fromRect({ x: 0, y: 0, width: 0, height: 0 });
const emptyRects = () => ({ length: 0, item: () => null, [Symbol.iterator]: [][Symbol.iterator] }) as unknown as DOMRectList;
Range.prototype.getBoundingClientRect ??= emptyRect;
Range.prototype.getClientRects ??= emptyRects;
document.elementFromPoint ??= () => null;
Element.prototype.scrollIntoView ??= () => {};
window.scrollBy = () => {};
window.scrollTo = () => {};

// jsdom has no Web Animations, and Svelte's transitions (a form builder row slides open and shut) play through
// element.animate. Each one finishes at once, as a browser's does at zero length, so an outro still removes its element.
// The bits-ui overlays read getAnimations, which tests that need it stub for themselves.
Element.prototype.animate ??= function () {
  let finished: ((this: Animation, event: AnimationPlaybackEvent) => unknown) | null = null;
  const animation = {
    currentTime: 0,
    playState: 'finished',
    cancel() {},
    finish() {},
    get onfinish() {
      return finished;
    },
    set onfinish(callback) {
      finished = callback;
      queueMicrotask(() => finished?.call(animation as unknown as Animation, new Event('finish') as AnimationPlaybackEvent));
    },
  };
  return animation as unknown as Animation;
};
