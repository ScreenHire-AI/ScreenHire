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
