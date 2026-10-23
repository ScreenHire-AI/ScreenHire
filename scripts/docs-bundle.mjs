/**
 * Helpers for the Open Knowledge Format v0.2 bundle under docs/ (ADR-0012): frontmatter, GitHub's heading anchors,
 * links, and the concept list. Every markdown file but index.md and log.md is a concept. /sync-docs and the
 * conformance tests (tests/ScreenHire.UnitTests/Tooling/DocsBundleTests.cs) both read the docs through this file, so
 * the two cannot disagree about what a heading's anchor is or which concepts a source file feeds.
 *
 *   node scripts/docs-bundle.mjs sources <repo path>...
 *
 * prints, for each path, the concepts whose `sources:` name the file. /sync-docs uses it to map a changed source file
 * to the docs to re-check. A path that is a folder prints the concepts that name any file in it.
 *
 *   node scripts/docs-bundle.mjs model [--root <dir>]
 *
 * prints the bundle as JSON (every docs file, each markdown file's frontmatter, headings and links) for the
 * conformance tests. `--root` points at another repository root, such as a test fixture.
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
export const PROJECT_ROOT = path.resolve(HERE, '..');
/** The two names OKF reserves at every level. Neither is a concept. */
export const RESERVED_NAMES = new Set(['index.md', 'log.md']);
/** The `type` values a concept may carry. Decision is an ADR. */
export const CONCEPT_TYPES = ['Guide', 'Reference', 'Design', 'Decision', 'Audit', 'Research'];

/** A leading `---` line (after an optional BOM), the block, which may be empty, and the closing `---` line. */
const FRONTMATTER = /^\uFEFF?---[ \t]*\r?\n(?:([\s\S]*?)\r?\n)??---[ \t]*(?:\r?\n|$)/;
const FENCE = /^\s*(```|~~~)/;

/** A scalar as YAML writes it here: bare, or double or single quoted. */
function parseScalar(raw) {
  const value = raw.trim();
  if (value.length >= 2 && value.startsWith('"') && value.endsWith('"')) {
    return value.slice(1, -1).replace(/\\(["\\])/g, '$1');
  }
  if (value.length >= 2 && value.startsWith('\'') && value.endsWith('\'')) {
    return value.slice(1, -1).replace(/''/g, '\'');
  }
  return value;
}

/** `[a, "b, c"]` as a list of strings. A comma inside a quoted item stays in that item. */
function parseInlineList(raw) {
  const inner = raw.trim().slice(1, -1);
  if (inner.trim().length === 0) return [];
  const items = [];
  let current = '';
  let quote = null;
  for (let position = 0; position < inner.length; position += 1) {
    const character = inner[position];
    if (quote !== null) {
      current += character;
      if (quote === '"' && character === '\\' && position + 1 < inner.length) {
        current += inner[position + 1];
        position += 1;
      } else if (character === quote) {
        quote = null;
      }
      continue;
    }
    if (character === ',') {
      items.push(current);
      current = '';
      continue;
    }
    if (character === '"' || character === '\'') quote = character;
    current += character;
  }
  items.push(current);
  return items.map(parseScalar);
}

/**
 * The frontmatter of a markdown file, or null when it has none. Reads the subset the concepts use: scalar keys,
 * inline lists, and block lists of scalars or of flat mappings (`sources:`). Anything else throws, so a hand edit the
 * parser would misread fails loudly instead of passing as empty.
 */
export function parseFrontmatter(text) {
  const match = FRONTMATTER.exec(text);
  if (!match) return null;
  const lines = (match[1] ?? '').split(/\r?\n/);
  const data = {};
  let index = 0;
  while (index < lines.length) {
    const line = lines[index];
    if (line.trim() === '' || line.trimStart().startsWith('#')) {
      index += 1;
      continue;
    }
    const keyMatch = /^([A-Za-z_][\w-]*):(.*)$/.exec(line);
    if (!keyMatch) throw new Error(`frontmatter line ${index + 1} is not a top-level key: ${line}`);
    const [, key, rest] = keyMatch;
    const value = rest.trim();
    index += 1;
    if (value.startsWith('[')) {
      data[key] = parseInlineList(value);
      continue;
    }
    if (value !== '') {
      data[key] = parseScalar(value);
      continue;
    }
    const items = [];
    while (index < lines.length && /^\s+-\s/.test(lines[index])) {
      const first = lines[index].replace(/^\s+-\s+/, '');
      index += 1;
      const pair = /^([A-Za-z_][\w-]*):\s*(.*)$/.exec(first);
      if (!pair) {
        items.push(parseScalar(first));
        continue;
      }
      const item = { [pair[1]]: parseScalar(pair[2]) };
      while (index < lines.length && /^\s{4,}[A-Za-z_][\w-]*:/.test(lines[index]) && !/^\s+-\s/.test(lines[index])) {
        const more = /^\s+([A-Za-z_][\w-]*):\s*(.*)$/.exec(lines[index]);
        item[more[1]] = parseScalar(more[2]);
        index += 1;
      }
      items.push(item);
    }
    data[key] = items;
  }
  return data;
}

/** The text after the frontmatter block, or the whole text when there is none. */
export function stripFrontmatter(text) {
  const match = FRONTMATTER.exec(text);
  return match ? text.slice(match[0].length) : text;
}

/**
 * A heading's rendered text: links to their text, emphasis markers and HTML tags dropped, and inline code kept as
 * written (GitHub renders `<id>` inside backticks as text).
 */
export function headingText(raw) {
  const withoutLinks = raw.replace(/!?\[([^\]]*)\]\([^)]*\)/g, '$1');
  // Outside code spans: drop HTML tags, `*` emphasis, and `_` emphasis only at a word's edge, so a snake_case word
  // keeps its underscores the way GitHub's anchor keeps them.
  return withoutLinks
    .split(/(`+[^`]*`+)/)
    .map((part) => (part.startsWith('`')
      ? part.replace(/^`+|`+$/g, '')
      : part.replace(/<[^>]+>/g, '').replace(/\*/g, '').replace(/(^|\s)_+|_+(\s|$)/g, '$1$2')))
    .join('')
    .trim();
}

