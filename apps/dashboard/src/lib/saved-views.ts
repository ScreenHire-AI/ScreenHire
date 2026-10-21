import type { ApplicationSummary } from '@screenhire/api-client';
import CalendarPlusIcon from '@lucide/svelte/icons/calendar-plus';
import InboxIcon from '@lucide/svelte/icons/inbox';
import MessagesSquareIcon from '@lucide/svelte/icons/messages-square';
import { needsReview, verificationRecommended } from '#lib/integrity.js';
import type { LucideIcon } from '#lib/shell.js';

/**
 * A ready-made filter on the candidates table, listed in the sidebar with its count. A view of an integrity status shows
 * that status's mark; any other view shows an icon (design system I2).
 */
export type SavedView = {
  id: string;
  label: string;
  matches(application: ApplicationSummary, now: number): boolean;
} & ({ mark: { shape: 'diamond' | 'square'; tone: 'review' | 'verify' }; icon?: never } | { icon: LucideIcon; mark?: never });

const day = 24 * 60 * 60 * 1000;

/**
 * The sidebar's saved views, in order. They open `/candidates?view=<id>`. The integrity views follow authenticity only:
 * AI assistance is information, not a queue (ADR-0004). A dismissed assessment leaves both.
 */
export const savedViews: readonly SavedView[] = [
  {
    id: 'needs-review',
    label: 'Needs review',
    mark: { shape: 'diamond', tone: 'review' },
    matches: (application) => needsReview(application),
  },
  {
    id: 'verification-recommended',
    label: 'Verification recommended',
    mark: { shape: 'square', tone: 'verify' },
    matches: (application) => verificationRecommended(application),
  },
  {
    id: 'new-today',
    label: 'New today',
    icon: CalendarPlusIcon,
    matches: (application, now) => now - Date.parse(application.submittedAt) < day,
  },
  {
    id: 'not-screened',
    label: 'Not screened yet',
    icon: InboxIcon,
    matches: (application) => application.stage.kind === 'Applied',
  },
  {
    id: 'interviewing',
    label: 'Interviewing',
    icon: MessagesSquareIcon,
    matches: (application) => application.stage.kind === 'Interview',
  },
];

export const findSavedView = (id: string | null) => savedViews.find((view) => view.id === id);
