<script lang="ts">
  import { page } from '$app/state';
  import CurrentCandidates from '#lib/components/current-candidates.svelte';
  import SavedCandidates from '#lib/components/saved-candidates.svelte';

  // Candidates, in two lists on one page: Current (every application) and Saved (the people the team kept for future
  // roles, docs/architecture/talent-pool.md). The switch beside the title is the address, `?list=saved`, so a link and a
  // reload keep it; `/saved` and `/talent-pool` redirect here.
  const saved = $derived(page.url.searchParams.get('list') === 'saved');
</script>

{#if saved}
  <SavedCandidates />
{:else}
  <CurrentCandidates />
{/if}
