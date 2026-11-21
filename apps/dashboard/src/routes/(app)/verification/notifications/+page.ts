import { moveTab } from '../tab-guard.js';
import type { PageLoad } from './$types';

// This tab merged into another (movedTabs in #lib/verification-area.ts); old links land on the tab that holds its settings now.
export const load: PageLoad = ({ url }) => moveTab('/verification/notifications', url);
