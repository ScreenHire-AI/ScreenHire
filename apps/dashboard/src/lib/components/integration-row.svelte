<script lang="ts">
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import type { Connection, Integration } from '#lib/integrations.js';

  // One row of the Integrations directory (design system section 11): its icon, its name, one line on what it does and,
  // where the integration can tell, whether it is set up. The whole row is the link to the integration's page. Place it
  // in a `<ul>`. `IntegrationEntry` asks the question that gives `state`.
  let { integration, state }: { integration: Pick<Integration, 'label' | 'href' | 'icon' | 'description'>; state?: Connection } = $props();

  const Icon = $derived(integration.icon);
</script>

<li data-slot="integration-row" class="relative flex min-h-16 min-w-0 items-center gap-3 border-b border-border-subtle px-4.5 py-3 last:border-0 hover:bg-muted max-md:px-4">
  <span aria-hidden="true" class="grid size-9 shrink-0 place-items-center rounded-lg bg-secondary text-secondary-foreground">
    <Icon />
  </span>
  <div class="flex min-w-0 flex-1 flex-col">
    <a href={integration.href} class="min-w-0 truncate font-semibold text-foreground no-underline after:absolute after:inset-0 hover:underline">{integration.label}</a>
    <span class="relative min-w-0 text-muted-foreground"><TruncatedText text={integration.description} /></span>
  </div>
  {#if state === 'connected'}
    <StatusBadge status="done" label="Connected" quiet />
  {:else if state === 'notConnected'}
    <StatusBadge status="notInEffect" label="Not connected" quiet />
  {/if}
</li>
