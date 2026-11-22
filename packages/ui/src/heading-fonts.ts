/**
 * Heading fonts on the careers pages (ADR-0031, docs/architecture/branding.md section 3). An employer picks any Google
 * Fonts family under the OFL or Apache 2.0, by its Fontsource id. Six are bundled here and load from heading-fonts.css,
 * keyed by those ids; ScreenHire's own, Bricolage Grotesque, is the default and comes from fonts.css. Every other family
 * is hosted by the API and declared by the careers brand stylesheet under `hostedHeadingFontFace(id)`. The careers pages
 * set `--sh-font-display` to the chosen family, so every title that uses `font-display` takes it. A family is written
 * into CSS only from this table or as a face name built from a checked id, never from a name the API sent.
 */
export interface HeadingFontChoice {
  /** The font's name, as the font picker and History show it. */
  label: string;
  /** The CSS `font-family` stack. */
  family: string;
}

const sans = '"Hanken Grotesk Variable", system-ui, sans-serif';
const serif = 'Georgia, "Times New Roman", serif';

/** ScreenHire's heading font, used while an organization has picked none. */
export const defaultHeadingFont: HeadingFontChoice = {
  label: 'Bricolage Grotesque',
  family: `"Bricolage Grotesque Variable", ${sans}`,
};

/** The six bundled fonts, by Fontsource id (the ids the API saves). */
export const headingFonts = {
  inter: { label: 'Inter', family: `"Inter Variable", ${sans}` },
  montserrat: { label: 'Montserrat', family: `"Montserrat Variable", ${sans}` },
  nunito: { label: 'Nunito', family: `"Nunito Variable", ${sans}` },
  fraunces: { label: 'Fraunces', family: `"Fraunces Variable", ${serif}` },
  'playfair-display': { label: 'Playfair Display', family: `"Playfair Display Variable", ${serif}` },
} as const satisfies Record<string, HeadingFontChoice>;

export type BundledHeadingFontId = keyof typeof headingFonts;

/** The shape of every font id: lower-case words joined by single hyphens, as every Fontsource id is. */
export const headingFontIdPattern = /^[a-z0-9]+(-[a-z0-9]+)*$/;

/** The bundled choice for `id`, or null for a font that is not bundled. */
export function bundledHeadingFont(id: string | null | undefined): HeadingFontChoice | null {
  return id != null && Object.hasOwn(headingFonts, id) ? headingFonts[id as BundledHeadingFontId] : null;
}

/**
 * The `@font-face` family name a hosted font is declared under. Built from the id alone, which is checked first, so
 * nothing the API sent other than an id of the safe shape reaches a stylesheet.
 */
export function hostedHeadingFontFace(id: string): string {
  if (!headingFontIdPattern.test(id)) throw new Error(`Not a heading font id: ${id}`);
  return `ScreenHire heading ${id}`;
}

/** The fallback after a hosted face: serif faces fall back to a serif, everything else to ScreenHire's sans. */
export function headingFontFallback(category: string): string {
  return category === 'serif' ? serif : sans;
}

/** The `font-family` stack for a font: the default for null, a bundled font's own stack, or a hosted face's. */
export function headingFontFamily(font: { id: string; category?: string } | null | undefined): string {
  if (font == null) return defaultHeadingFont.family;
  const bundled = bundledHeadingFont(font.id);
  if (bundled) return bundled.family;
  return `"${hostedHeadingFontFace(font.id)}", ${headingFontFallback(font.category ?? '')}`;
}
