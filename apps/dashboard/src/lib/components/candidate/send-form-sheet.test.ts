// Send a form on the candidate's page: the job's forms, each with Send or Send again, none once answered, and one
// request per press that carries its own Idempotency-Key.
import type { ApplicationFollowUp, FollowUpQuestionnaire } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiStub } from '../../../test-support/fixtures.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import SendFormSheet from './send-form-sheet.svelte';

const applicationId = '01a157d4-0000-7000-8000-000000000501';
const form = (id: string, name: string): FollowUpQuestionnaire => ({ id, name, questions: [{ id: `q_${id}`, type: 'ShortText', label: 'A?', required: false }] });
const sent: ApplicationFollowUp = {
  id: '01a157d4-0000-7000-8000-000000000502',
  questionnaireId: 'references',
  name: 'References',
  firstSentAt: '2026-11-19T09:00:00Z',
  lastSentAt: '2026-11-20T09:00:00Z',
  expiresAt: '2026-12-04T09:00:00Z',
  timesSent: 1,
  completedAt: null,
  questions: [],
  answers: [],
};

afterEach(() => vi.unstubAllGlobals());

describe('send form sheet', () => {
  it('sends one of the job forms with its own key, offers Send again while one waits, and nothing once answered', async () => {
    const fetchMock = vi.fn(
      apiStub({
        [`POST /v1/applications/${applicationId}/forms/screening/send`]: () => Response.json({ ...sent, questionnaireId: 'screening' }, { status: 202 }),
      }),
    );
    vi.stubGlobal('fetch', fetchMock);
    const onsent = vi.fn();
    render(WithQueryClient, {
      props: {
        component: SendFormSheet,
        props: {
          open: true,
          applicationId,
          forms: [form('screening', 'Screening questionnaire'), form('references', 'References'), form('availability', 'Availability')],
          sent: [sent, { ...sent, id: '01a157d4-0000-7000-8000-000000000503', questionnaireId: 'availability', name: 'Availability', completedAt: '2026-11-21T09:00:00Z' }],
          onsent,
        },
      },
    });

    const sheet = await screen.findByRole('dialog', { name: 'Send a form' });
    expect(within(sheet).getByRole('button', { name: 'Send again References' })).toBeInTheDocument();
    expect(within(sheet).queryByRole('button', { name: /Availability/ })).toBeNull();
    expect(sheet).toHaveTextContent('Answered');

    await fireEvent.click(within(sheet).getByRole('button', { name: 'Send Screening questionnaire' }));
    await waitFor(() => expect(onsent).toHaveBeenCalledWith('Screening questionnaire'));
    const [url, init] = fetchMock.mock.calls[0]!;
    expect(url).toBe(`/v1/applications/${applicationId}/forms/screening/send`);
    expect(new Headers(init?.headers).get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
  });
});
