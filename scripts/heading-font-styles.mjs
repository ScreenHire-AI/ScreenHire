// Refreshes apps/api/Fonts/google-font-styles.json: the Google Fonts families that the heading font picker
// files under Rounded and Slab (docs/architecture/branding.md section 3). Fontsource's catalogue, which the API reads
// live, has a category (sans-serif, serif, display) but no Rounded or Slab, so those two come from Google's own tags:
// tags/all/families.csv in github.com/google/fonts, where a family carries "/Sans/Rounded" or a "/Slab/..." group with a
// weight out of 100. A family counts at 50 or more. Ids are Fontsource's, matched on the family name.
//
//   node scripts/heading-font-styles.mjs
//
// Run it when Google adds families (a few times a year is plenty); the API picks the file up on its next build. No key
// is needed for either source.
import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const TAGS = 'https://raw.githubusercontent.com/google/fonts/main/tags/all/families.csv';
const CATALOGUE = 'https://api.fontsource.org/v1/fonts';
const THRESHOLD = 50;
const output = fileURLToPath(new URL('../apps/api/Fonts/google-font-styles.json', import.meta.url));

async function fetchText(url) {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`${url} answered ${response.status}`);
  return response.text();
}

const ids = new Map(JSON.parse(await fetchText(CATALOGUE)).filter((font) => font.type === 'google').map((font) => [font.family, font.id]));
const rounded = new Set();
const slab = new Set();
// Each line is family,axes,group,weight; no family name holds a comma.
for (const line of (await fetchText(TAGS)).split('\n')) {
  const [family, , group, weight] = line.split(',');
  if (!family || !(Number(weight) >= THRESHOLD) || !ids.has(family)) continue;
  if (group === '/Sans/Rounded') rounded.add(ids.get(family));
  if (group?.startsWith('/Slab/')) slab.add(ids.get(family));
}

const sorted = (set) => [...set].sort();
writeFileSync(output, `${JSON.stringify({ source: TAGS, threshold: THRESHOLD, rounded: sorted(rounded), slab: sorted(slab) }, null, 2)}\n`);
console.log(`${rounded.size} rounded and ${slab.size} slab families written to ${output}`);
