<script lang="ts">
  import { Editor } from '@tiptap/core';
  import { untrack } from 'svelte';
  import { keepSpaces, pastedLine, singleLineExtensions, textWithChips, type RichTextVariable } from '#lib/rich-text.js';
  import { cn } from '#lib/utils.js';

  // An email template's subject: one line of plain text in which each `{{ name }}` shows as the same chip the message
  // uses (design system section 11, Rich text editor). `value` is plain text with the Liquid in it, as the API stores it,
  // and is written only when the person changes the text, so opening a form never makes it dirty. It is a text box
  // around a single-paragraph editor: Enter does nothing, and a paste is plain text whose line breaks become spaces.
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
    value?: string;
    /** The text box's id, for the label. */
    id: string;
    labelledBy?: string;
    describedBy?: string;
    invalid?: boolean;
    /** False shows the subject as read-only text, still with its chips. */
    editable?: boolean;
    variables: readonly RichTextVariable[];
    onfocus?: () => void;
    class?: string;
  } = $props();

  let editor = $state.raw<Editor | null>(null);
  // What the editor last wrote to `value` or loaded from it; anything else in `value` came from outside.
  let emitted = untrack(() => value);
  // The variables by what they say, so a refetched list with the same names and labels does not make the editor again.
  const variableKey = $derived(JSON.stringify(variables.map(({ name, label }) => [name, label])));

  // Building the editor runs the chips' node views, so it reads nothing that would make it again on every change.
  function mount(element: HTMLElement) {
    void variableKey;
    const readOnly = !editable;
    return untrack(() => create(element, readOnly));
  }

  function create(element: HTMLElement, readOnly: boolean) {
    const instance = new Editor({
      element,
      extensions: singleLineExtensions(() => variables),
      content: textWithChips(value),
      parseOptions: keepSpaces,
      editable: !readOnly,
      editorProps: {
        attributes: {
          id,
          role: 'textbox',
          'aria-multiline': 'false',
          ...(readOnly ? { 'aria-readonly': 'true' } : {}),
          // Editing keeps one line that scrolls, chips included (ProseMirror lets a chip's words wrap); reading wraps, as
          // the message does.
          class: readOnly ? 'min-w-0 break-words outline-none' : 'min-w-0 whitespace-pre! outline-none [&_[data-liquid]]:whitespace-nowrap!',
        },
        handlePaste: (view, event) => {
          const text = event.clipboardData?.getData('text/plain');
          if (!text) return false;
          view.dispatch(view.state.tr.replaceSelection(pastedLine(text, view.state.schema)).scrollIntoView());
          return true;
        },
      },
      onUpdate: ({ editor: changed }) => {
        emitted = changed.getText();
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

  // A value set from outside (Reset to original) replaces the text without counting as an edit.
  $effect(() => {
    const next = value;
    if (editor && next !== emitted) {
      emitted = next;
      editor.commands.setContent(textWithChips(next), { emitUpdate: false, parseOptions: keepSpaces });
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

<!-- The size and border of a text field (the Input), around the editor. A long subject scrolls inside it (F5) and, as in
     an Input, the caret shows where it has got to: a scrollbar would take the line's height. -->
<div
  class={cn(
    'flex h-9 w-full min-w-0 items-center overflow-x-auto overflow-y-hidden rounded-lg border border-input bg-transparent px-2.5 text-body transition-colors [scrollbar-width:none] max-md:text-reading pointer-coarse:h-11 pointer-coarse:text-reading dark:bg-input/30',
    editable && 'focus-within:border-ring focus-within:outline-2 focus-within:outline-offset-2 focus-within:outline-ring',
    invalid && 'border-destructive ring-3 ring-destructive/20 dark:ring-destructive/40',
    className,
  )}
>
  <div class="min-w-0 flex-1" {@attach mount}></div>
</div>
