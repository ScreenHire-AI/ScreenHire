<script lang="ts">
  import type { CalendarStatus } from '@screenhire/api-client';
  import CalendarConnection from '#lib/components/calendar-connection.svelte';
  import * as Sheet from '#lib/components/ui/sheet/index.js';

  // The person's calendar as a sheet, for a page that needs it connected (scheduling an interview opens it through the
  // shell). The same connection is a section of Your account > Connected accounts.
  let {
    open = $bindable(false),
    status,
    outcome = '',
  }: {
    open?: boolean;
    status: CalendarStatus;
    /** What the last round trip said, when it failed. */
    outcome?: string;
  } = $props();
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto">
    <Sheet.Header>
      <Sheet.Title>Calendar</Sheet.Title>
    </Sheet.Header>
    <div class="px-4 pb-4">
      {#if open}<CalendarConnection {status} {outcome} />{/if}
    </div>
  </Sheet.Content>
</Sheet.Root>
