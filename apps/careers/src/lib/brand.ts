import { createHash } from 'node:crypto';
import type { PublicBrandImage, PublicBranding, PublicHeadingFont } from '@screenhire/api-client';
import { bundledHeadingFont, headingFontFamily, headingFontIdPattern, hostedHeadingFontFace } from '@screenhire/ui/heading-fonts';

const hexColor = /^#[0-9a-f]{6}$/;
// One uuid pattern for the image ids the pages link and the paths the image route reads.
const uuid = '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}';
const imageId = new RegExp(`^${uuid}$`);
const imageFile = new RegExp(String.raw`/brand/images/(${uuid})\.(png|jpg)$`);
const imageTypes: Record<string, string> = { 'image/png': 'png', 'image/jpeg': 'jpg' };
// A hosted heading font's files (docs/architecture/branding.md section 3): one per subset and weight, as the API names them.
const fontFileName = /^(latin|latin-ext)-[1-9]00\.woff2$/;
const fontWeight = /^[1-9]00$/;
const unicodeRange = /^U\+[0-9A-F?]{1,6}(-[0-9A-F]{1,6})?(,\s*U\+[0-9A-F?]{1,6}(-[0-9A-F]{1,6})?)*$/i;
const fontFile = /\/brand\/fonts\/([a-z0-9]+(?:-[a-z0-9]+)*)\/([a-z-]+-[1-9]00\.woff2)$/;

/**
 * The per-tenant stylesheet for an organization's branding (docs/architecture/branding.md): the candidate CTA and
 * brand tokens from the palette the API checked, the header band from the second colour, and the heading font, which
 * replaces `--sh-font-display` so every title that uses `font-display` takes it. Served as a file, so the content
 * security policy needs no inline styles, and named by a hash of its content, so it can be cached forever and changes
 * name when the branding does. Brand never touches status colours, the privacy notice or the footer, which use other
 * tokens. Null when the organization uses ScreenHire's look throughout.
 */
export function brandStylesheet(branding: PublicBranding): { css: string; hash: string } | null {
  const declarations: string[] = [];
  if (branding.colors) {
    const { brand, onBrand, brandText, brandUi } = branding.colors;
    declarations.push(
      `--candidate-cta:${color(brand)}`,
      `--candidate-cta-foreground:${color(onBrand)}`,
      `--candidate-brand-text:${color(brandText)}`,
      `--candidate-brand-ui:${color(brandUi)}`,
    );
  }

  if (branding.header) {
    declarations.push(`--candidate-header:${color(branding.header.background)}`, `--candidate-header-foreground:${color(branding.header.foreground)}`);
  }

  const faces = branding.headingFont ? fontFaces(branding.headingFont) : [];
  if (branding.headingFont) {
    // A bundled font's stack from ScreenHire's own table, a hosted one's from a face name built from its checked id;
    // never the family name the API sent.
    declarations.push(`--sh-font-display:${headingFontFamily(branding.headingFont)}`);
  }

  if (declarations.length === 0) return null;
  // `:root:root` outranks the `:root` defaults in the bundled stylesheet, whichever order the two load in.
  const css = `${faces.join('')}:root:root{${declarations.join(';')}}\n`;
  return { css, hash: createHash('sha256').update(css).digest('hex').slice(0, 16) };
}

/**
 * The `@font-face` rules of a hosted heading font, one per file, each limited to its subset's unicode range so a page
 * downloads only the files its titles draw. The addresses are relative to the stylesheet (`{base}/brand/{hash}.css`),
 * so they resolve to `{base}/brand/fonts/{id}/{file}`, the careers route that serves them on this origin. A bundled font
 * has none: heading-fonts.css declares it. Anything that is not of the shape the API sends throws.
 */
function fontFaces(font: PublicHeadingFont): string[] {
  if (!headingFontIdPattern.test(font.id)) throw new Error('A heading font id is lower-case words joined by hyphens.');
  if (font.bundled) {
    if (!bundledHeadingFont(font.id)) throw new Error(`Not a bundled heading font: ${font.id}`);
    return [];
  }

  const face = hostedHeadingFontFace(font.id);
  return font.files.map((file) => {
    if (!fontFileName.test(file.fileName) || !fontWeight.test(file.weight) || !unicodeRange.test(file.unicodeRange)) {
      throw new Error('A heading font file must be a latin WOFF2 file of one weight.');
    }

    return (
      `@font-face{font-family:"${face}";src:url("fonts/${font.id}/${file.fileName}") format("woff2");` +
      `font-weight:${file.weight};font-style:normal;font-display:swap;unicode-range:${file.unicodeRange}}\n`
    );
  });
}

/**
 * The file of a hosted heading font a page preloads: its latin file at the weight nearest the titles' 700, or null for
 * a bundled font or none. Preloading it lets the title draw in its font on the first paint more often, so the swap from
 * the fallback (`font-display: swap`) moves the page less.
 */
export function headingFontPreload(base: string, branding: PublicBranding | null | undefined): string | null {
  const font = branding?.headingFont;
  if (!font || font.bundled || !headingFontIdPattern.test(font.id)) return null;
  const nearest = font.files
    .filter((file) => file.subset === 'latin' && fontFileName.test(file.fileName))
    .sort((a, b) => Math.abs(Number(a.weight) - 700) - Math.abs(Number(b.weight) - 700) || Number(b.weight) - Number(a.weight))[0];
  return nearest ? `${base}/brand/fonts/${font.id}/${nearest.fileName}` : null;
}

/** The font id and file a `/brand/fonts/{id}/{file}` path names, or null for anything else. */
export function parseHeadingFontFile(pathname: string): { id: string; fileName: string } | null {
  const match = fontFile.exec(pathname);
  if (!match || !fontFileName.test(match[2])) return null;
  return { id: match[1], fileName: match[2] };
}

/** The API sends #rrggbb; anything else never reaches a stylesheet. */
function color(value: string): string {
  const lower = value.toLowerCase();
  if (!hexColor.test(lower)) throw new Error('Brand colours must be #rrggbb.');
  return lower;
}

/**
 * Where a careers page links a brand image: `/brand/images/{id}.{png|jpg}` under the organization's base path, served on
 * the careers origin by `[organization]/brand/images/[file].ts`, so `img-src 'self'` covers it.
 */
export function brandImagePath(base: string, image: PublicBrandImage): string {
  const extension = imageTypes[image.contentType];
  if (!extension || !imageId.test(image.id)) throw new Error('Brand images are PNG or JPEG with a uuid.');
  return `${base}/brand/images/${image.id}.${extension}`;
}

/** The image id and media type a `/brand/images/{file}` path names, or null for anything else. */
export function parseBrandImageFile(pathname: string): { id: string; contentType: string } | null {
  const match = imageFile.exec(pathname);
  if (!match) return null;
  return { id: match[1], contentType: match[2] === 'png' ? 'image/png' : 'image/jpeg' };
}
