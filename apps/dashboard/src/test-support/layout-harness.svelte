<script lang="ts">
  import Fact from '#lib/components/fact.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import ListItem from '#lib/components/list-item.svelte';
  import Panel from '#lib/components/panel.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';

  // The layout components with snippets filled in, as a page fills them, for component tests.
  let {
    example,
  }: { example: 'band' | 'band-with-tabs' | 'panel' | 'flush-panel' | 'summary' | 'list' } = $props();
</script>

<Tooltip.Provider>
  <main>
    {#if example === 'band'}
      <HeaderBand title="Jordan Ellis">
        {#snippet status()}<StatusBadge status="review" label="Review suggested" />{/snippet}
        {#snippet details()}<a href="mailto:jordan.ellis@example.com">jordan.ellis@example.com</a>{/snippet}
        {#snippet actions()}
          <Button variant="outline">Add note</Button>
          <Button>Move to stage</Button>
        {/snippet}
        {#snippet facts()}
          <Fact label="Job">Senior product designer</Fact>
          <Fact label="Stage">Screening</Fact>
        {/snippet}
      </HeaderBand>
    {:else if example === 'band-with-tabs'}
      <HeaderBand title="Senior product designer" wide>
        {#snippet tabs()}
          <Tabs.Root value="pipeline">
            <Tabs.List>
              <Tabs.Trigger value="pipeline">Pipeline</Tabs.Trigger>
              <Tabs.Trigger value="details">Details</Tabs.Trigger>
            </Tabs.List>
          </Tabs.Root>
        {/snippet}
      </HeaderBand>
    {:else if example === 'panel'}
      <Panel title="Answers" count={3}>
        {#snippet actions()}<Button variant="outline">Edit</Button>{/snippet}
        <p>The body</p>
        {#snippet footer()}<p>The footer</p>{/snippet}
      </Panel>
    {:else if example === 'flush-panel'}
      <Panel title="Candidates" flush><p>Edge to edge</p></Panel>
    {:else if example === 'summary'}
      <SummaryList>
        <SummaryItem label="Title">Senior product designer</SummaryItem>
        <SummaryItem label="Location">Remote, United States</SummaryItem>
      </SummaryList>
    {:else}
      <ul>
        <ListItem href="/applications/1" title="Jordan Ellis">
          {#snippet status()}<StatusBadge status="review" label="Review suggested" />{/snippet}
          {#snippet meta()}Oct 20, 2026{/snippet}
          {#snippet facts()}<Badge variant="muted">Screening</Badge>{/snippet}
        </ListItem>
        <ListItem href="/applications/2" title="Priya Natarajan" />
      </ul>
    {/if}
  </main>
</Tooltip.Provider>
