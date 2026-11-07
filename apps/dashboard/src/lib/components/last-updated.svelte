<script lang="ts">
  import HistoryIcon from '~icons/lucide/history';
  import type { LastUpdate, TeamMember } from '@screenhire/api-client';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { actorName, lastUpdateWords } from '#lib/history.js';

  // The Last updated fact (design system T6, section 13): time ago and who, the exact time on hover, and the History
  // button that opens the sheet of every change. Built-ins say "by ScreenHire". Inside `<Fact>`, whose label is
  // `lastUpdateWords(value).label`, it is the value alone ("2 hours ago by Avery Quinn"). With `lead`, where no label
  // shows (a settings page's title row), it says its own verb: "Created 2 hours ago by Avery Quinn" when nothing has
  // changed the object since, else "Updated 2 hours ago". With `titleRow`, on a header band's title row (`HistoryFact`), it
  // never wraps (S8): the words give way with an ellipsis, and History keeps its icon with its name for screen readers once
  // the band is narrower than 448px (a phone), so a title with a status badge fits. Anywhere else it wraps.
  let {
    value,
    members,
    currentUserId,
    lead = false,
    titleRow = false,
    onhistory,
  }: {
    value: LastUpdate;
    members: readonly TeamMember[] | undefined;
    currentUserId: string | undefined;
    /** Start with Created or Updated, for a header with no fact label. */
    lead?: boolean;
    /** On a header band's title row: one line, with the words and History giving way before the title. */
    titleRow?: boolean;
    /** Opens the History sheet; without it there is no History button (nothing can change the object). */
    onhistory?: () => void;
  } = $props();

  const who = $derived(actorName(value, members, currentUserId));
  const words = $derived(lastUpdateWords(value));
</script>

<span class={['inline-flex min-w-0 max-w-full items-center gap-x-2.5 gap-y-1', !titleRow && 'flex-wrap']}>
  {#if lead}
    <span class={['min-w-0', titleRow && 'truncate']}
      ><span class="sr-only">{words.label}:</span><span aria-hidden="true">{words.verb}</span>{#if value.at}{' '}<RelativeTime
          at={value.at}
        />{/if}{#if who}{' by '}<span class="whitespace-nowrap">{who}</span>{/if}</span
    >
  {:else}
    <span class="min-w-0"
      >{#if value.at}<RelativeTime at={value.at} />{/if}{#if who}{value.at ? ' by ' : 'By '}<span class="whitespace-nowrap">{who}</span
        >{/if}</span
    >
  {/if}
  {#if onhistory}
    <Button variant="outline" size="sm" class={titleRow ? 'shrink-0' : undefined} aria-haspopup="dialog" onclick={onhistory}><HistoryIcon aria-hidden="true" /><span class={titleRow ? '@max-md:sr-only' : undefined}>History</span></Button>
  {/if}
</span>
