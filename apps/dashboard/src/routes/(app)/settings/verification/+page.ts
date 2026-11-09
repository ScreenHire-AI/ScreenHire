import { redirect } from '@sveltejs/kit';
import type { PageLoad } from './$types';

// Verification is a sidebar item of its own, not a Settings page, so the old address lands on the new one. Alert emails and
// Slack messages from before the move still link here.
export const load: PageLoad = () => {
  redirect(307, '/verification');
};
