import type { EmailTemplateEntry, EmailTemplateVariableEntry } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import Harness from '../../test-support/email-template-form-harness.svelte';
import { apiStub } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';

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
  it('shows the wording or the email, never both, and the preview follows the wording with the sample candidate (A8)', async () => {
    const { previews } = renderForm();
    expect(screen.queryByRole('region', { name: 'Preview' })).toBeNull();
    await waitFor(() => expect(previews).toHaveLength(1));
    expect(previews[0]).toEqual({ subject: 'An update on {{ jobTitle }}', body: 'Hi {{ candidateName }},', templateId: 'stage-change' });

    await fireEvent.input(screen.getByLabelText('Subject'), { target: { value: 'News about {{ jobTitle }}' } });
    await showPreview();
    const preview = screen.getByRole('region', { name: 'Preview' });
    expect(screen.queryByLabelText('Subject')).toBeNull();
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
    await fireEvent.input(screen.getByLabelText('Message'), { target: { value: 'Hi there' } });
    await showPreview();
    expect(screen.queryByRole('button', { name: 'Send me a test' })).not.toBeInTheDocument();

    await fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Enter a name.')).toBeInTheDocument();
    await fireEvent.input(screen.getByLabelText('Name'), { target: { value: 'Interview invitation' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(onsave).toHaveBeenCalledWith({ name: 'Interview invitation', subject: 'Hello', body: 'Hi there' }));
  });

  it('reads as labels and values without the role to change it (E3)', async () => {
    renderForm({ editable: false });

    expect(screen.queryByRole('textbox')).toBeNull();
    expect(screen.getByText('An update on {{ jobTitle }}')).toBeInTheDocument();
    await showPreview();
    expect(screen.queryByRole('button', { name: 'Send me a test' })).toBeNull();
  });
});
