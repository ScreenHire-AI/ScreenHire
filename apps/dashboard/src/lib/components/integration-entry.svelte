<script lang="ts">
  import type { CurrentUser } from '@screenhire/api-client';
  import IntegrationRow from '#lib/components/integration-row.svelte';
  import type { Integration } from '#lib/integrations.js';

  // An integration's row with its own state: the question is asked when the row starts, as every query is, so the
  // directory runs one query per integration that has one, and none for an integration that cannot tell.
  let { integration, user }: { integration: Integration; user: CurrentUser | null | undefined } = $props();

  // svelte-ignore state_referenced_locally
  const connection = integration.useConnection?.(user);
</script>

<IntegrationRow {integration} state={connection?.()} />
