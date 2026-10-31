import type { ApplicationSummary } from '@screenhire/api-client';
import CalendarPlusIcon from '@lucide/svelte/icons/calendar-plus';
import InboxIcon from '@lucide/svelte/icons/inbox';
import MessagesSquareIcon from '@lucide/svelte/icons/messages-square';
import { needsReview, verificationRecommended } from '#lib/verification.js';
import type { LucideIcon } from '#lib/shell.js';
import { isOverdue } from '#lib/sla.js';
import type { StatusName } from '#lib/status.js';

/**
 * A ready-made filter on the candidates table, listed in the sidebar with its count. A view of a status (a verification
 * level, Overdue) shows that status's icon; any other view shows an ordinary icon (design system I2). A status view picks rows by
 * something that must never drive a decision against a candidate, so the candidates page offers no bulk email on it.
 */
export type SavedView = {
  id: string;
  label: string;
  matches(application: ApplicationSummary, now: number): boolean;
} & ({ status: StatusName; icon?: never } | { icon: LucideIcon; status?: never });

const day = 24 * 60 * 60 * 1000;

/**
 * The sidebar's saved views, in order. They open `/candidates?view=<id>`. The verification views follow authenticity only:
 * AI assistance is information, not a queue (ADR-0004). A dismissed assessment leaves both.
 */
export const savedViews: readonly SavedView[] = [
  {
    id: 'needs-review',
    label: 'To review',
    status: 'review',
    matches: (application) => needsReview(application),
  },
  {
    id: 'verification-recommended',
    label: 'To verify',
    status: 'verification',
    matches: (application) => verificationRecommended(application),
  },
  {
    // Past the stage's time limit: the team's delay, never the candidate's (design system section 3.2).
    id: 'overdue',
    label: 'Overdue',
    status: 'overdue',
    matches: (application, now) => isOverdue(application, now),
  },
  {
    id: 'new-today',
    label: 'New today',
    icon: CalendarPlusIcon,
    matches: (application, now) => now - Date.parse(application.submittedAt) < day,
  },
  {
    id: 'not-screened',
    label: 'Not screened',
    icon: InboxIcon,
    matches: (application) => application.stage.kind === 'Applied' && !application.keptInMindAt,
  },
  {
    id: 'interviewing',
    label: 'Interviewing',
    icon: MessagesSquareIcon,
    matches: (application) => application.stage.kind === 'Interview' && !application.keptInMindAt,
  },
];

export const findSavedView = (id: string | null) => savedViews.find((view) => view.id === id);
