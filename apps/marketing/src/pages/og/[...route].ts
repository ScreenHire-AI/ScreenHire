import { OGImageRoute } from 'astro-og-canvas';
import { fontFiles, readTokens, tokenColor } from '../../lib/og';
import { ogImageKey } from '../../lib/seo';
import { siteIndex } from '../../lib/site-index';

// One 1200x630 PNG per indexable page at /og/<key>.png, rendered at build time (ADR-0003). Seo.astro links each page
// to its image through ogImagePath().
const entries = await siteIndex();
const tokens = readTokens();
const fonts = fontFiles();

export const { getStaticPaths, GET } = await OGImageRoute({
  pages: Object.fromEntries(entries.map((entry) => [ogImageKey(entry.path), entry])),
  getSlug: (key) => `${key}.png`,
  getImageOptions: (_key, entry) => ({
    title: entry.title,
    description: entry.description,
    bgGradient: [tokenColor(tokens, 'background')],
    border: { color: tokenColor(tokens, 'primary'), width: 24, side: 'inline-start' },
    padding: 72,
    fonts,
    font: {
      // CanvasKit names the variable font by its default instance, which is the weight the titles use.
      title: { families: ['Bricolage Grotesque 96pt ExtraBold', 'Bricolage Grotesque'], weight: 'ExtraBold', size: 64, lineHeight: 1.05, color: tokenColor(tokens, 'foreground') },
      description: { families: ['Hanken Grotesk'], weight: 'Normal', size: 32, lineHeight: 1.35, color: tokenColor(tokens, 'muted-foreground') },
    },
  }),
});
