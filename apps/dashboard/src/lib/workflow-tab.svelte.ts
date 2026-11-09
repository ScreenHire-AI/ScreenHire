import { goto } from '$app/navigation';
import { page } from '$app/state';
import { tabFrom, tabHref, type WorkflowTab } from '#lib/workflow-tabs.js';

/**
 * The workflow page's tab, kept in the address. Showing another tab only changes the address (shallow, replacing the
 * entry), so the page keeps its state and unsaved edits; `LeaveDialog` does not ask before a change that only moves
 * between tabs. Call during component setup.
 */
export function createWorkflowTab() {
  const url = $derived(page.shallow?.url ?? page.url);
  const value = $derived(tabFrom(url.searchParams.get('tab')));

  return {
    get value(): WorkflowTab {
      return value;
    },
    set value(next: WorkflowTab) {
      if (next === value) return;
      void goto(tabHref(url, next), { shallow: true, replace: true, state: page.state });
    },
    /** The address that shows `tab`, for a link. */
    href(tab: WorkflowTab, extra?: Record<string, string>): string {
      return tabHref(url, tab, extra);
    },
    /** The value of `?show=`, which a link from the Test tab sets to light a path on the Automations tab. */
    get show(): string | null {
      return url.searchParams.get('show');
    },
  };
}
