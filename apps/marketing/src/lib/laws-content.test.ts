import { readFileSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

// A citation in a law entry links to its place in the source list (SourceList anchors each as `source-<n>`), so a reader can
// jump from a claim to where it comes from. The number must name a source the entry lists, and no citation may be left as plain
// "[n]" text.
const directory = fileURLToPath(new URL('../content/laws/', import.meta.url));
const files = readdirSync(directory).filter((name) => name.endsWith('.md'));

function parts(text: string): { frontmatter: string; body: string } {
  const [, frontmatter = '', ...rest] = text.split(/^---\s*$/m);
  return { frontmatter, body: rest.join('---') };
}

describe('law entries', () => {
  it('finds the entries it guards', () => {
    expect(files.length).toBeGreaterThanOrEqual(6);
  });

  it.each(files)('%s: every citation links to a source the entry lists', (name) => {
    const { frontmatter, body } = parts(readFileSync(`${directory}${name}`, 'utf8'));
    const sources = (frontmatter.split(/^sources:\s*$/m)[1] ?? '').split(/^\S/m)[0]!.match(/^\s+- label:/gm)?.length ?? 0;
    expect(sources).toBeGreaterThan(0);

    const linked = [...body.matchAll(/\[\[(\d{1,2})\]\]\(#source-(\d{1,2})\)/g)];
    for (const [, shown, target] of linked) {
      expect(target, name).toBe(shown);
      expect(Number(target), name).toBeGreaterThanOrEqual(1);
      expect(Number(target), name).toBeLessThanOrEqual(sources);
    }

    const plain = body.replace(/\[\[\d{1,2}\]\]\(#source-\d{1,2}\)/g, '').match(/(?<![\w\]])\[\d{1,2}\](?![(\]])/g) ?? [];
    expect(plain, `${name} has citations that are not linked`).toEqual([]);
  });
});
