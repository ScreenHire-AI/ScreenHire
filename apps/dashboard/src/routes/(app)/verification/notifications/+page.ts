import { guardTab } from '../tab-guard.js';
import type { PageLoad } from './$types';

export const load: PageLoad = async ({ parent }) => {
  const { user } = await parent();
  guardTab('notifications', user);
};
