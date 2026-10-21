// The file viewer inside the resume preview's sandboxed frame. It runs with an opaque origin under a policy that
// allows only its own script and style (file-viewer-plugin.ts builds the document), so nothing it draws can reach the
// dashboard's cookies, DOM or API. It receives a file's bytes from the dashboard, draws the pages to canvases with
// pdf.js, keeps each page's text for screen readers, and reports its height and page count.
//
// Importing the worker module sets `globalThis.pdfjsWorker`, so pdf.js parses in this thread: the policy allows no
// Worker and no fetch, and the frame has nothing else to load.
import 'pdfjs-dist/build/pdf.worker.mjs';
import { getDocument, VerbosityLevel } from 'pdfjs-dist';
import { isRenderRequest, maxPreviewPages, type FrameMessage } from './protocol.js';

// The parent is the only window that talks to this frame; its origin is the dashboard's, which an opaque frame cannot
// name, so the target is "*". Nothing sent here is secret: sizes and a page count.
const post = (message: FrameMessage) => parent.postMessage(message, '*');

const pages = document.querySelector('main')!;
let started = false;

async function render(bytes: ArrayBuffer): Promise<void> {
  const pdf = await getDocument({ data: new Uint8Array(bytes), verbosity: VerbosityLevel.ERRORS, enableXfa: false }).promise;
  const ratio = Math.min(window.devicePixelRatio || 1, 2);
  for (let number = 1; number <= Math.min(pdf.numPages, maxPreviewPages); number++) {
    const page = await pdf.getPage(number);
    const viewport = page.getViewport({ scale: (pages.clientWidth * ratio) / page.getViewport({ scale: 1 }).width });
    const canvas = document.createElement('canvas');
    canvas.width = Math.floor(viewport.width);
    canvas.height = Math.floor(viewport.height);
    canvas.setAttribute('aria-hidden', 'true');
    await page.render({ canvas, viewport }).promise;

    const text = await page.getTextContent();
    const words = document.createElement('p');
    words.textContent = text.items.map((item) => ('str' in item ? item.str + (item.hasEOL ? '\n' : ' ') : '')).join('');
    const section = document.createElement('section');
    section.setAttribute('aria-label', `Page ${number}`);
    section.append(canvas, words);
    pages.append(section);
    page.cleanup();
  }

  // Nothing to tear down: the dashboard removes the whole frame with the preview.
  post({ type: 'rendered', pageCount: pdf.numPages });
}

addEventListener('message', (event) => {
  if (event.source !== parent || started || !isRenderRequest(event.data)) return;
  started = true;
  render(event.data.bytes).catch(() => post({ type: 'failed' }));
});

new ResizeObserver(() => post({ type: 'size', height: Math.ceil(document.documentElement.getBoundingClientRect().height) })).observe(
  document.documentElement,
);
post({ type: 'ready' });
