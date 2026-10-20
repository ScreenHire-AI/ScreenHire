import { createHash } from 'node:crypto';
import type { PublicBranding } from '@screenhire/api-client';

const hexColor = /^#[0-9a-f]{6}$/;

/**
 * The per-tenant stylesheet for an organization's brand (docs/design/design-system.md §7): the candidate CTA and brand
 * tokens from the palette the API derived and contrast-checked. Served as a file, so the content security policy needs
 * no inline styles, and named by a hash of its content, so it can be cached forever and changes name when the brand
 * does. Brand never touches status colours, the privacy notice or the footer, which use other tokens.
 */
export function brandStylesheet(branding: PublicBranding): { css: string; hash: string } {
  const colors = [branding.brand, branding.onBrand, branding.brandText, branding.brandUi].map((color) => color.toLowerCase());
  // The API sends #rrggbb; anything else never reaches a stylesheet.
  if (!colors.every((color) => hexColor.test(color))) {
    throw new Error('Brand colours must be #rrggbb.');
  }

  const [brand, onBrand, brandText, brandUi] = colors;
  // `:root:root` outranks the `:root` defaults in the bundled stylesheet, whichever order the two load in.
  const css =
    `:root:root{--candidate-cta:${brand};--candidate-cta-foreground:${onBrand};` +
    `--candidate-brand-text:${brandText};--candidate-brand-ui:${brandUi}}\n`;
  return { css, hash: createHash('sha256').update(css).digest('hex').slice(0, 16) };
}
