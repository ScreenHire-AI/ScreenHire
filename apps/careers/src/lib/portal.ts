// A candidate's own page on the careers server (docs/architecture/candidate-portal.md): ask for an emailed link, open the
// page with it, read the messages about an application and answer them. Native forms posted to Astro Actions, so every page
// works with JavaScript off. Nothing about the browser goes with any of it: no telemetry and no device check. Server-only
// (it calls the API); the pure parts are exported for the unit tests.
import { requestPublicPortalLink, sendPublicPortalReply } from './api';
import { text } from './apply';
import { forwardedFor, TURNSTILE_FIELD } from './apply-server';
import { isToken } from './questionnaire';

export { isToken };

/** What the form page renders after the candidate asks for a link. `sent` redirects; the rest show a message in place. */
export type LinkOutcome = 'sent' | 'bad-email' | 'bot-check-failed' | 'busy' | 'unavailable' | 'error';

/** What the thread page says after a reply. `sent` redirects to the thread; `expired` shows the expired view. */
export type ReplyOutcome = 'sent' | 'empty' | 'too-long' | 'too-many-files' | 'files-too-large' | 'nothing-to-reply-to' | 'too-many-today' | 'expired' | 'error';

export interface LinkResult {
  outcome: LinkOutcome;
  /** What the candidate typed, handed back with a problem so the page can fill the field again. Never part of an address. */
  email?: string;
}

export interface ReplyResult {
  outcome: ReplyOutcome;
  /** What the candidate typed and the form's own id, handed back with a problem so nothing is lost and a retry sends once. */
  body?: string;
  key?: string;
}

/** The most an email address can be (RFC 5321). The API checks the rest. */
export const MAX_EMAIL = 254;

/** The most files one reply may carry, as the API says. */
export const MAX_FILES = 3;

/**
 * The most bytes the files of one reply may total. The careers server takes at most 12 MB from a form (`actionBodySizeLimit`
 * in astro.config.mjs), so 10 MB of files leaves room for the words.
 */
export const MAX_FILE_BYTES = 10 * 1024 * 1024;

/** What the file field offers: the kinds the API keeps (PDF, Word, PNG and JPEG). The API checks what the files really are. */
export const FILE_TYPES = '.pdf,.doc,.docx,.png,.jpg,.jpeg';

/** An address worth sending to the API: one `@` with something on each side. The API validates it properly. */
export const looksLikeEmail = (value: string): boolean => value.length <= MAX_EMAIL && /^[^\s@]+@[^\s@]+$/.test(value);

/** An application id from the address, checked before it goes into an API path. */
export const isGuid = (value: string | undefined | null): value is string =>
  !!value && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);

/** The form's own id for one reply: 32 hex digits, so sending the same form twice stores one reply. */
export const newReplyKey = (): string => crypto.randomUUID().replaceAll('-', '');

const isKey = (value: string): boolean => /^[0-9a-f]{32}$/.test(value);

/** What each outcome of asking for a link says. `sent` has no message: it moves on to the fixed answer. */
export const LINK_MESSAGES: Record<Exclude<LinkOutcome, 'sent'>, string> = {
  'bad-email': 'Enter the email address you used to apply, such as name@example.com.',
  'bot-check-failed': 'Our bot check did not finish, so no link was sent. Wait a moment and try again, or write to the employer instead.',
  busy: 'Too many attempts from your network. Wait a minute, then try again.',
  unavailable: 'This page is not available right now. Write to the employer instead.',
  error: 'Something went wrong on our side. Try again in a moment.',
};

/** What each failed outcome of replying says. The expired outcome has a page of its own. */
export const REPLY_MESSAGES: Record<Exclude<ReplyOutcome, 'sent' | 'expired'>, string> = {
  empty: 'Write your message first.',
  'too-long': 'Your message is too long. Shorten it, or send the rest in a second message.',
  'too-many-files': `Attach up to ${MAX_FILES} files.`,
  'files-too-large': 'Your files add up to more than 10 MB. Attach fewer or smaller files.',
  'nothing-to-reply-to': 'The team has not written to you about this application yet, so there is nothing to reply to.',
  'too-many-today': 'You have sent several messages today. Wait a while, or reply to the email you were sent.',
  error: 'Something went wrong on our side. Your message was not sent. Try again in a moment.',
};

/** The outcome for the API's answer to asking for a link. */
export function linkOutcome(status: number): LinkOutcome {
  switch (status) {
    case 202:
      return 'sent';
    case 400:
      return 'bad-email';
    case 403:
      return 'bot-check-failed';
    case 404:
      return 'unavailable';
    case 429:
      return 'busy';
    default:
      return 'error';
  }
}

