import { redirect } from '@sveltejs/kit';
import type { PageLoad } from './$types';

// Saved candidates are the Saved choice on the Candidates page, so an old link lands there. The query string stays; a
// load cannot read the hash.
export const load: PageLoad = ({ url }) => {
  const target = new URL('/candidates', url);
  target.search = url.search;
  target.searchParams.set('list', 'saved');
  redirect(307, `${target.pathname}${target.search}`);
};
