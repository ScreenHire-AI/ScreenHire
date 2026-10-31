// The page a candidate opens from the link in a talent pool invitation, to stop an employer keeping their details for
// future roles (docs/architecture/talent-pool.md). One native form posted to the `talentPool` action, so it works with
// JavaScript off and ships no script. Server-only: it calls the API.
import { stopKeepingDetails } from './api';
import { forwardedFor } from './apply-server';
import { text } from './apply';
import { isToken } from './questionnaire';

/** What the page renders after a post. */
export interface TalentPoolResult {
  outcome: 'stopped' | 'not-found' | 'error';
}

/** Stops the employer keeping the candidate's details. Never throws for the candidate. */
export async function stopKeeping(form: FormData, clientAddress: string | undefined): Promise<TalentPoolResult> {
  const organization = text(form, 'organization');
  const token = text(form, 'token');
  if (!isToken(token)) return { outcome: 'not-found' };

  const response = await stopKeepingDetails(organization, token, forwardedFor(clientAddress));
  switch (response.status) {
    case 204:
      return { outcome: 'stopped' };
    case 404:
      return { outcome: 'not-found' };
    default:
      return { outcome: 'error' };
  }
}
