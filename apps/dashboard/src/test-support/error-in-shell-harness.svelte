<script lang="ts">
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import Page from '../routes/(app)/+error.svelte';

  // The (app) error page inside the providers the signed-in layout gives it: the tooltip provider and the sidebar's.
</script>

<Tooltip.Provider>
  <Sidebar.Provider>
    <Sidebar.Inset><Page /></Sidebar.Inset>
  </Sidebar.Provider>
</Tooltip.Provider>
