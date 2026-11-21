import type { CandidateEmailEntry } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { emailThreads, threadIds } from './email-history.js';

const email = (id: string, sentAt: string, overrides: Partial<CandidateEmailEntry> = {}): CandidateEmailEntry => ({
  id,
  applicationId: 'a1',
  jobTitle: 'Senior Product Designer',
  templateId: null,
  subject: id,
  actorUserId: 'u1',
  automationRunId: null,
  sentAt,
  kept: true,
  direction: 'Sent',
  ...overrides,
});
const reply = (id: string, sentAt: string, inReplyTo: string | null) =>
  email(id, sentAt, { direction: 'Received', actorUserId: null, fromAddress: 'casey@example.com', inReplyTo });

const shape = (emails: CandidateEmailEntry[]) => emailThreads(emails).map((t) => [t.email.id, t.replies.map((r) => r.id)]);

describe('emailThreads', () => {
  it('puts a reply under the email it answers, replies oldest first', () => {
    // As the API sends them: newest first.
    const emails = [
      reply('second answer', '2026-11-20T10:00:00Z', 'invite'),
      reply('first answer', '2026-11-19T10:00:00Z', 'invite'),
      email('invite', '2026-11-18T10:00:00Z'),
    ];

    expect(shape(emails)).toEqual([['invite', ['first answer', 'second answer']]]);
  });

  it('orders rows by their latest message, so a new reply brings an older email to the top', () => {
    const emails = [
      reply('answer', '2026-11-21T09:00:00Z', 'invite'),
      email('reminder', '2026-11-20T10:00:00Z'),
      email('invite', '2026-11-10T10:00:00Z'),
      email('welcome', '2026-11-01T10:00:00Z'),
    ];

    expect(shape(emails)).toEqual([
      ['invite', ['answer']],
      ['reminder', []],
      ['welcome', []],
    ]);
  });

  it('keeps a reply whose email is not listed, or that names none, as a row of its own', () => {
    const emails = [
      reply('to an unknown email', '2026-11-21T09:00:00Z', 'missing'),
      reply('to nothing', '2026-11-20T09:00:00Z', null),
      email('invite', '2026-11-10T10:00:00Z'),
    ];

    expect(shape(emails)).toEqual([
      ['to an unknown email', []],
      ['to nothing', []],
      ['invite', []],
    ]);
  });

  it('lists every email once for Expand all', () => {
    const threads = emailThreads([reply('answer', '2026-11-21T09:00:00Z', 'invite'), email('invite', '2026-11-10T10:00:00Z')]);

    expect(threadIds(threads)).toEqual(['invite', 'answer']);
  });
});
