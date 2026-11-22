import { getGetHeadingFontPreviewUrl } from '@screenhire/api-client/svelte-query';
import { bundledHeadingFont, headingFontFallback, hostedHeadingFontFace } from '@screenhire/ui/heading-fonts';
import { SvelteMap } from 'svelte/reactivity';

/**
 * The faces the font picker and the branding preview draw a catalogue font in (docs/architecture/branding.md section
 * 3). A bundled font is in the dashboard's own CSS. Any other family's preview file comes from the API
 * (`/v1/heading-fonts/{id}/preview`, one latin file near 700), never from a font host, and is added to `document.fonts`
 * once, the first time a row or the preview asks for it, under the same face name the careers pages use. Its one file
 * stands for every weight, so a preview never synthesises bold.
 */
export type FaceState = 'loading' | 'ready' | 'failed';

const states = new SvelteMap<string, FaceState>();

/** Loads `id`'s preview face if it is not loaded or loading; a bundled font needs nothing. */
export function loadHeadingFontFace(id: string): void {
  if (bundledHeadingFont(id) || states.has(id) || typeof FontFace === 'undefined') return;
  states.set(id, 'loading');
  const face = new FontFace(hostedHeadingFontFace(id), `url(${getGetHeadingFontPreviewUrl(id)}) format("woff2")`, { weight: '100 900' });
  face.load().then(
    (loaded) => {
      document.fonts.add(loaded);
      states.set(id, 'ready');
    },
    // The name stays in the page's own font; the picker still works.
    () => states.set(id, 'failed'),
  );
}

/** Whether `id`'s face is drawn yet: a bundled font always is. */
export function headingFontFaceState(id: string): FaceState {
  return bundledHeadingFont(id) ? 'ready' : (states.get(id) ?? 'loading');
}

/** The `font-family` to draw `id` in: its bundled stack, or its preview face with a fallback for its style. */
export function previewFamily(id: string, serif: boolean): string {
  return bundledHeadingFont(id)?.family ?? `"${hostedHeadingFontFace(id)}", ${headingFontFallback(serif ? 'serif' : '')}`;
}
