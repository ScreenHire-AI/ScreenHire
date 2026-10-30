<script lang="ts" module>
  import type { ConnectionStatus } from '@screenhire/realtime';
  import type { StatusName } from '#lib/status.js';

  // Icon, colour and label together (design system section 3.2): live is done, (re)connecting is ScreenHire at work, and
  // offline is not in effect. All but live are grey: the page still works, and it reconnects on its own.
  export const liveStatus: Record<ConnectionStatus, { label: string; status: StatusName; text: string }> = {
    connected: { label: 'Live', status: 'done', text: 'text-status-clear' },
    connecting: { label: 'Connecting…', status: 'working', text: 'text-muted-foreground' },
    reconnecting: { label: 'Reconnecting…', status: 'working', text: 'text-muted-foreground' },
    disconnected: { label: 'Offline', status: 'notInEffect', text: 'text-muted-foreground' },
  };
</script>

<script lang="ts">
  import StatusIcon from '#lib/components/status-icon.svelte';

  let { status }: { status: ConnectionStatus } = $props();

  const current = $derived(liveStatus[status]);
</script>

<!-- A status region, so a screen reader hears when live updates drop and come back. A pill in the top bar, where it is
     in view on every page. While connected its icon has a slow, soft pulse behind it, so a glance says the page is live;
     the pulse stops when the person asks for less motion (section 17), and the icon and label still say it. On a phone
     only the icon shows, and the label stays for screen readers. -->
<p
  role="status"
  class={['inline-flex h-8 shrink-0 items-center gap-1.5 rounded-full border border-border bg-card px-2.5 text-caption font-semibold max-md:px-2', current.text]}
>
  <span class="relative grid place-items-center">
    {#if status === 'connected'}
      <span aria-hidden="true" data-live-pulse class="absolute inset-0 rounded-full bg-status-clear/30 motion-safe:animate-ping motion-safe:[animation-duration:2.4s]"></span>
    {/if}
    <StatusIcon status={current.status} class="relative" />
  </span>
  <span class="max-md:sr-only"><span class="sr-only">Live updates:</span> {current.label}</span>
</p>
