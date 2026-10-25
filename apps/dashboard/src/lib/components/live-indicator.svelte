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

<!-- A status region, so a screen reader hears when live updates drop and come back. -->
<!-- In the sidebar's icon rail only the icon shows; the sidebar gives it a tooltip. -->
<p
  role="status"
  class={['flex h-6 items-center gap-2 px-1.5 text-caption font-semibold group-data-[collapsible=icon]:justify-center', current.text]}
>
  <StatusIcon status={current.status} />
  <span class="group-data-[collapsible=icon]:sr-only"><span class="sr-only">Live updates:</span> {current.label}</span>
</p>
