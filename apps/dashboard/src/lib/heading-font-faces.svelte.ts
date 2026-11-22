import { getGetHeadingFontPreviewUrl } from '@screenhire/api-client/svelte-query';
import { bundledHeadingFont, headingFontFallback, hostedHeadingFontFace } from '@screenhire/ui/heading-fonts';

/**
 * The faces the font picker and the branding preview draw a catalogue font in (docs/architecture/branding.md section
 * 3). A bundled font is in the dashboard's own CSS. Any other family's preview file comes from the API
 * (`/v1/heading-fonts/{id}/preview`, one latin file near 700), never from a font host, and is added to `document.fonts`
 * once, the first time a row or the preview asks for it, under the same face name the careers pages use. Its one file
 * stands for every weight, so a preview never synthesises bold. A face that fails to load (the preview route's rate
 * limit, a moment offline) is forgotten, so the next row or preview that asks tries again; until then the name shows in
 * the page's own font.
 */
const requested = new Set<string>();

/** Loads `id`'s preview face unless it is loaded or loading; a bundled font needs nothing. */
export function loadHeadingFontFace(id: string): void {
  if (bundledHeadingFont(id) || requested.has(id) || typeof FontFace === 'undefined') return;
  requested.add(id);
  const face = new FontFace(hostedHeadingFontFace(id), `url(${getGetHeadingFontPreviewUrl(id)}) format("woff2")`, { weight: '100 900' });
  face.load().then(
    (loaded) => document.fonts.add(loaded),
    () => requested.delete(id),
  );
}

/** The `font-family` to draw `id` in: its bundled stack, or its preview face with a fallback for its style. */
export function previewFamily(id: string, serif: boolean): string {
  return bundledHeadingFont(id)?.family ?? `"${hostedHeadingFontFace(id)}", ${headingFontFallback(serif ? 'serif' : '')}`;
}
