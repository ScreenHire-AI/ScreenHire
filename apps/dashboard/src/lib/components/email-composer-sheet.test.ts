import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { apiStub } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import EmailComposerSheet from './email-composer-sheet.svelte';
import EmailComposer from './email-composer.svelte';
import { editRichText } from '../../test-support/rich-text.js';

const recipients = [
  { id: '0199f0c4-0000-7000-8000-00000000000a', candidateName: 'Casey Morgan' },
  { id: '0199f0c4-0000-7000-8000-00000000000b', candidateName: 'Riley Chen' },
];

const template = (id: string, name: string, subject: string, body: string, kind: { rejectOnly?: boolean; questionnaireOnly?: boolean } = {}) => ({
  id,
  name,
  builtIn: true,
  edited: false,
  rejectOnly: kind.rejectOnly ?? false,
  questionnaireOnly: kind.questionnaireOnly ?? false,
  subject,
  body,
  updatedAt: null,
  lastUpdated: { at: null, by: 'ScreenHire' },
});

const variable = (name: string, label: string, kind: { rejectOnly?: boolean; questionnaireOnly?: boolean } = {}) => ({
  name,
  label,
  rejectOnly: kind.rejectOnly ?? false,
  questionnaireOnly: kind.questionnaireOnly ?? false,
});

function renderSheet(sendStatus = 202) {
  const sends: unknown[] = [];
  const previews: unknown[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(
      apiStub({
        'GET /v1/email-templates': () =>
          Response.json({
            sendingEnabled: true,
            variables: [
              variable('candidateName', 'Full name'),
              variable('firstName', 'First name'),
              variable('jobTitle', 'Job title'),
              variable('reason', 'Reason', { rejectOnly: true }),
              variable('questionnaireLink', 'Questionnaire link', { questionnaireOnly: true }),
            ],
            templates: [
              template('application-received', 'Application received', 'Thanks for applying', 'Hi {{ firstName }},'),
              template('questionnaire-request', 'Follow-up questions', 'A few questions', '{{ questionnaireLink }}', { questionnaireOnly: true }),
              template('application-rejected', 'Not progressing', 'Your application', '{{ reason }}', { rejectOnly: true }),
            ],
          }),
        'POST /v1/emails/preview': (request) => {
          const draft = JSON.parse(String(request?.body)) as { subject: string; body: string };
          previews.push(draft);
          return Response.json({ subject: draft.subject.replace('{{ firstName }}', 'Casey'), html: '<p>Hi Casey,</p>', text: 'Hi Casey,' });
        },
        'POST /v1/emails': (request) => {
          sends.push(JSON.parse(String(request?.body)));
          return sendStatus === 202
            ? Response.json({ queued: 2, skipped: 0 }, { status: 202 })
            : Response.json({ title: 'Something went wrong' }, { status: sendStatus });
        },
      }),
    ),
  );
  const onsent = vi.fn();
  render(WithQueryClient, { props: { component: EmailComposerSheet, props: { open: true, recipients, onsent } } });
  return { sends, previews, onsent };
}

