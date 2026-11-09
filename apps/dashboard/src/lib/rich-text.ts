import { InputRule, Node, type Extensions } from '@tiptap/core';
import { ListItem } from '@tiptap/extension-list';
import { DOMParser as SchemaParser, type Schema, type Slice } from '@tiptap/pm/model';
import type { EditorState } from '@tiptap/pm/state';
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

// An output, `{{ firstName }}`, or a logic tag, `{% if meetingLink != blank %}`. Both show as a chip, so a person editing an
// email sees "Meeting link" and "If there is a meeting link", never the braces. A piece with Liquid's whitespace control,
// `{%- ... -%}` or `{{- ... -}}`, stays text as written: a chip stores only what is inside and would drop the dashes.
// The last character inside may not be a dash, which keeps the expression free of lookbehind for older browsers.
const liquidPiece = /\{\{(?!-)\s*([^{}]*?[^{}\s-])\s*\}\}|\{%(?!-)\s*([^%]*?[^%\s-])\s*%\}/g;

/** The HTML with each `{{ ... }}` and `{% ... %}` in its text as a chip's element, so the editor loads it as one piece. */
export function withVariableChips(html: string): string {
  const document = new DOMParser().parseFromString(html, 'text/html');
  const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
  const texts: Text[] = [];
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    if (liquidPiece.test(node.nodeValue ?? '')) texts.push(node as Text);
    liquidPiece.lastIndex = 0;
  }

  for (const text of texts) {
    const value = text.nodeValue ?? '';
    const parts: (string | HTMLElement)[] = [];
    let start = 0;
    for (const match of value.matchAll(liquidPiece)) {
      parts.push(value.slice(start, match.index));
      const chip = document.createElement('span');
      if (match[1] !== undefined) chip.dataset['liquid'] = match[1].trim();
      else chip.dataset['liquidTag'] = match[2]!.trim();
      chip.textContent = match[0];
      parts.push(chip);
      start = match.index + match[0].length;
    }

    parts.push(value.slice(start));
    text.replaceWith(...parts);
  }

  return document.body.innerHTML;
}

/** Plain text as editor content: the text escaped, with each `{{ ... }}` in it as a chip's element. */
export function textWithChips(text: string): string {
  const paragraph = document.createElement('p');
  paragraph.textContent = text;
  return withVariableChips(paragraph.outerHTML);
}

/**
 * Parses a line of plain text with its spaces as written. By default the editor's parser collapses them, and the next
 * edit anywhere in the line would save the change.
 */
export const keepSpaces = { preserveWhitespace: 'full' } as const;

/**
 * Pasted text as a piece of one line: a line break becomes a space and each `{{ ... }}` a chip. A paste takes the
 * clipboard's plain text because the HTML of a rich copy holds paragraphs, of which a one-paragraph document keeps the
 * first, and line breaks, which it drops with no space between the words.
 */
export function pastedLine(text: string, schema: Schema): Slice {
  const line = document.createElement('div');
  line.innerHTML = textWithChips(text.replace(/\s*[\r\n]+\s*/g, ' '));
  return SchemaParser.fromSchema(schema).parseSlice(line, keepSpaces);
}

/** The editor's HTML as it is stored: each chip's element unwrapped to its Liquid text, which the server renders. */
export function withoutChipElements(html: string): string {
  if (!html.includes('data-liquid')) return html;
  const document = new DOMParser().parseFromString(html, 'text/html');
  for (const chip of document.body.querySelectorAll('span[data-liquid], span[data-liquid-tag]')) {
    const tag = chip.getAttribute('data-liquid-tag');
    chip.replaceWith(tag === null ? `{{ ${chip.getAttribute('data-liquid')} }}` : `{% ${tag} %}`);
  }

  return document.body.innerHTML;
}

/** The words a chip shows: a variable's label, or the expression itself when it is more than a name. */
export function chipLabel(expression: string, variables: readonly RichTextVariable[]): string {
  return variables.find((variable) => variable.name === expression)?.label ?? `{{ ${expression} }}`;
}

/**
 * The words a logic tag's chip shows. The one kind the built-in emails use, "show this only when there is a meeting link",
 * reads as it means; any other tag shows as written.
 */
