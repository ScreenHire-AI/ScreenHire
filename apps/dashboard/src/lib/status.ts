import ArchiveIcon from '@lucide/svelte/icons/archive';
import CircleCheckIcon from '@lucide/svelte/icons/circle-check';
import CircleMinusIcon from '@lucide/svelte/icons/circle-minus';
import ClockAlertIcon from '@lucide/svelte/icons/clock-alert';
import Clock4Icon from '@lucide/svelte/icons/clock-4';
import DoorClosedIcon from '@lucide/svelte/icons/door-closed';
import DoorOpenIcon from '@lucide/svelte/icons/door-open';
import EyeIcon from '@lucide/svelte/icons/eye';
import FilePenLineIcon from '@lucide/svelte/icons/file-pen-line';
import IdCardIcon from '@lucide/svelte/icons/id-card';
import LoaderCircleIcon from '@lucide/svelte/icons/loader-circle';
import ShieldXIcon from '@lucide/svelte/icons/shield-x';
import SparklesIcon from '@lucide/svelte/icons/sparkles';
import WrenchIcon from '@lucide/svelte/icons/wrench';
import type { LucideIcon } from '#lib/shell.js';

/**
 * A status's colour (design system section 3.2). `ai` is information, not a status; `neutral` is grey: waiting on
 * someone else, ScreenHire at work, not started or ended. Nobody needs to act on a grey status.
 */
export type StatusTone = 'clear' | 'review' | 'verify' | 'ai' | 'neutral';

export interface StatusView {
  tone: StatusTone;
  /** The Lucide icon's name, as section 3.2 lists it. */
  iconName: string;
  icon: LucideIcon;
}

const view = (tone: StatusTone, iconName: string, icon: LucideIcon): StatusView => ({ tone, iconName, icon });

/**
 * Every status the dashboard shows, by meaning, with the icon and colour of its row in section 3.2. The icon alone tells
 * statuses apart, so two meanings never share an icon. The label is the caller's: "Overdue by 2 days", "3 to fix".
 * A new status takes the row whose meaning it shares, and joins the table in section 3.2.
 */
export const statuses = {
  /** No concerns, No problems found, Delivering, Delivered, Done, Active. */
  done: view('clear', 'circle-check', CircleCheckIcon),
  /** Open, a job taking applications; Live, a job showing on a board. */
  open: view('clear', 'door-open', DoorOpenIcon),
  /** Review suggested. */
  review: view('review', 'eye', EyeIcon),
  /** Overdue: the hiring team is late, never the candidate. */
  overdue: view('review', 'clock-alert', ClockAlertIcon),
  /** Verification suggested. */
  verification: view('verify', 'id-card', IdCardIcon),
  /** Needs fixes: a refused or failed posting, a workflow with problems, a webhook turned off or a delivery that failed. */
  needsFixes: view('verify', 'wrench', WrenchIcon),
  /** Unsafe file, flagged by the malware scan. */
  unsafeFile: view('verify', 'shield-x', ShieldXIcon),
  /** No signs, Some signs or Strong signs of AI help: information, never a warning. */
  ai: view('ai', 'sparkles', SparklesIcon),
  /** Sent, Queued, Removing: waiting on someone else. */
  waiting: view('neutral', 'clock-4', Clock4Icon),
  /** Scanning, Checks pending: ScreenHire at work. */
  working: view('neutral', 'loader-circle', LoaderCircleIcon),
  /** Closed, a job that takes no more applications. */
  closed: view('neutral', 'door-closed', DoorClosedIcon),
  /** Draft. */
  draft: view('neutral', 'file-pen-line', FilePenLineIcon),
  /** Archived, a template taken out of the pickers until someone restores it. */
  archived: view('neutral', 'archive', ArchiveIcon),
  /** Not posted, Removed, Off, Skipped, Revoked: not in effect. */
  notInEffect: view('neutral', 'circle-minus', CircleMinusIcon),
} as const satisfies Record<string, StatusView>;

export type StatusName = keyof typeof statuses;

/** A status someone needs to act on (review or verify). In lists and tables only these get a filled badge (T3). */
export const needsAttention = (status: StatusName): boolean => statuses[status].tone === 'review' || statuses[status].tone === 'verify';
