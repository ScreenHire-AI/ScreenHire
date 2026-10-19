import { listJobs } from '@screenhire/api-client';
import type { PageLoad } from './$types';

export const load: PageLoad = async () => {
  const response = await listJobs();
  if (response.status !== 200) {
    throw new Error(`Could not load jobs (HTTP ${response.status}).`);
  }
  return { jobs: response.data };
};
