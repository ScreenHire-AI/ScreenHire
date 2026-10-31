import { createHash } from 'node:crypto';
import type { PublicBrandImage, PublicBranding } from '@screenhire/api-client';
import { headingFont } from '@screenhire/ui/heading-fonts';

const hexColor = /^#[0-9a-f]{6}$/;
const imageTypes: Record<string, string> = { 'image/png': 'png', 'image/jpeg': 'jpg' };

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

  if (branding.headingFont) {
    // From ScreenHire's own table, never the API's string; an unknown name throws.
    declarations.push(`--sh-font-display:${headingFont(branding.headingFont).family}`);
  }

  if (declarations.length === 0) return null;
  // `:root:root` outranks the `:root` defaults in the bundled stylesheet, whichever order the two load in.
  const css = `:root:root{${declarations.join(';')}}\n`;
  return { css, hash: createHash('sha256').update(css).digest('hex').slice(0, 16) };
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
  if (!extension || !/^[0-9a-f-]{36}$/.test(image.id)) throw new Error('Brand images are PNG or JPEG with a uuid.');
  return `${base}/brand/images/${image.id}.${extension}`;
}

/** The image id and media type a `/brand/images/{file}` path names, or null for anything else. */
export function parseBrandImageFile(pathname: string): { id: string; contentType: string } | null {
  const match = /\/brand\/images\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\.(png|jpg)$/.exec(pathname);
  if (!match) return null;
  return { id: match[1], contentType: match[2] === 'png' ? 'image/png' : 'image/jpeg' };
}
