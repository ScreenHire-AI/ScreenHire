import { redirect } from '@sveltejs/kit';
import type { PageLoad } from './$types';

// The Integrations directory is gone: the Settings home lists every integration beside the other settings (design system S9).
export const load: PageLoad = () => {
  redirect(307, '/settings');
};
