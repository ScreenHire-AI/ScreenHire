import type { CandidateEmailEntry } from '@screenhire/api-client';

/** One row of Messages: an email, and the candidate's replies to it under it. */
export interface EmailThread {
  email: CandidateEmailEntry;
  /** Oldest first, so they read as the conversation went. */
  replies: CandidateEmailEntry[];
}

/**
 * Messages' history (design system section 21, Candidate): a reply goes under the email it answers when that email is in
 * the list, and stands as its own row when it is not. Rows are newest first by their latest message, so a reply that
 * came today brings its email to the top rather than sitting under an email from last week.
 */
export function emailThreads(emails: readonly CandidateEmailEntry[]): EmailThread[] {
  const threads = new Map<string, EmailThread>();
  for (const email of emails) {
    if (email.direction !== 'Received') threads.set(email.id, { email, replies: [] });
  }

  const rows: EmailThread[] = [];
  for (const email of emails) {
    const parent = email.direction === 'Received' && email.inReplyTo ? threads.get(email.inReplyTo) : undefined;
    if (parent) parent.replies.push(email);
    else rows.push(threads.get(email.id) ?? { email, replies: [] });
  }

  for (const thread of rows) thread.replies.sort((a, b) => at(a) - at(b));
  // A stable sort: rows whose latest message came at the same moment keep the API's order.
  return rows
    .map((thread) => ({ thread, latest: Math.max(at(thread.email), ...thread.replies.map(at)) }))
    .sort((a, b) => b.latest - a.latest)
    .map(({ thread }) => thread);
}

/** Every email in the threads, for Expand all. */
export function threadIds(threads: readonly EmailThread[]): string[] {
  return threads.flatMap((thread) => [thread.email.id, ...thread.replies.map((reply) => reply.id)]);
}

const at = (email: CandidateEmailEntry) => Date.parse(email.sentAt);
