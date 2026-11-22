import { moveTab } from '../tab-guard.js';
import type { PageLoad } from './$types';

// This tab moved (movedTabs in #lib/verification-area.ts); old links land on the page that holds what it held, for the person's role.
export const load: PageLoad = async ({ url, parent }) => {
  const { user } = await parent();
  moveTab('/verification/usage', url, user);
};
