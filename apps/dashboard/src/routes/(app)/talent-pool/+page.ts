import { redirect } from '@sveltejs/kit';
import type { PageLoad } from './$types';

// The talent pool is Saved candidates now, so an old link lands on the new address.
export const load: PageLoad = () => {
  redirect(307, '/saved');
};
