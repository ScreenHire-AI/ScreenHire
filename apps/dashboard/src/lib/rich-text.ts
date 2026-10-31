import { InputRule, Node, type Extensions } from '@tiptap/core';
import { ListItem } from '@tiptap/extension-list';
import StarterKit from '@tiptap/starter-kit';

/**
 * Rich text for job descriptions and email bodies (docs/architecture/rich-text.md): the Tiptap setup behind
 * `RichTextEditor`. Its elements are the server's allow-list (p, br, strong, em, h2, h3, ul, ol, li and links), so
 * what the editor writes comes back from the API as it was written.
 */

/** A template variable as the API lists it: its Liquid name and the words people see. */
export interface RichTextVariable {
  name: string;
  label: string;
}

const linkSchemes = ['http:', 'https:', 'mailto:'];

/**
 * Whether `url` can be a link's address: absolute, on http, https or mailto, with no Liquid in it, because a candidate's
 * details never go to another site inside a link (the server refuses it too).
 */
export function isAllowedLink(url: string): boolean {
  if (/\{\{|\{%|%7B%7B|%7B%25/i.test(url)) return false;
  try {
    return linkSchemes.includes(new URL(url).protocol);
  } catch {
    return false;
  }
}

/** What a person typed in the link field as an address: `https://` before a bare host, `mailto:` before an email. */
export function normalizeLinkUrl(input: string): string | null {
  const text = input.trim();
  if (!text) return null;
  const withScheme = /^[a-z][a-z0-9+.-]*:(?!\d)/i.test(text) ? text : /^[^\s/@]+@[^\s/@]+$/.test(text) ? `mailto:${text}` : `https://${text}`;
  return isAllowedLink(withScheme) ? withScheme : null;
}

/** Whether rich text has no words, only markup: an empty editor writes `<p></p>`. */
export function isBlank(html: string | null | undefined): boolean {
  if (!html) return true;
  const text = new DOMParser().parseFromString(html, 'text/html').body.textContent ?? '';
  return text.replace(/ /g, ' ').trim().length === 0;
}

const liquidOutput = /\{\{\s*([^{}]*?)\s*\}\}/g;

/** The HTML with each `{{ ... }}` in its text as a chip's element, so the editor loads it as one piece. */
export function withVariableChips(html: string): string {
  const document = new DOMParser().parseFromString(html, 'text/html');
  const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
  const texts: Text[] = [];
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    if (liquidOutput.test(node.nodeValue ?? '')) texts.push(node as Text);
    liquidOutput.lastIndex = 0;
  }

  for (const text of texts) {
    const value = text.nodeValue ?? '';
    const parts: (string | HTMLElement)[] = [];
    let start = 0;
    for (const match of value.matchAll(liquidOutput)) {
      parts.push(value.slice(start, match.index));
      const chip = document.createElement('span');
      chip.dataset['liquid'] = match[1]!.trim();
      chip.textContent = match[0];
      parts.push(chip);
      start = match.index + match[0].length;
    }

    parts.push(value.slice(start));
    text.replaceWith(...parts);
  }

  return document.body.innerHTML;
}

/** The editor's HTML as it is stored: each chip's element unwrapped to its Liquid text, which the server renders. */
export function withoutChipElements(html: string): string {
  if (!html.includes('data-liquid')) return html;
  const document = new DOMParser().parseFromString(html, 'text/html');
  for (const chip of document.body.querySelectorAll('span[data-liquid]')) {
    chip.replaceWith(`{{ ${chip.getAttribute('data-liquid')} }}`);
  }

  return document.body.innerHTML;
}

/** The words a chip shows: a variable's label, or the expression itself when it is more than a name. */
export function chipLabel(expression: string, variables: readonly RichTextVariable[]): string {
  return variables.find((variable) => variable.name === expression)?.label ?? `{{ ${expression} }}`;
}

interface VariableOptions {
  variables: () => readonly RichTextVariable[];
}

/**
 * A Liquid output, `{{ firstName }}`, as one piece the editor keeps intact: a muted word that is deleted, moved and
 * copied whole. Its element unwraps to the Liquid text (`withoutChipElements`), which the server stores and renders.
 */
export const Variable = Node.create<VariableOptions>({
  name: 'variable',
  group: 'inline',
  inline: true,
  atom: true,
  selectable: true,

  addOptions() {
    return { variables: () => [] };
  },

  addAttributes() {
    return {
      expression: {
        default: '',
        parseHTML: (element) => element.getAttribute('data-liquid') ?? '',
        renderHTML: () => ({}),
      },
    };
  },

  parseHTML() {
    return [{ tag: 'span[data-liquid]' }];
  },

  renderHTML({ node }) {
    return ['span', { 'data-liquid': node.attrs['expression'] }, `{{ ${node.attrs['expression']} }}`];
  },

  renderText({ node }) {
    return `{{ ${node.attrs['expression']} }}`;
  },

  addNodeView() {
    return ({ node }) => {
      const dom = document.createElement('span');
      dom.dataset['liquid'] = node.attrs['expression'];
      dom.className = 'rounded-sm bg-secondary px-1 text-muted-foreground';
      const hidden = document.createElement('span');
      hidden.className = 'sr-only';
      hidden.textContent = 'variable ';
      dom.append(hidden, chipLabel(node.attrs['expression'], this.options.variables()));
      return { dom };
    };
  },

  // A typed `{{ name }}` becomes a chip, braces and all. (Tiptap's `nodeInputRule` keeps the text before a capture
  // group, which would leave `{{ ` around the chip.)
  addInputRules() {
    const type = this.type;
    return [
      new InputRule({
        find: /\{\{\s*([^{}]+?)\s*\}\}$/,
        handler: ({ state, range, match }) => {
          state.tr.replaceWith(range.from, range.to, type.create({ expression: match[1]!.trim() }));
        },
      }),
    ];
  },
});

/**
 * The editor's extensions: the toolbar's marks and blocks and nothing else. Headings are h2 and h3 (a careers page's h1
 * is the job title), lists do not nest and Tab always leaves the editor (no keyboard trap in a form), and links open
 * nothing while editing. `variables` adds the chips, for email bodies.
 */
export function richTextExtensions(variables?: () => readonly RichTextVariable[]): Extensions {
  return [
    StarterKit.configure({
      blockquote: false,
      code: false,
      codeBlock: false,
      horizontalRule: false,
      listItem: false,
      strike: false,
      trailingNode: false,
      underline: false,
      heading: { levels: [2, 3] },
      link: {
        openOnClick: false,
        autolink: true,
        defaultProtocol: 'https',
        HTMLAttributes: { target: null, rel: null },
        isAllowedUri: (url) => isAllowedLink(url),
      },
    }),
    ListItem.extend({
      addKeyboardShortcuts() {
        return { Enter: () => this.editor.commands.splitListItem(this.name) };
      },
    }),
    ...(variables ? [Variable.configure({ variables })] : []),
  ];
}
