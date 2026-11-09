import { redirect } from '@sveltejs/kit';
import { verificationTab, type VerificationTabValue } from '#lib/verification-area.js';
import type { CurrentUser } from '@screenhire/api-client';

/**
 * A tab someone's role cannot use is left out of the strip (A3), so its address sends them to the Overview instead of
 * showing a refusal. The API refuses the data as well (ADR-0014); this is the screen following it.
 */
export function guardTab(value: VerificationTabValue, user: CurrentUser | null | undefined) {
  if (!verificationTab(value).allowed(user)) redirect(307, '/verification');
}
