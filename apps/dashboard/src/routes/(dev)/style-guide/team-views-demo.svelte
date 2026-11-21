<script lang="ts">
  import { ViewScope, type SavedViewSummary, type ViewFilters } from '@screenhire/api-client';
  import { QueryClient, setQueryClientContext } from '@tanstack/svelte-query';
  import UserRoundSearchIcon from '~icons/lucide/user-round-search';
  import TeamViewsSheet from '#lib/components/team-views-sheet.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import type { ViewActions } from '#lib/views.svelte.js';

  // The sheet the sidebar's Browse team views opens, with one view shared since the last visit and one already in the
  // sidebar. The candidate count under each view is read from the API the dev server proxies, and stays empty without
  // one. The actions change nothing and succeed at once.
  setQueryClientContext(new QueryClient({ defaultOptions: { queries: { retry: false } } }));
  const filters = (overrides: Partial<ViewFilters>): ViewFilters => ({
    baseView: null,
    jobIds: [],
    stages: [],
    authenticity: null,
    aiAssistance: null,
    tag: null,
    sortColumn: null,
    sortDescending: false,
    ...overrides,
  });
  const shared = (overrides: Partial<SavedViewSummary>): SavedViewSummary => ({
    id: '00000000-0000-0000-0000-000000000011',
    name: 'Referrals in screening',
    scope: ViewScope.Candidates,
    jobId: null,
    shared: true,
    mine: false,
    canEdit: false,
    inSidebar: false,
    ownerName: 'Jordan Ellis',
    sharedAt: '2026-11-18T09:00:00+00:00',
    filters: filters({ stages: ['Screening'], tag: 'referral' }),
    updatedAt: '2026-11-18T09:00:00+00:00',
    ...overrides,
  });
  const views = [
    shared({}),
    shared({
      id: '00000000-0000-0000-0000-000000000012',
      name: 'Needs a second look',
      inSidebar: true,
      sharedAt: '2026-10-02T09:00:00+00:00',
      filters: filters({ baseView: 'needs-review' }),
    }),
  ];
  const actions: ViewActions = {
    rename: async () => null,
    setShared: async () => null,
    remove: async () => null,
    setHidden: async () => null,
    addToSidebar: async () => null,
    removeFromSidebar: async () => null,
    markTeamViewsOpened: async () => null,
  };
  let open = $state(false);
</script>

<Button variant="outline" size="lg" onclick={() => (open = true)} aria-haspopup="dialog"><UserRoundSearchIcon aria-hidden="true" />Browse team views</Button>
<TeamViewsSheet bind:open {views} seenAt="2026-11-01T00:00:00+00:00" jobTitles={{}} {actions} />
