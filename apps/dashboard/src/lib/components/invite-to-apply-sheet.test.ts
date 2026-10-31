import type { TalentPoolMember } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiStub, jobs } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import InviteToApplySheet from './invite-to-apply-sheet.svelte';

afterEach(() => {
  vi.unstubAllGlobals();
});

const casey: TalentPoolMember = {
  candidateId: '01a157d4-0000-7000-8000-000000000301',
  candidateName: 'Casey Morgan',
  email: 'casey@example.com',
  applicationId: '01a157d4-0000-7000-8000-000000000302',
  jobId: jobs[0]!.id,
  jobTitle: jobs[0]!.title,
  note: null,
  tags: [],
  keptAt: '2026-10-20T12:00:00Z',
  keptByUserId: null,
  permissionEndsAt: '2027-10-20T12:00:00Z',
  lastInvitedJobId: null,
  lastInvitedJobTitle: null,
  lastInvitedAt: null,
  appliedJobIds: [jobs[0]!.id],
};

describe('invite to apply sheet', () => {
  // The preview's request changes the mutation's own state; if the effect read it, every answer would start the preview
  // again, and the sheet's buttons would keep moving under a click.
  it('fetches the preview once for the chosen job, then sends the invitation and closes', async () => {
    const preview = vi.fn(() => Response.json({ subject: 'Brightwater would like you to apply', html: '<p>Hi</p>', text: 'Hi' }));
    const invite = vi.fn(() => Response.json({ queued: 1, skipped: 0 }, { status: 202 }));
    vi.stubGlobal(
      'fetch',
      vi.fn(apiStub({ 'GET /v1/jobs': () => Response.json(jobs), 'POST /v1/talent-pool/invitations/preview': preview, 'POST /v1/talent-pool/invitations': invite })),
    );
    const ondone = vi.fn();
    render(WithQueryClient, { props: { component: InviteToApplySheet, props: { open: true, people: [casey], ondone } } });

    const sheet = await screen.findByRole('dialog', { name: 'Invite Casey Morgan to apply' });
    await waitFor(() => expect(sheet).toHaveTextContent('Brightwater would like you to apply'));
    await new Promise((resolve) => setTimeout(resolve, 100));
    expect(preview).toHaveBeenCalledTimes(1);

    await fireEvent.click(within(sheet).getByRole('button', { name: 'Send invitation' }));
    await waitFor(() => expect(invite).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(ondone).toHaveBeenCalledWith('Sending 1 invitation.'));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });
});
