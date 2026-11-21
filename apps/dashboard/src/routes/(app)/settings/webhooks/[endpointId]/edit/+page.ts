import { redirect } from '@sveltejs/kit';
import type { PageLoad } from './$types';

// Editing an endpoint is a sheet over its page (design system A16), so an old link opens the page with it open.
export const load: PageLoad = ({ params }) => {
  redirect(307, `/settings/webhooks/${encodeURIComponent(params.endpointId)}?edit=endpoint`);
};
