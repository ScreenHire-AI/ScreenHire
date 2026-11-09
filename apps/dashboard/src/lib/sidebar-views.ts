import type { SavedViewSummary } from '@screenhire/api-client';
import ListFilterIcon from '~icons/lucide/list-filter';
import UsersIcon from '~icons/lucide/users';
import { savedViews as builtInViews } from '#lib/saved-views.js';
import type { LucideIcon } from '#lib/shell.js';

/** One row of the sidebar's Views list: a built-in view, or one that a person saved. */
export interface SidebarView {
  /** The built-in view's id, or the saved view's id. Unique across both, because a saved view's id is a uuid. */
  key: string;
  label: string;
  href: string;
  builtIn: boolean;
  /** The saved view behind a row that is not built in. */
  saved?: SavedViewSummary;
  /** A plain icon, never a status icon (design system I2). */
  icon: LucideIcon;
  /** The job's title, for a view of one job's table. */
  jobTitle?: string;
}

/** Where a view opens: the Candidates page, or the job's candidates tab. */
export function viewHref(view: Pick<SavedViewSummary, 'id' | 'scope' | 'jobId'>): string {
  return view.scope === 'Job' && view.jobId ? `/jobs/${view.jobId}?tab=candidates&view=${view.id}` : `/candidates?view=${view.id}`;
}

/**
 * The sidebar's views: the built-in ones the person has not hidden, then the saved ones they see (their own and the
 * team's); and the built-in ones they hid, behind "Show hidden views". A view has no count: it only filters.
 */
export function sidebarViews(input: {
  saved: readonly SavedViewSummary[];
  hiddenBuiltIns: readonly string[];
  jobTitles: Readonly<Record<string, string>>;
}): { visible: SidebarView[]; hidden: SidebarView[] } {
  const builtIn = builtInViews.map(
    (view): SidebarView => ({
      key: view.id,
      label: view.label,
      href: `/candidates?view=${view.id}`,
      builtIn: true,
      icon: view.icon ?? ListFilterIcon,
    }),
  );
  const saved = input.saved.map((view): SidebarView => {
    return {
      key: view.id,
      label: view.name,
      href: viewHref(view),
      builtIn: false,
      saved: view,
      icon: view.shared && !view.mine ? UsersIcon : ListFilterIcon,
      jobTitle: view.jobId ? input.jobTitles[view.jobId] : undefined,
    };
  });
  const hidden = new Set(input.hiddenBuiltIns);
  return {
    visible: [...builtIn.filter((view) => !hidden.has(view.key)), ...saved],
    hidden: builtIn.filter((view) => hidden.has(view.key)),
  };
}
