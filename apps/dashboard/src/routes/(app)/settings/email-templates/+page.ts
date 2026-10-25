import { redirect } from '@sveltejs/kit';
import type { PageLoad } from './$types';

// Email templates moved to the Templates page (design system section 22). The load runs before the settings layout's
// permission check, so every old link lands on the Emails tab, whatever the person's role.
export const load: PageLoad = () => {
  redirect(307, '/templates?tab=emails');
};
