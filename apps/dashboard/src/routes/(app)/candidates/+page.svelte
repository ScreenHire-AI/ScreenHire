<script lang="ts">
  import { page } from '$app/state';
  import { listOf } from '#lib/candidate-lists.js';
  import CurrentCandidates from '#lib/components/current-candidates.svelte';
  import SavedCandidates from '#lib/components/saved-candidates.svelte';

  // Candidates, in three lists on one page: Current (the people in a hiring process now), Saved (the people the team kept
  // for future roles, docs/architecture/talent-pool.md) and All (every application). The switch beside the title is the
  // address, `?list=saved` or `?list=all`, so a link and a reload keep it; `/saved` and `/talent-pool` redirect here.
  const list = $derived(listOf(page.url.searchParams));
</script>

{#if list === 'saved'}
  <SavedCandidates />
{:else}
  <CurrentCandidates scope={list} />
{/if}
