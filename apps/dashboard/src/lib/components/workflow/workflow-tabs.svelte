<script lang="ts">
  import RecordTabs, { type RecordTab } from '#lib/components/record-tabs.svelte';

  // The workflow's views, on the header band's bottom edge. Place inside the page's `Tabs.Root`, which also holds the
  // `WorkflowEditor` or `WorkflowSummary` panes. They are `RecordTabs`, the same 44px strip as the job page's, so moving
  // between the job and its workflow never shifts the frame (S8).
  const tabs: readonly RecordTab[] = [
    { value: 'pipeline', label: 'Pipeline' },
    { value: 'graph', label: 'Graph' },
    { value: 'questionnaire', label: 'Questionnaire' },
    { value: 'simulate', label: 'Simulate' },
  ];
</script>

<RecordTabs label="Workflow views" {tabs} />
