import type { CandidateEmailEntry, TeamMember } from '@screenhire/api-client';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { apiStub } from '../../../test-support/fixtures.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import MessagesTab from './messages-tab.svelte';

const members = [{ userId: 'u1', displayName: 'Avery Quinn', roleName: 'Owner' }] as TeamMember[];

const invite: CandidateEmailEntry = {
  id: 'invite',
  applicationId: 'a1',
  jobTitle: 'Senior Product Designer',
  templateId: null,
  subject: 'Your interview with Brightwater Studio',
  actorUserId: 'u1',
  automationRunId: null,
  sentAt: '2026-11-18T10:00:00Z',
  kept: true,
  direction: 'Sent',
  snippet: 'Hi Casey, thank you for your time on Thursday.',
};
const answer: CandidateEmailEntry = {
  ...invite,
  id: 'answer',
  subject: 'Re: Your interview with Brightwater Studio',
  actorUserId: null,
  sentAt: '2026-11-19T10:00:00Z',
  direction: 'Received',
  fromAddress: 'casey@example.com',
  snippet: 'Thursday works for me.',
  inReplyTo: 'invite',
};
const welcome: CandidateEmailEntry = {
  ...invite,
  id: 'welcome',
  subject: 'We received your application',
  automationRunId: 'run1',
  actorUserId: null,
  sentAt: '2026-11-01T10:00:00Z',
  snippet: 'Hi Casey, thank you for applying.',
};

beforeEach(() => {
  vi.stubGlobal(
    'fetch',
    apiStub({
      'GET /v1/emails/invite': () =>
        Response.json({
          id: 'invite',
          applicationId: 'a1',
          templateId: null,
          to: 'casey@example.com',
          subject: invite.subject,
          html: '<p>Hi Casey, thank you for your time on Thursday.</p>',
          text: 'Hi Casey, thank you for your time on Thursday.',
          sentAt: invite.sentAt,
        }),
      'GET /v1/emails/welcome': () =>
        Response.json({ ...welcome, to: 'casey@example.com', html: '<p>Hi Casey.</p>', text: 'Hi Casey.' }),
      'GET /v1/replies/answer': () =>
        Response.json({
          id: 'answer',
          applicationId: 'a1',
          fromAddress: 'casey@example.com',
          fromName: 'Casey Morgan',
          subject: answer.subject,
          html: '',
          text: 'Thursday works for me.',
          receivedAt: answer.sentAt,
          autoReply: false,
          attachments: [],
        }),
    }),
  );
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

function show(emails: CandidateEmailEntry[] = [answer, invite, welcome]) {
  render(WithQueryClient, { props: { component: MessagesTab, props: { emails, onretry: () => {}, members, currentUserId: 'u1' } } });
  return screen.getByRole('list', { name: 'Emails' });
}

const rowFor = (subject: string) => screen.getByRole('button', { name: new RegExp(`^${subject}`) });

describe('the Messages history', () => {
  it('shows each email as one row: subject, who, the first line and when, with a reply under the email it answers', () => {
    const list = show();

    const rows = within(list).getAllByRole('button');
    expect(rows.map((row) => row.querySelector('.font-semibold')?.textContent)).toEqual([invite.subject, answer.subject, welcome.subject]);
    expect(rowFor('Your interview')).toHaveTextContent('Sent by you');
    expect(rowFor('Your interview')).toHaveTextContent('Hi Casey, thank you for your time on Thursday.');
    expect(rowFor('We received')).toHaveTextContent('Sent by an automation');
    expect(within(rowFor('Re: Your interview')).getByText('From casey@example.com')).toBeInTheDocument();
    expect(rowFor('Re: Your interview').querySelector('time')).not.toBeNull();

    const replies = within(list).getByRole('list', { name: 'Replies to Your interview with Brightwater Studio' });
    expect(within(replies).getByRole('button')).toHaveTextContent('Thursday works for me.');
  });

  it('opens and closes every row with Expand all and Collapse all', async () => {
    const list = show();
    // Each shows only while it would change something (A3).
    expect(screen.queryByRole('button', { name: 'Collapse all' })).not.toBeInTheDocument();

    await fireEvent.click(screen.getByRole('button', { name: 'Expand all' }));
    expect(within(list).getAllByRole('button', { expanded: true })).toHaveLength(3);
    expect(within(list).queryAllByRole('button', { expanded: false })).toHaveLength(0);
    expect(screen.queryByRole('button', { name: 'Expand all' })).not.toBeInTheDocument();
    await screen.findByText('Casey Morgan <casey@example.com>');

    await fireEvent.click(screen.getByRole('button', { name: 'Collapse all' }));
    expect(within(list).queryAllByRole('button', { expanded: true })).toHaveLength(0);
    expect(screen.getByRole('button', { name: 'Expand all' })).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByText('Casey Morgan <casey@example.com>')).not.toBeInTheDocument());
  });

  it('puts Email and Plain text on the opened email\'s own line, and switches to the text part', async () => {
    show();

    await fireEvent.click(rowFor('Your interview'));
    const to = await screen.findByText('casey@example.com', { selector: '.font-semibold' });
    const toggle = screen.getByRole('group', { name: 'Part' });
    // One line: who it went to and the toggle share their row, with nothing else in it.
    expect(to.closest('p')!.parentElement).toBe(toggle.parentElement);
    expect(toggle.parentElement!.children).toHaveLength(2);

    await fireEvent.click(within(toggle).getByRole('button', { name: 'Plain text' }));
    await waitFor(() => expect(document.querySelector('[data-email-text]')).toHaveTextContent('Hi Casey, thank you for your time on Thursday.'));
    await expectNoAxeViolations();
  });

  it('offers Expand all only when there is more than one email', () => {
    show([welcome]);

    expect(screen.queryByRole('button', { name: 'Expand all' })).not.toBeInTheDocument();
  });
});
