// The workflow page's peer views (design system section 7 and 21), shared by the template page, a job's workflow page,
// the tab strip and the panes, so a tab is named once. The tab is in the address (`?tab=`), so a link reaches it.

export const workflowTabs = [
  { value: 'overview', label: 'Overview' },
  { value: 'pipeline', label: 'Pipeline' },
  { value: 'questions', label: 'Questions' },
  { value: 'automations', label: 'Automations' },
  { value: 'test', label: 'Test' },
] as const;

export type WorkflowTab = (typeof workflowTabs)[number]['value'];

export const defaultWorkflowTab: WorkflowTab = 'overview';

/** The tabs' old names, so an address saved before the rename still opens the right one. */
const renamed: Record<string, WorkflowTab> = { graph: 'automations', questionnaire: 'questions', simulate: 'test' };

/** The tab an address names, or the first for anything else. */
export function tabFrom(value: string | null | undefined): WorkflowTab {
  if (!value) return defaultWorkflowTab;
  return workflowTabs.find((tab) => tab.value === value)?.value ?? renamed[value] ?? defaultWorkflowTab;
}

/** The address of `url` showing `tab`; the first tab is the bare address. Other parameters (`version`) stay. */
export function tabHref(url: Pick<URL, 'href'>, tab: WorkflowTab, extra: Record<string, string> = {}): string {
  const next = new URL(url.href);
  next.searchParams.delete('show');
  if (tab === defaultWorkflowTab) next.searchParams.delete('tab');
  else next.searchParams.set('tab', tab);
  for (const [key, value] of Object.entries(extra)) next.searchParams.set(key, value);
  return `${next.pathname}${next.search}`;
}
