import { listJobs } from '@screenhire/api-client';
import { jobsDependency } from '#lib/realtime.js';
import type { PageLoad } from './$types';

export const load: PageLoad = async ({ depends }) => {
  // Live JobPublished and JobClosed events re-run this load (#lib/realtime.ts).
  depends(jobsDependency);
  const response = await listJobs();
  if (response.status !== 200) {
    throw new Error(`Could not load jobs (HTTP ${response.status}).`);
  }
  return { jobs: response.data };
};
