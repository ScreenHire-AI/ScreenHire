import { redirect } from '@sveltejs/kit';
import type { PageLoad } from './$types';

// Integrity settings were renamed Verification (ADR-0017) and then moved out of Settings, so an old link lands on the new address.
export const load: PageLoad = () => {
  redirect(307, '/verification');
};
