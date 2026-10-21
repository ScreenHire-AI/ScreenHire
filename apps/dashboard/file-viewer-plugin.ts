import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { build, type Plugin } from 'vite';

// The resume preview draws an untrusted file inside an `<iframe sandbox="allow-scripts" srcdoc>`: an opaque origin,
// so no script in it can read the dashboard's cookies or DOM. This plugin builds that frame's document as the virtual
// module `virtual:file-viewer`. Vite bundles `src/lib/file-viewer/frame.ts` (pdf.js included) into one classic
// script, and the document allows that script and its stylesheet by hash and nothing else: no network, no other
// script, no eval. It runs in `vite dev`, `vite build` and Vitest alike, so there is no prebuilt file to go stale.
// Vite has no native way to inline a separately bundled script into a document string, hence the plugin.

const moduleId = 'virtual:file-viewer';
const resolvedId = `\0${moduleId}`;
const root = fileURLToPath(new URL('.', import.meta.url));
const entry = fileURLToPath(new URL('./src/lib/file-viewer/frame.ts', import.meta.url));

// Layout only. The frame is transparent, so the dashboard's inset colour shows through, and design tokens never need
// to cross into it. Each page's text is kept for screen readers and hidden visually.
const style =
  'html,body{margin:0;background:transparent}' +
  'main{display:flex;flex-direction:column;gap:12px}' +
  'section{position:relative}' +
  'canvas{display:block;width:100%;height:auto}' +
  'p{position:absolute;width:1px;height:1px;margin:0;overflow:hidden;clip-path:inset(50%);white-space:pre-wrap}';

const sha256 = (text: string) => `'sha256-${createHash('sha256').update(text, 'utf8').digest('base64')}'`;

async function bundle(): Promise<string> {
  const result = await build({
    configFile: false,
    root,
    logLevel: 'warn',
    publicDir: false,
    build: {
      write: false,
      minify: true,
      sourcemap: false,
      lib: { entry, formats: ['iife'], name: 'screenhireFileViewer' },
      // pdf.js reads import.meta.url only on its Node.js paths; a classic script has no import.meta.
      rolldownOptions: { transform: { define: { 'import.meta.url': 'undefined' } } },
    },
  });
  const outputs = Array.isArray(result) ? result : [result];
  const chunks = outputs.flatMap((output) => ('output' in output ? output.output : [])).filter((file) => file.type === 'chunk');
  if (chunks.length !== 1) throw new Error(`The file viewer should bundle to one script, not ${chunks.length}.`);
  // `</script` would end the inline element early; `<\/script` means the same in every JavaScript context.
  const code = chunks[0]!.code.replace(/<\/script/gi, '<\\/script');
  if (code.includes('<!--')) throw new Error('The file viewer bundle contains "<!--", which an inline script cannot hold.');
  return code;
}

/** The viewer's whole document, ready for `srcdoc`. */
export async function viewerDocument(): Promise<string> {
  const script = await bundle();
  const policy = [
    "default-src 'none'",
    `script-src ${sha256(script)}`,
    `style-src ${sha256(style)}`,
    "base-uri 'none'",
    "form-action 'none'",
  ].join('; ');
  return (
    '<!doctype html><html lang="en"><head><meta charset="utf-8">' +
    `<meta http-equiv="Content-Security-Policy" content="${policy}">` +
    '<meta name="referrer" content="no-referrer"><meta name="color-scheme" content="light dark">' +
    `<title>File preview</title><style>${style}</style></head><body><main></main><script>${script}</script></body></html>`
  );
}

export function fileViewer(): Plugin {
  let built: Promise<string> | undefined;
  return {
    name: 'screenhire:file-viewer',
    resolveId: (id) => (id === moduleId ? resolvedId : undefined),
    async load(id) {
      if (id !== resolvedId) return undefined;
      // Built once per process: a change to frame.ts needs a dev server restart.
      built ??= viewerDocument();
      return `export const viewerDocument = ${JSON.stringify(await built)};`;
    },
  };
}
