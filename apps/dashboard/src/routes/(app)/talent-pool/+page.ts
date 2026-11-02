import { redirect } from '@sveltejs/kit';
import type { PageLoad } from './$types';

// The talent pool is Saved candidates now, so an old link lands on the new address. A load cannot read the hash.
export const load: PageLoad = ({ url }) => {
  redirect(307, `/saved${url.search}`);
};
