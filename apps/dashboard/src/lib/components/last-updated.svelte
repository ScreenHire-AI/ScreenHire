<script lang="ts">
  import HistoryIcon from '@lucide/svelte/icons/history';
  import type { LastUpdate, TeamMember } from '@screenhire/api-client';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { actorName } from '#lib/history.js';

  // The Last updated fact (design system T6, section 13): time ago and who, the exact time on hover, and the History
  // button that opens the sheet of every change. Built-ins say "by ScreenHire". Goes inside `<Fact label="Last updated">`,
  // the last fact of its header.
  let {
    value,
    members,
    currentUserId,
    onhistory,
  }: {
    value: LastUpdate;
    members: readonly TeamMember[] | undefined;
    currentUserId: string | undefined;
    /** Opens the History sheet; without it there is no History button (nothing can change the object). */
    onhistory?: () => void;
  } = $props();

  const who = $derived(actorName(value, members, currentUserId));
</script>

<span class="inline-flex min-w-0 flex-wrap items-center gap-x-2.5 gap-y-1">
  <span class="min-w-0"
    >{#if value.at}<RelativeTime at={value.at} />{/if}{#if who}{value.at ? ' by ' : 'By '}{who}{/if}</span
  >
  {#if onhistory}
    <Button variant="outline" size="sm" aria-haspopup="dialog" onclick={onhistory}><HistoryIcon aria-hidden="true" />History</Button>
  {/if}
</span>