async function openSheet() {
  const sheet = await screen.findByRole('dialog', { name: 'Email 2 candidates' });
  const start = within(sheet).getByLabelText('Start from');
  await waitFor(() => expect(start).not.toBeDisabled());
  return { sheet, start };
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('email composer', () => {
  it('starts blank and offers every template but the rejection and follow-up questions emails', async () => {
    renderSheet();
    const { sheet, start } = await openSheet();

    expect(within(start).getAllByRole('option').map((option) => option.textContent)).toEqual(['Blank email', 'Application received']);
    expect(start).toHaveValue('');
    expect(within(sheet).getByLabelText('Subject')).toHaveValue('');
    // An empty email says what it needs when sent, and nothing goes.
    await fireEvent.click(within(sheet).getByRole('button', { name: 'Send 2 emails' }));
    expect(await within(sheet).findByText('Enter a subject.')).toBeInTheDocument();
    expect(within(sheet).getByText('Enter the message.')).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('fills the wording from a template and says a change is for this email only', async () => {
    renderSheet();
    const { sheet, start } = await openSheet();

    await fireEvent.change(start, { target: { value: 'application-received' } });
    const subject = within(sheet).getByLabelText('Subject');
    expect(subject).toHaveValue('Thanks for applying');
    const message = within(sheet).getByRole('textbox', { name: 'Message' });
    expect(message).toHaveTextContent('Hi variable First name,');
    expect(within(sheet).queryByText(/Changed for this email only/)).not.toBeInTheDocument();

    await fireEvent.input(subject, { target: { value: 'Thanks for applying, {{ firstName }}' } });
    expect(within(sheet).getByText('Changed for this email only. The Application received template stays as it is.')).toBeInTheDocument();
    await fireEvent.input(subject, { target: { value: 'Thanks for applying' } });
    expect(within(sheet).queryByText(/Changed for this email only/)).not.toBeInTheDocument();
    editRichText(message, '<p>Hi {{ firstName }}, <em>welcome</em></p>');
    expect(await within(sheet).findByText(/Changed for this email only/)).toBeInTheDocument();
  });

  it('inserts a variable as a chip in the message, offering none that only an action fills in', async () => {
    renderSheet();
    const { sheet } = await openSheet();
    const body = within(sheet).getByRole('textbox', { name: 'Message' });
    editRichText(body, '<p>Hi ,</p>');
    body.focus();
    await fireEvent.focus(body);

    await fireEvent.pointerDown(within(sheet).getByRole('button', { name: 'Insert variable' }), { button: 0, pointerType: 'mouse' });
    const menu = await screen.findByRole('menu');
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual(['Full name', 'First name', 'Job title']);
    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'First name' }));

    await waitFor(() => expect(body.querySelector('[data-liquid]')).toHaveTextContent('First name'));
  });

  it('previews the email for the first candidate and keeps the wording to send', async () => {
    const { previews } = renderSheet();
    const { sheet, start } = await openSheet();
    await fireEvent.change(start, { target: { value: 'application-received' } });

    await fireEvent.click(within(sheet).getByRole('button', { name: 'Preview' }));

    expect(within(sheet).getByRole('heading', { name: 'Preview for Casey Morgan' })).toBeInTheDocument();
    await waitFor(() => expect(sheet.querySelector('[data-email-subject]')).toHaveTextContent('Thanks for applying'));
    expect(previews).toEqual([{ applicationId: recipients[0]!.id, subject: 'Thanks for applying', body: 'Hi {{ firstName }},' }]);
    // The template's HTML goes as it was: only an edit makes the editor write it again.
  });

  it('sends a changed template with where it started, and a blank email with no template', async () => {
    const { sends, onsent } = renderSheet();
    const { sheet, start } = await openSheet();
    await fireEvent.change(start, { target: { value: 'application-received' } });
    editRichText(within(sheet).getByRole('textbox', { name: 'Message' }), '<p>Hi {{ firstName }}, one more thing.</p>');

    await fireEvent.click(within(sheet).getByRole('button', { name: 'Send 2 emails' }));

    await waitFor(() => expect(onsent).toHaveBeenCalledWith('Sending 2 emails.'));
    expect(sends).toEqual([
      {
        applicationIds: recipients.map((recipient) => recipient.id),
        templateId: 'application-received',
        subject: 'Thanks for applying',
        body: '<p>Hi {{ firstName }}, one more thing.</p>',
      },
    ]);
  });

  it('empties the inline box once the email is on its way, ready for the next one', async () => {
    const { sends } = renderSheet();
    const onsent = vi.fn();
    render(WithQueryClient, {
      props: { component: EmailComposer, props: { id: 'inline', label: 'Email Casey Morgan', recipients: [recipients[0]!], inline: true, onsent } },
    });
    const box = await screen.findByRole('form', { name: 'Email Casey Morgan' });
    await fireEvent.input(within(box).getByLabelText('Subject'), { target: { value: 'A quick question' } });
    editRichText(within(box).getByRole('textbox', { name: 'Message' }), '<p>Hi {{ firstName }},</p>');

    await fireEvent.click(within(box).getByRole('button', { name: 'Send' }));

    await waitFor(() => expect(onsent).toHaveBeenCalled());
    await waitFor(() => expect(within(box).getByLabelText('Subject')).toHaveValue(''));
    await waitFor(() => expect(within(box).getByRole('textbox', { name: 'Message' })).toHaveTextContent(''));
    expect(sends).toEqual([{ applicationIds: [recipients[0]!.id], templateId: null, subject: 'A quick question', body: '<p>Hi {{ firstName }},</p>' }]);
  });

  it('keeps the sheet open with the reason when the send fails', async () => {
    const { onsent } = renderSheet(500);
    const { sheet } = await openSheet();
    await fireEvent.input(within(sheet).getByLabelText('Subject'), { target: { value: 'A quick question' } });
    editRichText(within(sheet).getByRole('textbox', { name: 'Message' }), '<p>Hi {{ firstName }},</p>');

    await fireEvent.click(within(sheet).getByRole('button', { name: 'Send 2 emails' }));

    expect(await within(sheet).findByText(/Could not send the email/)).toBeInTheDocument();
    expect(onsent).not.toHaveBeenCalled();
  });
});
