<script lang="ts">
  import { Editor } from '@tiptap/core';
  import { untrack } from 'svelte';
  import RichTextToolbar, { type RichTextActive } from '#lib/components/rich-text-toolbar.svelte';
  import { preferences } from '#lib/preferences.svelte.js';
  import { richTextExtensions, withoutChipElements, withVariableChips, type RichTextVariable } from '#lib/rich-text.js';
  import { cn } from '#lib/utils.js';

  // The rich text editor (design system section 11): a job's description, an email template's message and the email
  // composer's. It edits sanitized HTML, the stored format (docs/architecture/rich-text.md), and writes `value` only when
  // the person changes the text, so an untouched editor hands back exactly what it was given: opening a form never makes
  // it dirty, and an unchanged description goes back as it was. With `variables`, `{{ name }}` shows as a chip.
  let {
    value = $bindable(''),
    id,
    labelledBy,
    describedBy,
    invalid = false,
    editable = true,
    variables,
    onfocus,
    class: className,
  }: {
    /** The HTML. An editor with no text writes "". */
    value?: string;
    /** The text area's id, for `aria-controls` and the label. */
    id: string;
    labelledBy?: string;
    describedBy?: string;
    invalid?: boolean;
    /** False shows the content read-only, without the toolbar. */
    editable?: boolean;
    variables?: readonly RichTextVariable[];
    onfocus?: () => void;
    class?: string;
  } = $props();

  let editor = $state.raw<Editor | null>(null);
  let active = $state<RichTextActive>({
    bold: false,
    italic: false,
    heading: false,
    subheading: false,
    bulletList: false,
    orderedList: false,
    link: false,
  });

  // What the editor last wrote to `value` or loaded from it; anything else in `value` came from outside.
  let emitted = untrack(() => value);
  // The variables by what they say, so a refetched list with the same names and labels does not make the editor again;
  // a changed one does, and its chips show the new labels.
  const variableKey = $derived(variables === undefined ? undefined : JSON.stringify(variables.map(({ name, label }) => [name, label])));
  const content = (html: string, chips: boolean) => (chips ? withVariableChips(html) : html);

  function refresh(instance: Editor) {
    active = {
      bold: instance.isActive('bold'),
      italic: instance.isActive('italic'),
      heading: instance.isActive('heading', { level: 2 }),
      subheading: instance.isActive('heading', { level: 3 }),
      bulletList: instance.isActive('bulletList'),
      orderedList: instance.isActive('orderedList'),
      link: instance.isActive('link'),
    };
  }

  // Typed formatting ("- " starts a list, "## " a heading) acts like a single-key shortcut for speech input, so it is
  // off while single-key shortcuts are (WCAG 2.1.4). Tiptap builds those rules once, so the attachment reads the
  // preference and a change makes the editor again with the text it holds. It reads nothing else: building the editor
  // runs the chips' node views, and a value they read would make the editor again on every change, losing the caret.
  function mount(element: HTMLElement) {
    const typedFormatting = preferences.shortcutsEnabled;
    const readOnly = !editable;
    const withChips = variableKey !== undefined;
    return untrack(() => create(element, typedFormatting, readOnly, withChips));
  }

  function create(element: HTMLElement, typedFormatting: boolean, readOnly: boolean, withChips: boolean) {
    const instance = new Editor({
      element,
      extensions: richTextExtensions(withChips ? () => variables ?? [] : undefined),
      content: content(value, withChips),
      editable: !readOnly,
      enableInputRules: typedFormatting,
      enablePasteRules: typedFormatting,
      editorProps: {
        attributes: {
          id,
          role: 'textbox',
          'aria-multiline': 'true',
          ...(readOnly ? { 'aria-readonly': 'true' } : {}),
          class: cn(
            'min-h-32 px-2.5 py-2 outline-none max-md:text-reading pointer-coarse:text-reading',
            '[&_a]:underline [&_h2]:mt-3 [&_h2]:text-heading [&_h3]:mt-2 [&_h3]:font-semibold [&_li]:mt-1 [&_ol]:list-decimal [&_ol]:pl-5',
            '[&_p+p]:mt-2 [&_ul]:list-disc [&_ul]:pl-5 [&>*:first-child]:mt-0 [&_ol]:mt-2 [&_ul]:mt-2',
          ),
        },
      },
      onCreate: ({ editor: created }) => refresh(created),
      onTransaction: ({ editor: changed }) => refresh(changed),
      onUpdate: ({ editor: changed }) => {
        emitted = changed.isEmpty ? '' : withoutChipElements(changed.getHTML());
        value = emitted;
      },
      onFocus: () => onfocus?.(),
    });
    editor = instance;
    return () => {
      instance.destroy();
      editor = null;
    };
  }

  // The label, the description and the error state change without making the editor again, which would lose the caret.
  $effect(() => {
    const dom = editor?.view.dom;
    if (!dom) return;
    for (const [name, attribute] of [
      ['aria-labelledby', labelledBy],
      ['aria-describedby', describedBy],
      ['aria-invalid', invalid ? 'true' : undefined],
    ] as const) {
      if (attribute) dom.setAttribute(name, attribute);
      else dom.removeAttribute(name);
    }
  });

  // A value set from outside (Start from in the composer, Reset to original) replaces the text without counting as an
  // edit.
  $effect(() => {
    const next = value;
    if (editor && next !== emitted) {
      emitted = next;
      editor.commands.setContent(content(next, variables !== undefined), { emitUpdate: false });
    }
  });

  /** Puts a variable's chip at the selection, as one piece. */
  export function insertVariable(name: string) {
    editor?.chain().focus().insertContent({ type: 'variable', attrs: { expression: name } }).run();
  }

  export function focus() {
    editor?.commands.focus();
  }
</script>

<div
  class={cn(
    'flex w-full min-w-0 flex-col rounded-lg border border-input bg-transparent text-body transition-colors dark:bg-input/30',
    editable && 'focus-within:border-ring focus-within:outline-2 focus-within:outline-offset-2 focus-within:outline-ring',
    invalid && 'border-destructive ring-3 ring-destructive/20 dark:ring-destructive/40',
    className,
  )}
>
  {#if editable && editor}
    <RichTextToolbar {editor} {active} controls={id} />
  {/if}
  <div class="min-w-0 [&_.ProseMirror]:min-w-0 [&_.ProseMirror]:break-words" {@attach mount}></div>
</div>