/** The outcome for the API's answer to a reply. A 404 is a link that is forged, expired or for an erased candidate. */
export function replyOutcome(status: number): ReplyOutcome {
  switch (status) {
    case 202:
      return 'sent';
    case 400:
      return 'error';
    case 404:
      return 'expired';
    case 409:
      return 'nothing-to-reply-to';
    case 429:
      return 'too-many-today';
    default:
      return 'error';
  }
}

/** The files a form posted, without the empty part a browser sends when none was chosen. */
export function filesOf(form: FormData, name = 'files'): File[] {
  return form.getAll(name).filter((entry): entry is File => entry instanceof File && entry.size > 0 && entry.name !== '');
}

/** Checks a reply before it is sent: words and files. Null when it can go. */
export function replyProblem(body: string, files: readonly File[], bodyMax: number): Exclude<ReplyOutcome, 'sent' | 'expired' | 'error'> | null {
  if (body.trim().length === 0) return 'empty';
  if (body.length > bodyMax) return 'too-long';
  if (files.length > MAX_FILES) return 'too-many-files';
  if (files.reduce((total, file) => total + file.size, 0) > MAX_FILE_BYTES) return 'files-too-large';
  return null;
}

/** Asks the API to email a link to the address typed. The answer never says whether the address applied. Never throws for the candidate. */
export async function requestLink(form: FormData, clientAddress: string | undefined): Promise<LinkResult> {
  const organization = text(form, 'organization');
  const email = text(form, 'email');
  if (!looksLikeEmail(email)) return { outcome: 'bad-email', email: email.slice(0, MAX_EMAIL) };

  const response = await requestPublicPortalLink(
    organization,
    { email, botCheckToken: text(form, TURNSTILE_FIELD) || null },
    forwardedFor(clientAddress),
  );
  const outcome = linkOutcome(response.status);
  return outcome === 'sent' ? { outcome } : { outcome, email };
}

/** The longest message the API takes, until the page learns the real limit from it. */
export const DEFAULT_BODY_MAX = 5000;

/** Sends the candidate's reply from the thread page, with the files they attached. Never throws for the candidate. */
export async function sendReply(form: FormData, clientAddress: string | undefined): Promise<ReplyResult> {
  const organization = text(form, 'organization');
  const token = text(form, 'token');
  const application = text(form, 'application');
  const rawKey = text(form, 'key');
  const key = isKey(rawKey) ? rawKey : newReplyKey();
  const bodyMax = Number(text(form, 'bodyMax')) || DEFAULT_BODY_MAX;
  if (!isToken(token) || !isGuid(application)) return { outcome: 'expired' };

  const body = (form.get('body') ?? '').toString();
  const files = filesOf(form);
  const problem = replyProblem(body, files, bodyMax);
  if (problem) return { outcome: problem, body, key };

  try {
    const attachments = await Promise.all(
      files.map(async (file) => ({ fileName: file.name, contentBase64: Buffer.from(await file.arrayBuffer()).toString('base64') })),
    );
    const response = await sendPublicPortalReply(organization, token, application, { body, key, attachments }, forwardedFor(clientAddress));
    const outcome = replyOutcome(response.status);
    return outcome === 'sent' ? { outcome } : { outcome, body, key };
  } catch {
    return { outcome: 'error', body, key };
  }
}

/** One of the four steps as the funnel draws it: done, the one the application is at, or still to come. */
export type StepState = 'done' | 'current' | 'upcoming';

/** The state of each step for an application at `current` (null once it has ended, when no step is drawn). */
export function stepStates(steps: readonly string[], current: number | null | undefined): { name: string; state: StepState }[] {
  return steps.map((name, index) => ({
    name,
    state: current == null ? 'upcoming' : index < current ? 'done' : index === current ? 'current' : 'upcoming',
  }));
}

/**
 * How an ended application reads, with the status mark design-system section 3.2 gives it: a tick for Hired only, and the grey
 * minus for the two ways of leaving a job. A saved candidate reads as Not moving forward and nothing says why.
 */
export const ENDED: Record<'Hired' | 'NotMovingForward' | 'Withdrawn', { label: string; detail: string; mark: 'check' | 'minus'; tone: string }> = {
  Hired: { label: 'Hired', detail: 'The team has marked you as hired for this role.', mark: 'check', tone: 'text-status-clear-mark' },
  NotMovingForward: {
    label: 'Not moving forward',
    detail: 'The team has decided not to move forward with this application.',
    mark: 'minus',
    tone: 'text-candidate-muted-foreground',
  },
  Withdrawn: { label: 'Withdrawn', detail: 'This application was withdrawn.', mark: 'minus', tone: 'text-candidate-muted-foreground' },
};
