import { redirect } from '@sveltejs/kit';
import type { PageLoad } from './$types';

// Email templates moved to the Templates page (design system section 22).
export const load: PageLoad = () => {
  redirect(307, '/templates/emails/new');
};
