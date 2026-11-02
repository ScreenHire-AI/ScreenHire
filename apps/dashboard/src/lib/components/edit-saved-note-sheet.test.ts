import type { HistoryEntry, TalentPoolMember } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { apiStub, user } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import EditSavedNoteSheet from './edit-saved-note-sheet.svelte';

vi.mock('$app/state', () => ({ page: { url: new URL('http://dashboard.test/saved'), data: { user } }, navigating: { to: null } }));

afterEach(() => {
  vi.unstubAllGlobals();
});

const person: TalentPoolMember = {
  candidateId: '01a157d4-0000-7000-8000-000000000401',
  candidateName: 'Casey Morgan',
  email: 'casey.morgan@example.com',
  applicationId: '01a157d4-0000-7000-8000-000000000501',
  jobId: '01a157d4-0000-7000-8000-000000000601',
  jobTitle: 'Senior .NET Engineer',
  note: 'Strong systems thinking, wrong timing.',
  noteUpdated: null,
  tags: [],
  keptAt: '2026-10-20T12:00:00Z',
  keptByUserId: null,
  rating: { state: 'NotRated' },
  lastContactedAt: null,
  lastInvitedJobId: null,
  lastInvitedJobTitle: null,
  lastInvitedAt: null,
  appliedJobIds: [],
};

const entry: HistoryEntry = {
  id: 'h1',
  at: '2026-10-25T13:22:00Z',
  by: 'Member',
  userId: user.id,
  apiKeyId: null,
  lines: [{ text: 'edited the note', parts: [{ text: 'edited the note', strong: false }], details: [] }],
};

function renderSheet(props: { person?: TalentPoolMember; ondone?: (message: string) => void } = {}, routes: Parameters<typeof apiStub>[0] = {}) {
  const put = vi.fn((init: RequestInit | undefined) => new Response(null, { status: 204 }));
  vi.stubGlobal(
    'fetch',
    vi.fn(
      apiStub({
        [`PUT /v1/talent-pool/${person.candidateId}/note`]: put,
        'GET /v1/organization/members': () => Response.json([]),
        [`GET /v1/talent-pool/${person.candidateId}/history`]: () => Response.json({ entries: [entry], nextBefore: null }),
        ...routes,
      }),
    ),
  );
  render(WithQueryClient, { props: { component: EditSavedNoteSheet, props: { open: true, person, ...props } } });
  return put;
}

const bodyOf = (put: ReturnType<typeof vi.fn>) => JSON.parse(String(put.mock.calls[0]![0].body));

describe('edit saved note sheet', () => {
  it('names the candidate, shows their current note and says where the first note and the changes are kept', async () => {
    renderSheet();
    const sheet = await screen.findByRole('dialog', { name: 'Edit note for Casey Morgan' });

    expect(sheet).toHaveTextContent('The first note stays in their Activity.');
    expect(within(sheet).getByLabelText('Note')).toHaveValue('Strong systems thinking, wrong timing.');
    expect(within(sheet).getByLabelText('Note')).toHaveAttribute('maxlength', '2000');
    await expectNoAxeViolations(document.body);
  });

  it('puts the trimmed note, announces it and closes', async () => {
    const ondone = vi.fn();
    const put = renderSheet({ ondone });
    const sheet = await screen.findByRole('dialog', { name: 'Edit note for Casey Morgan' });

    await fireEvent.input(within(sheet).getByLabelText('Note'), { target: { value: '  Now ready to talk.  ' } });
    await fireEvent.click(within(sheet).getByRole('button', { name: 'Save note' }));

    await waitFor(() => expect(put).toHaveBeenCalledTimes(1));
    expect(bodyOf(put)).toEqual({ note: 'Now ready to talk.' });
    await waitFor(() => expect(ondone).toHaveBeenCalledWith('Saved the note for Casey Morgan.'));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });

  it('sends a null note when the box is cleared', async () => {
    const put = renderSheet();
    const sheet = await screen.findByRole('dialog', { name: 'Edit note for Casey Morgan' });

    await fireEvent.input(within(sheet).getByLabelText('Note'), { target: { value: '   ' } });
    await fireEvent.click(within(sheet).getByRole('button', { name: 'Save note' }));

    await waitFor(() => expect(put).toHaveBeenCalledTimes(1));
    expect(bodyOf(put)).toEqual({ note: null });
  });

  it('says why when saving fails and keeps the sheet open', async () => {
    const ondone = vi.fn();
    renderSheet({ ondone }, { [`PUT /v1/talent-pool/${person.candidateId}/note`]: () => Response.json({}, { status: 500 }) });
    const sheet = await screen.findByRole('dialog', { name: 'Edit note for Casey Morgan' });

    await fireEvent.click(within(sheet).getByRole('button', { name: 'Save note' }));

    expect(await within(sheet).findByRole('alert')).toHaveTextContent('Could not save the note (HTTP 500).');
    expect(ondone).not.toHaveBeenCalled();
    expect(screen.getByRole('dialog', { name: 'Edit note for Casey Morgan' })).toBeInTheDocument();
  });

  it('shows who last changed the note, and History opens the changes', async () => {
    renderSheet({ person: { ...person, noteUpdated: { at: '2026-10-25T13:22:00Z', by: 'Member', userId: user.id } } });
    const sheet = await screen.findByRole('dialog', { name: 'Edit note for Casey Morgan' });

    await fireEvent.click(within(sheet).getByRole('button', { name: 'History' }));

    expect(await screen.findByText('edited the note')).toBeInTheDocument();
  });

  it('offers History even when nobody has changed the note since it was saved', async () => {
    renderSheet();
    const sheet = await screen.findByRole('dialog', { name: 'Edit note for Casey Morgan' });

    expect(within(sheet).getByRole('button', { name: 'History' })).toBeInTheDocument();
  });
});
