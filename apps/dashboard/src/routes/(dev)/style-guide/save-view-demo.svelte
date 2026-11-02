<script lang="ts">
  import { ViewScope, type SavedViewSummary, type ViewFilters } from '@screenhire/api-client';
  import RenameViewDialog from '#lib/components/rename-view-dialog.svelte';
  import SaveViewButton from '#lib/components/save-view-button.svelte';
  import { Button } from '#lib/components/ui/button/index.js';

  // The button as it shows once a table's filters differ from its view, and the dialog that renames a saved view. Nothing
  // is sent from here: Rename succeeds at once, and the Save view dialog needs the signed-in app to save.
  const filters: ViewFilters = {
    baseView: null,
    jobIds: [],
    stages: ['Screening'],
    authenticity: null,
    aiAssistance: null,
    tag: null,
    sortColumn: null,
    sortDescending: false,
  };
  const sample: SavedViewSummary = {
    id: '00000000-0000-0000-0000-000000000001',
    name: 'Weekly referrals',
    scope: ViewScope.Candidates,
    jobId: null,
    shared: false,
    mine: true,
    canEdit: true,
    filters,
    updatedAt: '2026-11-02T09:00:00+00:00',
  };
  let renaming = $state<SavedViewSummary | null>(null);
</script>

<div class="flex flex-wrap items-center gap-2">
  <SaveViewButton scope={ViewScope.Candidates} {filters} show />
  <Button variant="outline" size="lg" onclick={() => (renaming = sample)}>Rename view</Button>
</div>
<RenameViewDialog view={renaming} onrename={async () => null} onclose={() => (renaming = null)} />
