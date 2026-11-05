<script lang="ts">
  import RecordTabs from '#lib/components/record-tabs.svelte';
  import * as Tabs from '#lib/components/ui/tabs/index.js';

  // A job's tabs inside the page's Tabs.Root, as a header band holds them.
  let value = $state('candidates');
</script>

<Tabs.Root bind:value>
  <RecordTabs
    label="Job sections"
    tabs={[
      { value: 'candidates', label: 'Candidates', count: 24 },
      { value: 'postings', label: 'Job postings', count: 7 },
      { value: 'verification', label: 'Verification', count: 5 },
      { value: 'details', label: 'Details' },
      { value: 'activity', label: 'Activity', count: 3 },
    ]}
  />
</Tabs.Root>
