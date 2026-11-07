import { redirect } from '@sveltejs/kit';
import type { PageLoad } from './$types';

// The talent pool is Saved candidates now, the Saved choice on the Candidates page, so an old link lands there. A load
// cannot read the hash.
export const load: PageLoad = ({ url }) => {
  const target = new URL('/candidates', url);
  target.search = url.search;
  target.searchParams.set('list', 'saved');
  redirect(307, `${target.pathname}${target.search}`);
};
