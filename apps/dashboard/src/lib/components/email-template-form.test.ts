import type { EmailTemplateEntry, EmailTemplateVariableEntry } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { apiStub } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import EmailTemplateForm from './email-template-form.svelte';

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
  render(WithQueryClient, {
    props: {
      component: EmailTemplateForm,
      props: { template, variables, sendingEnabled: true, submitLabel: 'Save', cancelHref: '/settings/email-templates', failure: 'Could not save', onsave, ...props },
    },
  });
  return { previews, tests, onsave };
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('email template form', () => {
  it('previews the wording with the sample candidate as it is typed, and switches to plain text', async () => {
    const { previews } = renderForm();
    const preview = screen.getByRole('region', { name: 'Preview' });
    expect(await within(preview).findByText('An update on Product designer')).toBeInTheDocument();
    expect(previews[0]).toEqual({ subject: 'An update on {{ jobTitle }}', body: 'Hi {{ candidateName }},', templateId: 'stage-change' });

    await fireEvent.input(screen.getByLabelText('Subject'), { target: { value: 'News about {{ jobTitle }}' } });
    expect(await within(preview).findByText('News about Product designer')).toBeInTheDocument();

    await fireEvent.click(within(preview).getByRole('button', { name: 'Plain text' }));
    expect(within(preview).getByRole('button', { name: 'Plain text' })).toHaveAttribute('aria-pressed', 'true');
    expect(preview.querySelector('[data-email-text]')).toHaveTextContent('Hi Jordan Lee,');
    await expectNoAxeViolations();
  });

  it('says what is wrong with wording the API refuses', async () => {
    renderForm();
    await fireEvent.input(screen.getByLabelText('Subject'), { target: { value: 'Hi {{ nope }}' } });

    expect(await screen.findByText('There is no variable "nope".')).toBeInTheDocument();
  });

  it('inserts a variable at the caret of the field last focused, and offers the reason only to the rejection email', async () => {
    renderForm();
    const subject = screen.getByLabelText<HTMLInputElement>('Subject');
    await fireEvent.focus(subject);
    subject.setSelectionRange(0, 0);

    await fireEvent.click(screen.getByRole('button', { name: 'Insert Candidate name' }));

    await waitFor(() => expect(subject).toHaveValue('{{ candidateName }}An update on {{ jobTitle }}'));
    expect(screen.queryByRole('button', { name: 'Reason' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Name')).not.toBeInTheDocument();
  });

  it('sends a test of the unsaved wording to the signed-in person', async () => {
    const { tests } = renderForm();

    await fireEvent.click(screen.getByRole('button', { name: 'Send test' }));

    expect(await screen.findByText('Sent a test to avery@example.com.')).toBeInTheDocument();
    expect(tests).toEqual([{ subject: 'An update on {{ jobTitle }}', body: 'Hi {{ candidateName }},', templateId: 'stage-change' }]);
  });

  it('says where the preview will be while a new template has no wording, and turns the test send on with the first words', async () => {
    const { previews } = renderForm({ template: undefined });
    const preview = screen.getByRole('region', { name: 'Preview' });

    expect(within(preview).getByText('The email appears here as you write the subject and the message.')).toBeInTheDocument();
    expect(within(preview).getByRole('button', { name: 'Send test' })).toBeDisabled();
    expect(previews).toHaveLength(0);

    await fireEvent.input(screen.getByLabelText('Subject'), { target: { value: 'An update on {{ jobTitle }}' } });
    expect(await within(preview).findByText('An update on Product designer')).toBeInTheDocument();
    expect(within(preview).getByRole('button', { name: 'Send test' })).toBeEnabled();
  });

  it('hides the test send when this deployment cannot send email, and asks a new template for its name', async () => {
    const { onsave } = renderForm({ template: undefined, sendingEnabled: false });

    expect(screen.queryByRole('button', { name: 'Send test' })).not.toBeInTheDocument();
    await fireEvent.input(screen.getByLabelText('Subject'), { target: { value: 'Hello' } });
    await fireEvent.input(screen.getByLabelText('Message'), { target: { value: 'Hi there' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Enter a name.')).toBeInTheDocument();
    await fireEvent.input(screen.getByLabelText('Name'), { target: { value: 'Interview invitation' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(onsave).toHaveBeenCalledWith({ name: 'Interview invitation', subject: 'Hello', body: 'Hi there' }));
  });
});
