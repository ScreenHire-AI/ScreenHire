import type { Editor } from '@tiptap/core';
import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { preferences } from '#lib/preferences.svelte.js';
import { isAllowedLink, isBlank, normalizeLinkUrl, withVariableChips } from '#lib/rich-text.js';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import Harness from '../../test-support/rich-text-editor-harness.svelte';

const variables = [
  { name: 'firstName', label: 'First name' },
  { name: 'jobTitle', label: 'Job title' },
];

const textbox = () => screen.getByRole('textbox', { name: 'Message' });

/** Selects all of the editor's text, as a person dragging over it would. */
function selectAll() {
  textbox().focus();
  const selection = window.getSelection()!;
  selection.selectAllChildren(textbox());
  document.dispatchEvent(new Event('selectionchange'));
}

afterEach(() => preferences.setShortcutsEnabled(true));

describe('rich text editor', () => {
  it('shows the HTML it is given, labelled, with a formatting toolbar', async () => {
    render(Harness, { props: { value: '<h2>About</h2><p>We <strong>build</strong>.</p><ul><li>Remote</li></ul>' } });

    expect(textbox()).toHaveAttribute('aria-multiline', 'true');
    expect(textbox().querySelector('h2')).toHaveTextContent('About');
    expect(textbox().querySelector('strong')).toHaveTextContent('build');
    expect(screen.getByRole('toolbar', { name: 'Formatting' })).toHaveAttribute('aria-controls', 'message');
    for (const name of ['Bold', 'Italic', 'Heading', 'Subheading', 'Bulleted list', 'Numbered list', 'Link']) {
      expect(screen.getByRole('button', { name })).toBeInTheDocument();
    }

    await expectNoAxeViolations(document.body);
  });

  it('never writes the value back until the text changes', async () => {
    const onvalue = vi.fn();
    render(Harness, { props: { value: '<p>Untouched   <b>markup</b></p>', onvalue } });

    await screen.findByRole('toolbar');
    expect(onvalue).toHaveBeenLastCalledWith('<p>Untouched   <b>markup</b></p>');
  });

  it('formats the selection from the toolbar and shows which formats it has', async () => {
    const onvalue = vi.fn();
    render(Harness, { props: { value: '<p>Hello</p>', onvalue } });
    selectAll();

    await fireEvent.click(screen.getByRole('button', { name: 'Bold' }));

    await waitFor(() => expect(onvalue).toHaveBeenLastCalledWith('<p><strong>Hello</strong></p>'));
    expect(screen.getByRole('button', { name: 'Bold' })).toHaveAttribute('aria-pressed', 'true');

    await fireEvent.click(screen.getByRole('button', { name: 'Heading' }));
    await waitFor(() => expect(onvalue).toHaveBeenLastCalledWith('<h2><strong>Hello</strong></h2>'));

    await fireEvent.click(screen.getByRole('button', { name: 'Numbered list' }));
    await waitFor(() => expect(onvalue).toHaveBeenLastCalledWith(expect.stringContaining('<ol><li>')));
  });

  it('is one tab stop, and the arrow keys move between its buttons', async () => {
    render(Harness, { props: { value: '<p>Hi</p>' } });
    const bold = screen.getByRole('button', { name: 'Bold' });
    const italic = screen.getByRole('button', { name: 'Italic' });
    const link = screen.getByRole('button', { name: 'Link' });

    expect(bold).toHaveAttribute('tabindex', '0');
    expect(italic).toHaveAttribute('tabindex', '-1');
    bold.focus();
    await fireEvent.keyDown(bold, { key: 'ArrowRight' });
    expect(italic).toHaveFocus();
    await fireEvent.keyDown(italic, { key: 'End' });
    expect(link).toHaveFocus();
    await fireEvent.keyDown(link, { key: 'ArrowRight' });
    expect(bold).toHaveFocus();
  });

  it('links the selection to an address, and refuses one that is not a web or email address', async () => {
    const onvalue = vi.fn();
    render(Harness, { props: { value: '<p>Our site</p>', onvalue } });
    selectAll();

    await fireEvent.click(screen.getByRole('button', { name: 'Link' }));
    const field = await screen.findByLabelText('Link address');
    await fireEvent.input(field, { target: { value: 'javascript:alert(1)' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Apply' }));
    expect(await screen.findByText('Enter a web address or an email address.')).toBeInTheDocument();

    await fireEvent.input(field, { target: { value: 'example.com/jobs' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Apply' }));

    await waitFor(() => expect(onvalue).toHaveBeenLastCalledWith('<p><a href="https://example.com/jobs">Our site</a></p>'));
  });

  it('shows a variable as a chip with its label, and writes it as Liquid', async () => {
    const onvalue = vi.fn();
    render(Harness, { props: { value: '<p>Hi {{ firstName }}, about {{ jobTitle | upcase }}</p>', variables, onvalue } });

    const chips = textbox().querySelectorAll('[data-liquid]');
    expect([...chips].map((chip) => chip.textContent)).toEqual(['variable First name', 'variable {{ jobTitle | upcase }}']);

    textbox().focus();
    await fireEvent.click(screen.getByRole('button', { name: 'Insert first name' }));

    await waitFor(() => expect(onvalue).toHaveBeenLastCalledWith(expect.stringMatching(/\{\{ firstName \}\}.*\{\{ firstName \}\}|\{\{ firstName \}\}\{\{ firstName \}\}/)));
    expect(onvalue.mock.lastCall![0]).not.toContain('data-liquid');
  });

  it('turns a typed variable into one chip, braces and all', async () => {
    const onvalue = vi.fn();
    render(Harness, { props: { value: '<p>Hi</p>', variables, onvalue } });
    const editor = (textbox() as HTMLElement & { editor: Editor }).editor;
    editor.commands.setTextSelection(editor.state.doc.content.size - 1);

    // ProseMirror runs input rules as text is typed, one key at a time, at the caret.
    for (const key of ' {{ firstName }}') {
      const { view } = editor;
      const { from, to } = view.state.selection;
      const handled = view.someProp('handleTextInput', (handle) => handle(view, from, to, key, () => view.state.tr.insertText(key, from, to)));
      if (!handled) view.dispatch(view.state.tr.insertText(key, from, to));
    }

    await waitFor(() => expect(onvalue).toHaveBeenLastCalledWith('<p>Hi {{ firstName }}</p>'));
    expect(textbox().querySelectorAll('[data-liquid]')).toHaveLength(1);
    expect(textbox()).toHaveTextContent('Hi variable First name');
  });

  it('loads a value set from outside without counting it as an edit', async () => {
    const onvalue = vi.fn();
    render(Harness, { props: { value: '<p>Blank</p>', onvalue } });

    await fireEvent.click(screen.getByRole('button', { name: 'Start from a template' }));

    await waitFor(() => expect(textbox()).toHaveTextContent('From a template'));
    expect(onvalue).toHaveBeenLastCalledWith('<p>From a template</p>');
  });

  it('turns typed formatting off with single-key shortcuts', async () => {
    preferences.setShortcutsEnabled(false);
    render(Harness, { props: { value: '<p>Kept</p>' } });
    // Tiptap puts its editor on the text area's element.
    const options = () => (textbox() as HTMLElement & { editor: { options: { enableInputRules: boolean; enablePasteRules: boolean } } }).editor.options;

    expect(options().enableInputRules).toBe(false);
    expect(options().enablePasteRules).toBe(false);

    preferences.setShortcutsEnabled(true);
    await waitFor(() => expect(options().enableInputRules).toBe(true));
    expect(textbox()).toHaveTextContent('Kept');
  });

  it('shows the content read-only, without the toolbar', () => {
    render(Harness, { props: { value: '<p>Read me</p>', editable: false } });

    expect(textbox()).toHaveAttribute('contenteditable', 'false');
    expect(screen.queryByRole('toolbar')).not.toBeInTheDocument();
  });
});

describe('rich text helpers', () => {
  it('takes only absolute web and email addresses with no variable in them', () => {
    expect(isAllowedLink('https://example.com')).toBe(true);
    expect(isAllowedLink('mailto:jobs@example.com')).toBe(true);
    expect(isAllowedLink('javascript:alert(1)')).toBe(false);
    expect(isAllowedLink('/careers')).toBe(false);
    expect(isAllowedLink('https://example.com/?n={{ firstName }}')).toBe(false);
    expect(normalizeLinkUrl('example.com')).toBe('https://example.com');
    expect(normalizeLinkUrl('jobs@example.com')).toBe('mailto:jobs@example.com');
    expect(normalizeLinkUrl('  ')).toBeNull();
  });

  it('knows an empty editor from one with words', () => {
    expect(isBlank('<p></p>')).toBe(true);
    expect(isBlank('<h2> </h2><ul><li>&nbsp;</li></ul>')).toBe(true);
    expect(isBlank('<p>Hi</p>')).toBe(false);
  });

  it('wraps each Liquid output in a chip element', () => {
    expect(withVariableChips('<p>Hi {{firstName}} &amp; {{ jobTitle }}</p>')).toBe(
      '<p>Hi <span data-liquid="firstName">{{firstName}}</span> &amp; <span data-liquid="jobTitle">{{ jobTitle }}</span></p>',
    );
  });
});
