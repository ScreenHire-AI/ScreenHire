// A candidate's data request on the careers server (docs/architecture/data-requests.md): ask for an emailed link, choose a
// copy or erasure from it, and follow the emailed download link. Native forms posted to Astro Actions, so every page works
// with JavaScript off. Server-only (it calls the API); the pure parts are exported for the unit tests.
import { getPublicDataDownload, makePublicDataRequest, requestPublicDataRequestLink } from './api';
import { text } from './apply';
import { forwardedFor, TURNSTILE_FIELD } from './apply-server';
import { isToken } from './questionnaire';

export { isToken };

/** The two things a candidate can ask for. The API's `DataRequestKind`, kept here so a posted value is checked before it is sent. */
export type DataRequestKind = 'Copy' | 'Erasure';

/** What the form page renders after the candidate asks for a link. `sent` redirects; the rest show a message in place. */
export type LinkOutcome = 'sent' | 'bad-email' | 'bot-check-failed' | 'busy' | 'unavailable' | 'error';

/** What the choices page renders after the candidate chooses. `received` redirects; `expired` shows the expired view. */
export type RequestOutcome = 'received' | 'bad-choice' | 'expired' | 'busy' | 'error';

export interface LinkResult {
  outcome: LinkOutcome;
  /** What the candidate typed, handed back with a problem so the page can fill the field again. Never part of an address. */
  email?: string;
}

export interface RequestResult {
  outcome: RequestOutcome;
}

/** The most an email address can be (RFC 5321). The API checks the rest. */
export const MAX_EMAIL = 254;

/** An address worth sending to the API: one `@` with something on each side. The API validates it properly. */
export const looksLikeEmail = (value: string): boolean => value.length <= MAX_EMAIL && /^[^\s@]+@[^\s@]+$/.test(value);

/** `Copy` or `Erasure` from a posted value, or null for anything else. */
export const parseKind = (value: string): DataRequestKind | null => (value === 'Copy' || value === 'Erasure' ? value : null);

/** What each outcome of asking for a link says. `sent` has no message: it moves on to the fixed answer. */
export const LINK_MESSAGES: Record<Exclude<LinkOutcome, 'sent'>, string> = {
  'bad-email': 'Enter the email address you used to apply, such as name@example.com.',
  'bot-check-failed': 'Our bot check did not finish, so no link was sent. Wait a moment and try again, or write to the employer instead.',
  busy: 'Too many attempts from your network. Wait a minute, then try again.',
  unavailable: 'This page is not available right now. Write to the employer instead.',
  error: 'Something went wrong on our side. Try again in a moment.',
};

/** What each failed outcome of choosing says. The expired outcome has a page of its own. */
export const REQUEST_MESSAGES: Record<Exclude<RequestOutcome, 'received' | 'expired'>, string> = {
  'bad-choice': 'Choose one of the two options.',
  busy: 'Too many attempts from your network. Wait a minute, then try again.',
  error: 'Something went wrong on our side. Try again in a moment.',
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

/** The outcome for the API's answer to a choice. A 404 is a link that is forged, expired or for an erased candidate. */
export function requestOutcome(status: number): RequestOutcome {
  switch (status) {
    case 200:
      return 'received';
    case 400:
      return 'bad-choice';
    case 404:
      return 'expired';
    case 429:
      return 'busy';
    default:
      return 'error';
  }
}

/** Where the download endpoint sends the browser: the API's read link, and only if it is a web address. */
export function downloadTarget(url: string | undefined): string | null {
  if (!url) return null;
  try {
    const parsed = new URL(url);
    return parsed.protocol === 'https:' || parsed.protocol === 'http:' ? parsed.toString() : null;
  } catch {
    return null;
  }
}

/** Asks the API to email a link to the address typed. The answer never says whether the address applied. Never throws for the candidate. */
export async function requestLink(form: FormData, clientAddress: string | undefined): Promise<LinkResult> {
  const organization = text(form, 'organization');
  const email = text(form, 'email');
  if (!looksLikeEmail(email)) return { outcome: 'bad-email', email: email.slice(0, MAX_EMAIL) };

  const response = await requestPublicDataRequestLink(
    organization,
    { email, botCheckToken: text(form, TURNSTILE_FIELD) || null },
    forwardedFor(clientAddress),
  );
  const outcome = linkOutcome(response.status);
  return outcome === 'sent' ? { outcome } : { outcome, email };
}

/** Records the candidate's choice from the emailed link. Never throws for the candidate. */
export async function makeRequest(form: FormData, clientAddress: string | undefined): Promise<RequestResult> {
  const organization = text(form, 'organization');
  const token = text(form, 'token');
  if (!isToken(token)) return { outcome: 'expired' };
  const kind = parseKind(text(form, 'kind'));
  if (!kind) return { outcome: 'bad-choice' };

  const response = await makePublicDataRequest(organization, token, { kind }, forwardedFor(clientAddress));
  return { outcome: requestOutcome(response.status) };
}

/** The read link for the copy the emailed link names, or null when the link is forged, expired or past its copy's lifetime. */
export async function downloadLink(organization: string, token: string | undefined, clientAddress: string | undefined): Promise<string | null> {
  if (!isToken(token)) return null;
  const response = await getPublicDataDownload(organization, token, forwardedFor(clientAddress));
  return response.status === 200 ? downloadTarget(response.data.url) : null;
}
