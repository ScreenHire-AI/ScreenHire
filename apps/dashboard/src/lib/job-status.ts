import type { JobStatus } from '@screenhire/api-client';
import type { Status } from '#lib/components/status-badge.svelte';

/** A job's status as a status badge (design system section 3.2). A draft has not started and a closed job has ended. */
export const jobStatus: Record<JobStatus, { status: Status; label: string }> = {
  Open: { status: 'clear', label: 'Open' },
  Draft: { status: 'neutral', label: 'Draft' },
  Closed: { status: 'neutral', label: 'Closed' },
};
