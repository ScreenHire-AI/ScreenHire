import type { EmailTemplateEntry, EmailTemplateVariableEntry } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import Harness from '../../test-support/email-template-form-harness.svelte';
import { apiStub } from '../../test-support/fixtures.js';
import { inHiddenPane } from '../../test-support/panes.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import { editRichText } from '../../test-support/rich-text.js';

const variables: EmailTemplateVariableEntry[] = [
  { name: 'candidateName', label: 'Candidate name', rejectOnly: false },
  { name: 'jobTitle', label: 'Job title', rejectOnly: false },
  { name: 'reason', label: 'Reason', rejectOnly: true },
];

const template: EmailTemplateEntry = {
  id: 'stage-change',
  name: 'Moved to the next step',
  builtIn: true,
  edited: false,
  rejectOnly: false,
  subject: 'An update on {{ jobTitle }}',
  body: 'Hi {{ candidateName }},',
  updatedAt: null,
  lastUpdated: { at: null, by: 'ScreenHire' },
  questionnaireOnly: false,
  archived: false,
};

// The preview renders what the API returns for the wording it was sent.
function renderForm(props: Record<string, unknown> = {}) {
  const previews: { subject: string; body: string; templateId?: string | null }[] = [];
  const tests: unknown[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(
      apiStub({
        'POST /v1/email-templates/preview': (init) => {
          const draft = JSON.parse(String(init?.body)) as { subject: string; body: string };
          previews.push(draft);
          if (draft.subject.includes('{{ nope }}')) {
            return Response.json({ errors: { subject: ['There is no variable "nope".'] } }, { status: 400 });
          }
          return Response.json({
            subject: draft.subject.replace('{{ jobTitle }}', 'Product designer'),
            html: '<p>Hi Jordan Lee,</p>',
            text: draft.body.replace('{{ candidateName }}', 'Jordan Lee'),
          });
        },
        'POST /v1/email-templates/test': (init) => {
          tests.push(JSON.parse(String(init?.body)));
          return Response.json({ to: 'avery@example.com' });
        },
      }),
    ),
  );
  const onsave = vi.fn(async () => {});
  render(WithQueryClient, { props: { component: Harness, props: { template, variables, sendingEnabled: true, onsave, ...props } } });
  return { previews, tests, onsave };
}

