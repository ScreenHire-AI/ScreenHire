import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { apiStub } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import EmailSheet from './email-sheet.svelte';

const recipients = [
  { id: '0199f0c4-0000-7000-8000-00000000000a', candidateName: 'Casey Morgan' },
  { id: '0199f0c4-0000-7000-8000-00000000000b', candidateName: 'Riley Chen' },
];

const template = (id: string, name: string, rejectOnly = false) => ({
  id,
  name,
  builtIn: true,
  edited: false,
  rejectOnly,
  subject: '',
  body: '',
  updatedAt: null,
});

function renderSheet(sendStatus = 202) {
  const sends: unknown[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (url.startsWith('/v1/emails/preview?')) {
        const templateId = new URL(url, 'http://localhost').searchParams.get('templateId');
        return Response.json({ subject: `Subject of ${templateId}`, html: '<p>Hi Casey</p>', text: 'Hi Casey' });
      }
      return apiStub({
        'GET /v1/email-templates': () =>
          Response.json({
            sendingEnabled: true,
            variables: [],
            templates: [
              template('application-received', 'Application received'),
              template('stage-change', 'Moved to the next step'),
              template('application-rejected', 'Not progressing', true),
            ],
          }),
        'POST /v1/emails': (request) => {
          sends.push(JSON.parse(String(request?.body)));
          return sendStatus === 202
            ? Response.json({ queued: 2, skipped: 0 }, { status: 202 })
            : Response.json({ title: 'Something went wrong' }, { status: sendStatus });
        },
      })(input, init);
    }),
  );
  const onsent = vi.fn();
  render(WithQueryClient, { props: { component: EmailSheet, props: { open: true, recipients, onsent } } });
  return { sends, onsent };
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('email sheet', () => {
  it('offers every template but the rejection email and previews the choice for the first candidate', async () => {
    renderSheet();
    const sheet = await screen.findByRole('dialog', { name: 'Email 2 candidates' });

    const select = within(sheet).getByLabelText('Template');
    await waitFor(() => expect(within(select).getAllByRole('option').map((option) => option.textContent)).toEqual(['Application received', 'Moved to the next step']));
    expect(within(sheet).getByRole('heading', { name: 'Preview for Casey Morgan' })).toBeInTheDocument();
    expect(await within(sheet).findByText('Subject of application-received')).toBeInTheDocument();

    await fireEvent.change(select, { target: { value: 'stage-change' } });
    expect(await within(sheet).findByText('Subject of stage-change')).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('sends the chosen template to every recipient and says how many are on their way', async () => {
    const { sends, onsent } = renderSheet();
    const sheet = await screen.findByRole('dialog', { name: 'Email 2 candidates' });
    await within(sheet).findByText('Subject of application-received');

    await fireEvent.click(within(sheet).getByRole('button', { name: 'Send 2 emails' }));

    await waitFor(() => expect(onsent).toHaveBeenCalledWith('Sending 2 emails.'));
    expect(sends).toEqual([{ applicationIds: recipients.map((recipient) => recipient.id), templateId: 'application-received' }]);
  });

  it('keeps the sheet open with the reason when the send fails', async () => {
    const { onsent } = renderSheet(500);
    const sheet = await screen.findByRole('dialog', { name: 'Email 2 candidates' });
    await within(sheet).findByText('Subject of application-received');

    await fireEvent.click(within(sheet).getByRole('button', { name: 'Send 2 emails' }));

    expect(await within(sheet).findByText(/Could not send the email/)).toBeInTheDocument();
    expect(onsent).not.toHaveBeenCalled();
  });
});
