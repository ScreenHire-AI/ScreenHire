<script lang="ts">
  import { page } from '$app/state';
  import { listOf } from '#lib/candidate-lists.js';
  import CurrentCandidates from '#lib/components/current-candidates.svelte';
  import SavedCandidates from '#lib/components/saved-candidates.svelte';

  // Candidates, in three lists on one page: Current (the people in a hiring process now), Saved (the people the team kept
  // for future roles, docs/architecture/talent-pool.md) and All (every application). The switch beside the title is the
  // address, `?list=saved` or `?list=all`, so a link and a reload keep it; `/saved` and `/talent-pool` redirect here.
  const list = $derived(listOf(page.url.searchParams));
  // The jobs a link names (`?job=`, from a report's funnel or a new-applications toast) are where the list starts, so a
  // link that names other jobs starts it again, even when the person is already on this page.
  const linkedJobs = $derived(page.url.searchParams.getAll('job').join(','));
</script>

{#if list === 'saved'}
  <SavedCandidates />
{:else}
  {#key linkedJobs}
    <CurrentCandidates scope={list} />
  {/key}
{/if}
