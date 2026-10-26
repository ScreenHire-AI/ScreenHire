<script lang="ts" generics="TResponse extends { data: HistoryPage }, TError">
  import type { HistoryPage, LastUpdate } from '@screenhire/api-client';
  import { createListTeamMembers } from '@screenhire/api-client/svelte-query';
  import type { CreateQueryOptions, QueryKey } from '@tanstack/svelte-query';
  import { page as appPage } from '$app/state';
  import Fact from '#lib/components/fact.svelte';
  import HistorySheet from '#lib/components/history-sheet.svelte';
  import LastUpdated from '#lib/components/last-updated.svelte';
  import { createHistoryPages } from '#lib/history-pages.svelte.js';

  // A header's last fact (design system T6, section 11 History sheet): Last updated with History, and the sheet it
  // opens, for a page whose header shows nothing else of the history. `page` is the generated query options for one
  // page of the object's history.
  let {
    value,
    name,
    page,
  }: {
    value: LastUpdate;
    /** The object's name: the sheet's one line of description. */
    name: string;
    page: (before: string | undefined) => CreateQueryOptions<TResponse, TError, TResponse, QueryKey>;
  } = $props();

  const team = createListTeamMembers();
  let open = $state(false);
  const history = createHistoryPages(
    (before) => page(before),
    () => open,
  );
</script>

<Fact label="Last updated">
  <LastUpdated {value} members={team.data?.data} currentUserId={appPage.data.user?.id} onhistory={() => (open = true)} />
</Fact>
<HistorySheet
  bind:open
  {name}
  entries={history.entries}
  error={history.error}
  onretry={history.retry}
  hasMore={history.hasMore}
  loadingMore={history.loadingMore}
  onmore={history.more}
  members={team.data?.data}
  currentUserId={appPage.data.user?.id}
/>
