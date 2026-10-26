<script lang="ts">
  import * as Tabs from '#lib/components/ui/tabs/index.js';

  // The workflow's views, on the header band's bottom edge. Place inside the page's `Tabs.Root`, which also holds the
  // `WorkflowEditor` or `WorkflowSummary` panes.
</script>

<Tabs.List variant="line" aria-label="Workflow views">
  <Tabs.Trigger value="pipeline">Pipeline</Tabs.Trigger>
  <Tabs.Trigger value="graph">Graph</Tabs.Trigger>
  <Tabs.Trigger value="questionnaire">Questionnaire</Tabs.Trigger>
  <Tabs.Trigger value="simulate">Simulate</Tabs.Trigger>
</Tabs.List>
