import { error } from '@sveltejs/kit';
import type { PageLoad } from './$types';

// An address no page owns. Matching it here, inside the signed-in app, lets the not-found page show under the sidebar and
// top bar instead of the bare brand bar, so the way back is one press and the person keeps their place (design system
// section 7, Error pages). A signed-out visitor never gets here: the root layout's guard sends them to sign-in first.
export const load: PageLoad = () => {
  error(404, 'Not Found');
};
