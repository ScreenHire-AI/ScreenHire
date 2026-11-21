import { redirect } from '@sveltejs/kit';
import type { PageLoad } from './$types';

// Add endpoint is a sheet over the Webhooks list (design system A16), so an old link opens the list with it open.
export const load: PageLoad = () => {
  redirect(307, '/settings/webhooks?add=endpoint');
};
