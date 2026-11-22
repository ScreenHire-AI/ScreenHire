import { redirect } from '@sveltejs/kit';
import { movedTabs, verificationTab, type MovedTab, type VerificationTabValue } from '#lib/verification-area.js';
import type { CurrentUser } from '@screenhire/api-client';

/**
 * A tab someone's role cannot use is left out of the strip (A3), so its address sends them to the Overview instead of
 * showing a refusal. The API refuses the data as well (ADR-0014); this is the screen following it.
 */
export function guardTab(value: VerificationTabValue, user: CurrentUser | null | undefined) {
  if (!verificationTab(value).allowed(user)) redirect(307, '/verification');
}

/**
 * The address of a tab that moved sends people to the page that holds what it held now, for their role, with the query
 * string. A target's hash goes after the query string. A load cannot read the incoming hash, so only the target's is kept.
 */
export function moveTab(from: MovedTab, url: URL, user: CurrentUser | null | undefined): never {
  const target = movedTabs[from](user);
  const [path, hash] = target.split('#');
  redirect(307, `${path}${url.search}${hash ? `#${hash}` : ''}`);
}
