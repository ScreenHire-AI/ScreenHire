<script lang="ts">
  import RecordTabs, { type RecordTab } from '#lib/components/record-tabs.svelte';
  import { workflowTabs } from '#lib/workflow-tabs.js';

  // The workflow's views, on the header band's bottom edge. Place inside the page's `Tabs.Root`, which also holds the
  // `WorkflowEditor` or `WorkflowSummary` panes. They are `RecordTabs`, the same 44px strip as the job page's, so moving
  // between the job and its workflow never shifts the frame (S8). The names are in `#lib/workflow-tabs.ts`.
  const tabs: readonly RecordTab[] = workflowTabs;
</script>

<RecordTabs label="Workflow views" {tabs} />
