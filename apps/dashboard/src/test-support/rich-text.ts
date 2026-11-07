import type { Editor } from '@tiptap/core';
import { textWithChips } from '#lib/rich-text.js';

/**
 * Writes `html` into a rich text editor as a person's edit: Tiptap keeps its editor on the text area's element, and
 * jsdom cannot type into a contenteditable, so the test replaces the content through the editor and lets it report the
 * change as it would after typing.
 */
export function editRichText(textbox: HTMLElement, html: string): void {
  const editor = (textbox as HTMLElement & { editor?: Editor }).editor;
  if (!editor) throw new Error('Not a rich text editor.');
  editor.commands.setContent(html, { emitUpdate: true });
}

/** The Tiptap editor behind a rich text editor's or a subject's text box. */
export function editorOf(textbox: HTMLElement): Editor {
  const editor = (textbox as HTMLElement & { editor?: Editor }).editor;
  if (!editor) throw new Error('Not a rich text editor.');
  return editor;
}

/** Types `text` into a subject, as a person's edit: each `{{ name }}` in it becomes a chip. */
export function editSubject(textbox: HTMLElement, text: string): void {
  editorOf(textbox).commands.setContent(textWithChips(text), { emitUpdate: true });
}