/** GitHub's anchor for a heading's text, before the duplicate suffix. */
export function githubSlug(text) {
  return headingText(text)
    .toLowerCase()
    .replace(/[^\p{L}\p{N}\p{M} _-]/gu, '')
    .replace(/ /g, '-');
}

/**
 * Every ATX heading outside a code fence, with GitHub's anchor including the `-1`, `-2` suffix a repeated heading
 * gets: `{ level, text, line, slug }`. `line` is 1-based in the whole file, frontmatter included.
 */
export function headingsOf(text) {
  const headings = [];
  // github-slugger's dedupe: a taken slug gets the next free `-N` counted on its base.
  const occurrences = new Map();
  let fenced = false;
  const body = stripFrontmatter(text);
  const frontmatterLines = text.slice(0, text.length - body.length).split(/\r?\n/).length - 1;
  body.split(/\r?\n/).forEach((line, lineIndex) => {
    if (FENCE.test(line)) {
      fenced = !fenced;
      return;
    }
    if (fenced) return;
    // A trailing run of `#` closes the heading only after a space (CommonMark), so `## Using C#` keeps its `#`.
    const match = /^(#{1,6})\s+(.*?)(?:\s+#+)?\s*$/.exec(line);
    if (!match) return;
    const base = githubSlug(match[2]);
    let slug = base;
    while (occurrences.has(slug)) {
      occurrences.set(base, occurrences.get(base) + 1);
      slug = `${base}-${occurrences.get(base)}`;
    }
    occurrences.set(slug, 0);
    headings.push({ level: match[1].length, text: match[2], line: frontmatterLines + lineIndex + 1, slug });
  });
  return headings;
}

/** A link fragment percent-decoded, or as written when its escapes are malformed (`#100%`). */
function decodeFragment(fragment) {
  try {
    return decodeURIComponent(fragment);
  } catch {
    return fragment;
  }
}

/**
 * Every relative inline markdown link or image outside code fences and inline code: `{ url, path, fragment, line }`,
 * with `path` empty for a same-file `#fragment` link. A link whose text wraps onto the next line of its paragraph is
 * read whole and reported at the line it starts on.
 */
export function linksOf(text) {
  const links = [];
  let fenced = false;
  // An open `[` with no `]` after it on its line: the start of link text that wraps onto the next line.
  let carried = null;
  text.split(/\r?\n/).forEach((line, lineIndex) => {
    if (FENCE.test(line)) {
      fenced = !fenced;
      carried = null;
      return;
    }
    if (fenced) return;
    // Blank each inline code span to spaces of the same width, so a link written inside backticks is not read.
    const outsideCode = line.replace(/(`+)[\s\S]*?\1/g, (span) => ' '.repeat(span.length));
    if (outsideCode.trim().length === 0) {
      carried = null;
      return;
    }
    const prefix = carried ? `${carried.text} ` : '';
    const joined = prefix + outsideCode;
    const lineOf = (index) => (index < prefix.length ? carried.line : lineIndex + 1);
    for (const match of joined.matchAll(/!?\[[^\]]*\]\(([^)\s]+)(?:\s+"[^"]*")?\)/g)) {
      const url = match[1];
      if (/^[a-z][a-z0-9+.-]*:/i.test(url)) continue;
      const hash = url.indexOf('#');
      links.push({
        url,
        path: hash === -1 ? url : url.slice(0, hash),
        fragment: hash === -1 ? null : decodeFragment(url.slice(hash + 1)),
        line: lineOf(match.index),
      });
    }
    const open = joined.lastIndexOf('[');
    carried = open !== -1 && !joined.includes(']', open) ? { text: joined.slice(open), line: lineOf(open) } : null;
  });
  return links;
}

/** Every file under `<root>/docs`, as '/'-separated paths relative to the root. */
export function listDocsFiles(root = PROJECT_ROOT) {
  const docsRoot = path.join(root, 'docs');
  return fs.readdirSync(docsRoot, { recursive: true, withFileTypes: true })
    .filter((entry) => entry.isFile())
    .map((entry) => path.relative(root, path.join(entry.parentPath, entry.name)).replace(/\\/g, '/'))
    .sort();
}

/** Whether a docs path is a concept (any markdown file but the two reserved names). */
export function isConceptPath(docPath) {
  return docPath.endsWith('.md') && !RESERVED_NAMES.has(path.posix.basename(docPath));
}

/** Every concept with its frontmatter: `{ path, frontmatter }`. */
export function listConcepts(root = PROJECT_ROOT) {
  return listDocsFiles(root)
    .filter(isConceptPath)
    .map((docPath) => ({ path: docPath, frontmatter: parseFrontmatter(fs.readFileSync(path.join(root, docPath), 'utf8')) }));
}

/** A `sources:` entry's repo path: an entry is `{ resource, title? }`, or a bare string. */
export function sourcePathOf(entry) {
  if (typeof entry === 'string') return entry;
  return typeof entry?.resource === 'string' ? entry.resource : '';
}

function normalizeRepoPath(repoPath) {
  return repoPath.replace(/\\/g, '/').replace(/^\.\//, '');
}

/** Whether one `sources:` entry covers `target`: the same file, or a file inside the folder `target` names. */
function entryCovers(entry, target) {
  const source = normalizeRepoPath(sourcePathOf(entry));
  if (source.length === 0) return false;
  return source === target || source.startsWith(target.endsWith('/') ? target : `${target}/`);
}

/** The concepts whose sources cover `repoPath`: the file itself, or a file inside the folder it names. */
export function conceptsForSource(repoPath, concepts) {
  const target = normalizeRepoPath(repoPath);
  return concepts.filter((concept) => (concept.frontmatter?.sources ?? []).some((entry) => entryCovers(entry, target)));
}

/**
 * The bundle as plain data: every docs file, each markdown file's frontmatter, headings and links, and the heading
 * anchors of markdown files outside docs/ that a doc links to (`../../CLAUDE.md#commands`).
 */
export function bundleModel(root = PROJECT_ROOT) {
  const files = listDocsFiles(root);
  const markdown = files.filter((file) => file.endsWith('.md')).map((file) => {
    const text = fs.readFileSync(path.join(root, file), 'utf8');
    let frontmatter = null;
    let frontmatterError = null;
    try {
      frontmatter = parseFrontmatter(text);
    } catch (error) {
      frontmatterError = error.message;
    }
    return { path: file, frontmatter, frontmatterError, headings: headingsOf(text), links: linksOf(text) };
  });
  const outsideAnchors = {};
  for (const doc of markdown) {
    for (const link of doc.links) {
      const target = path.posix.normalize(path.posix.join(path.posix.dirname(doc.path), link.path));
      if (!target.endsWith('.md') || target.startsWith('docs/') || target in outsideAnchors) continue;
      const file = path.join(root, target);
      if (fs.existsSync(file)) outsideAnchors[target] = headingsOf(fs.readFileSync(file, 'utf8')).map((heading) => heading.slug);
    }
  }
  return { files, markdown, outsideAnchors };
}

function main(argv) {
  const [mode, ...rest] = argv;
  if (mode === 'model') {
    const rootIndex = rest.indexOf('--root');
    const root = rootIndex === -1 ? PROJECT_ROOT : path.resolve(rest[rootIndex + 1]);
    process.stdout.write(JSON.stringify(bundleModel(root)));
    return 0;
  }
  if (mode !== 'sources' || rest.length === 0) {
    console.error('usage: node scripts/docs-bundle.mjs sources <repo path>...\n       node scripts/docs-bundle.mjs model [--root <dir>]');
    return 2;
  }
  const concepts = listConcepts();
  for (const repoPath of rest) {
    const covering = conceptsForSource(repoPath, concepts).map((concept) => concept.path);
    console.log(`${repoPath}: ${covering.length === 0 ? '(no concept lists it)' : covering.join(', ')}`);
  }
  return 0;
}

// Run only as a script, not when imported. Paths compare case-insensitively: Windows drive letters differ in case.
const invoked = process.argv[1] ? path.resolve(process.argv[1]).toLowerCase() : '';
if (invoked === fileURLToPath(import.meta.url).toLowerCase()) {
  process.exitCode = main(process.argv.slice(2));
}
