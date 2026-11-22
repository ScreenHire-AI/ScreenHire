import { moveTab } from '../tab-guard.js';
import type { PageLoad } from './$types';

// This tab merged into Checks (movedTabs in #lib/verification-area.ts); old links land there.
export const load: PageLoad = async ({ url, parent }) => {
  const { user } = await parent();
  moveTab('/verification/policy', url, user);
};
