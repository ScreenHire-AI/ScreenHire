import { redirect } from '@sveltejs/kit';
import type { PageLoad } from './$types';

// The Scorecards tab is a tab of the Templates page; this address leads there.
export const load: PageLoad = () => {
  redirect(307, '/templates?tab=scorecards');
};