export function tagLabel(tag: string, variables: readonly RichTextVariable[]): string {
  if (/^endif$/i.test(tag)) return 'End of that part';
  const condition = /^if\s+([A-Za-z_][\w]*)\s*(!=|==)\s*blank$/.exec(tag);
  if (!condition) return `{% ${tag} %}`;
  const label = (variables.find((variable) => variable.name === condition[1])?.label ?? condition[1]!).toLowerCase();
  return condition[2] === '!=' ? `If there is a ${label}` : `If there is no ${label}`;
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

  // A browser's own End stops before a chip that ends a line (it is not editable text), so a keyboard could never reach
  // the end of "Thanks, {{ organizationName }}". End and Ctrl or Cmd End go past it.
  addKeyboardShortcuts() {
    const pastChip = (to: (state: EditorState) => number) =>
      () => {
        const { state } = this.editor;
        const target = to(state);
        const before = state.doc.resolve(target).nodeBefore;
        if (before?.type !== this.type || state.selection.head === target) return false;
        return this.editor.commands.setTextSelection(target);
      };
    return {
      End: pastChip((state) => state.selection.$head.end()),
      'Mod-End': pastChip((state) => state.doc.content.size - 1),
    };
  },

  // A typed `{{ name }}` becomes a chip, braces and all. (Tiptap's `nodeInputRule` keeps the text before a capture
  // group, which would leave `{{ ` around the chip.)
  addInputRules() {
    const type = this.type;
    return [
      new InputRule({
        find: /\{\{(?!-)\s*([^{}]*?[^{}\s-])\s*\}\}$/,
        handler: ({ state, range, match }) => {
          state.tr.replaceWith(range.from, range.to, type.create({ expression: match[1]!.trim() }));
        },
      }),
    ];
  },
});

/**
 * A Liquid logic tag, `{% if meetingLink != blank %}`, as one piece the editor keeps intact, like a variable. It is stored
 * as the tag's own text; what it shows is `tagLabel`.
 */
export const LiquidTag = Node.create<VariableOptions>({
  name: 'liquidTag',
  group: 'inline',
  inline: true,
  atom: true,
  selectable: true,

  addOptions() {
    return { variables: () => [] };
  },

  addAttributes() {
    return {
      tag: {
        default: '',
        parseHTML: (element) => element.getAttribute('data-liquid-tag') ?? '',
        renderHTML: () => ({}),
      },
    };
  },

  parseHTML() {
    return [{ tag: 'span[data-liquid-tag]' }];
  },

  renderHTML({ node }) {
    return ['span', { 'data-liquid-tag': node.attrs['tag'] }, `{% ${node.attrs['tag']} %}`];
  },

  renderText({ node }) {
    return `{% ${node.attrs['tag']} %}`;
  },

  addNodeView() {
    return ({ node }) => {
      const dom = document.createElement('span');
      dom.dataset['liquidTag'] = node.attrs['tag'];
      dom.className = 'rounded-sm bg-secondary px-1 text-muted-foreground';
      const hidden = document.createElement('span');
      hidden.className = 'sr-only';
      hidden.textContent = 'condition ';
      dom.append(hidden, tagLabel(node.attrs['tag'], this.options.variables()));
      return { dom };
    };
  },
});

/** A document of one paragraph that Enter leaves alone: a single line of text, such as an email's subject. */
const SingleLine = Node.create({
  name: 'doc',
  topNode: true,
  content: 'paragraph',
  addKeyboardShortcuts() {
    return { Enter: () => true };
  },
});

/** The extensions of a single line of plain text whose `{{ name }}` shows as a chip (`SubjectEditor`). */
export function singleLineExtensions(variables: () => readonly RichTextVariable[]): Extensions {
  return [
    StarterKit.configure({
      document: false,
      blockquote: false,
      bold: false,
      bulletList: false,
      code: false,
      codeBlock: false,
      dropcursor: false,
      gapcursor: false,
      hardBreak: false,
      heading: false,
      horizontalRule: false,
      italic: false,
      link: false,
      listItem: false,
      listKeymap: false,
      orderedList: false,
      strike: false,
      trailingNode: false,
      underline: false,
    }),
    SingleLine,
    Variable.configure({ variables }),
    LiquidTag.configure({ variables }),
  ];
}

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
    ...(variables ? [Variable.configure({ variables }), LiquidTag.configure({ variables })] : []),
  ];
}