const view = () => screen.getByRole('group', { name: 'View' });
const showPreview = () => fireEvent.click(within(view()).getByRole('button', { name: 'Preview' }));
const showEdit = () => fireEvent.click(within(view()).getByRole('button', { name: 'Edit' }));

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('email template form', () => {
  it('shows the wording and the email side by side where both fit, else one at a time, and the preview follows the wording (A8)', async () => {
    const { previews } = renderForm();
    const pane = screen.getByRole('region', { name: 'Preview' }).closest('[data-preview-pane]');
    // Container queries put the panes side by side in a wide container; jsdom has none, so this is the narrow layout.
    expect(pane).toHaveClass('@[82rem]/editor:flex');
    expect(inHiddenPane(pane)).toBe(true);
    await waitFor(() => expect(previews).toHaveLength(1));
    expect(previews[0]).toEqual({ subject: 'An update on {{ jobTitle }}', body: 'Hi {{ candidateName }},', templateId: 'stage-change' });

    await fireEvent.input(screen.getByLabelText('Subject'), { target: { value: 'News about {{ jobTitle }}' } });
    await showPreview();
    const preview = screen.getByRole('region', { name: 'Preview' });
    expect(inHiddenPane(preview)).toBe(false);
    expect(inHiddenPane(screen.getByLabelText('Subject'))).toBe(true);
    expect(await within(preview).findByText('News about Product designer')).toBeInTheDocument();

    await fireEvent.click(within(preview).getByRole('button', { name: 'Plain text' }));
    expect(preview.querySelector('[data-email-text]')).toHaveTextContent('Hi Jordan Lee,');
    await expectNoAxeViolations();
  });

  it('says what is wrong with wording the API refuses', async () => {
    renderForm();
    await fireEvent.input(screen.getByLabelText('Subject'), { target: { value: 'Hi {{ nope }}' } });
    await showPreview();

    expect(await screen.findByText('There is no variable "nope".')).toBeInTheDocument();
  });

  it('inserts a variable at the caret of the field last focused, and offers the reason only to the rejection email', async () => {
    renderForm();
    const subject = screen.getByLabelText<HTMLInputElement>('Subject');
    await fireEvent.focus(subject);
    subject.setSelectionRange(0, 0);

    await fireEvent.pointerDown(screen.getByRole('button', { name: 'Insert variable' }), { button: 0, pointerType: 'mouse' });
    const menu = await screen.findByRole('menu');
    expect(within(menu).queryByRole('menuitem', { name: 'Reason' })).not.toBeInTheDocument();
    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Candidate name' }));

    await waitFor(() => expect(subject).toHaveValue('{{ candidateName }}An update on {{ jobTitle }}'));
    expect(screen.queryByLabelText('Name')).not.toBeInTheDocument();
  });

  it('shows each variable in the message as a chip, and puts a picked one there as a chip too', async () => {
    renderForm();
    const message = screen.getByRole('textbox', { name: 'Message' });
    expect([...message.querySelectorAll('[data-liquid]')].map((chip) => chip.textContent)).toEqual(['variable Candidate name']);

    message.focus();
    await fireEvent.focus(message);
    await fireEvent.pointerDown(screen.getByRole('button', { name: 'Insert variable' }), { button: 0, pointerType: 'mouse' });
    await fireEvent.click(within(await screen.findByRole('menu')).getByRole('menuitem', { name: 'Job title' }));

    await waitFor(() => expect(message.querySelectorAll('[data-liquid]')).toHaveLength(2));
    expect([...message.querySelectorAll('[data-liquid]')].map((chip) => chip.textContent)).toContain('variable Job title');
  });

  it('sends a test of the unsaved wording to the signed-in person from Preview', async () => {
    const { tests } = renderForm();
    await showPreview();

    await fireEvent.click(screen.getByRole('button', { name: 'Send me a test' }));

    expect(await screen.findByText('Sent a test to avery@example.com.')).toBeInTheDocument();
    expect(tests).toEqual([{ subject: 'An update on {{ jobTitle }}', body: 'Hi {{ candidateName }},', templateId: 'stage-change' }]);
  });

  it('says a new template has nothing to preview until it has words, and turns the test send on with them', async () => {
    const { previews } = renderForm({ template: undefined });
    await showPreview();
    const preview = screen.getByRole('region', { name: 'Preview' });

    expect(within(preview).getByText('Write a subject and a message to see the email.')).toBeInTheDocument();
    expect(within(preview).getByRole('button', { name: 'Send me a test' })).toBeDisabled();
    expect(previews).toHaveLength(0);

    await showEdit();
    await fireEvent.input(screen.getByLabelText('Subject'), { target: { value: 'An update on {{ jobTitle }}' } });
    await showPreview();
    expect(await within(screen.getByRole('region', { name: 'Preview' })).findByText('An update on Product designer')).toBeInTheDocument();
    expect(within(screen.getByRole('region', { name: 'Preview' })).getByRole('button', { name: 'Send me a test' })).toBeEnabled();
  });

  it('saves from outside the form in either view, going back to Edit when a new template needs its name', async () => {
    const { onsave } = renderForm({ template: undefined, sendingEnabled: false });
    await fireEvent.input(screen.getByLabelText('Subject'), { target: { value: 'Hello' } });
    editRichText(screen.getByRole('textbox', { name: 'Message' }), '<p>Hi <strong>there</strong></p>');
    await showPreview();
    expect(screen.queryByRole('button', { name: 'Send me a test' })).not.toBeInTheDocument();

    await fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Enter a name.')).toBeInTheDocument();
    await fireEvent.input(screen.getByLabelText('Name'), { target: { value: 'Interview invitation' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(onsave).toHaveBeenCalledWith({ name: 'Interview invitation', subject: 'Hello', body: '<p>Hi <strong>there</strong></p>' }));
  });

  it('reads as labels and values without the role to change it (E3)', async () => {
    renderForm({ editable: false });

    expect(screen.getAllByRole('textbox')).toHaveLength(1);
    expect(screen.getByRole('textbox', { name: 'Message' })).toHaveAttribute('aria-readonly', 'true');
    expect(screen.queryByRole('toolbar')).toBeNull();
    expect(screen.getByText('An update on {{ jobTitle }}')).toBeInTheDocument();
    await showPreview();
    expect(screen.queryByRole('button', { name: 'Send me a test' })).toBeNull();
  });
});
