import { redirect } from '@sveltejs/kit';
import type { PageLoad } from './$types';

// Integrity settings are Verification settings now (ADR-0017), so an old link lands on the new address.
export const load: PageLoad = () => {
  redirect(307, '/settings/verification');
};
