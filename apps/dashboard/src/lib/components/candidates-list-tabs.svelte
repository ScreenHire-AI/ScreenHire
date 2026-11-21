<script lang="ts">
  import { candidateLists, type ListCounts } from '#lib/candidate-lists.js';
  import RecordTabs from '#lib/components/record-tabs.svelte';

  // Candidates' three lists as the tabs under the page's title (design system section 11, the list page), as Templates
  // has its kinds and My tasks whose tasks: Current (the people in a hiring process now), Saved (the people the team kept
  // for future roles, docs/architecture/talent-pool.md) and All (every application). Each tab carries its count from the
  // application and saved-people queries (`listCounts`), which both lists read. Render it inside the list's `Tabs.Root`,
  // whose value is the list and whose change goes to that list's address (`candidateListHref`), so a link and a reload
  // keep the choice, and each list starts with filters of its own.
  let { counts = {} }: { counts?: ListCounts } = $props();

  const tabs = $derived(candidateLists.map((list) => ({ value: list.value, label: list.label, count: counts[list.value] })));
</script>

<RecordTabs {tabs} label="Candidate lists" />
