import type { JobStatus } from '@screenhire/api-client';
import type { StatusName } from '#lib/status.js';

/** A job's status as a status badge (design system section 3.2). A draft has not started and a closed job has ended. */
export const jobStatus: Record<JobStatus, { status: StatusName; label: string }> = {
  Open: { status: 'open', label: 'Open' },
  Draft: { status: 'draft', label: 'Draft' },
  Closed: { status: 'closed', label: 'Closed' },
};
