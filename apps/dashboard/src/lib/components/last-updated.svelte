<script lang="ts">
  import type { LastUpdate, TeamMember } from '@screenhire/api-client';
  import Hint from '#lib/components/hint.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import { clock } from '#lib/clock.svelte.js';
  import { formatDateTime, timeAgo } from '#lib/dates.js';
  import { actorName, lastUpdateWords } from '#lib/history.js';

  // The Last updated fact (design system T6, section 13): time ago and who, the exact time on hover. With `onhistory`
  // the fact itself is the button that opens the sheet of every change. Built-ins say "by ScreenHire". Inside `<Fact>`,
  // whose label is `lastUpdateWords(value).label`, it is the value alone ("2 hours ago by Avery Quinn"). With `lead`,
  // where no label shows (the saved note sheet), it says its own verb: "Created 2 hours ago by Avery Quinn" when nothing
  // has changed the object since, else "Updated 2 hours ago". `titleRow`, on a header band's title row (`HistoryFact`),
  // says the verb too and never wraps (S8): the words give way with an ellipsis, the whole fact and its exact time in a
  // tooltip (F5). Anywhere else it wraps.
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
    /** On a header band's title row: its verb, on one line, with the words giving way before the title. */
    titleRow?: boolean;
    /** Opens the History sheet from the fact; without it the fact is plain text (nothing can change the object). */
    onhistory?: () => void;
  } = $props();

  const who = $derived(actorName(value, members, currentUserId));
  const words = $derived(lastUpdateWords(value));
  const wholeFact = $derived([words.verb, value.at && formatDateTime(value.at), who && `by ${who}`].filter(Boolean).join(' '));
</script>

<!-- The words of the fact. `plain` draws the time as text, for inside the History button, where RelativeTime's own tooltip
     would put one control inside another. -->
{#snippet fact(plain: boolean)}
  {#if titleRow || lead}<span class="sr-only">{words.label}:</span><span aria-hidden="true">{words.verb}</span>{#if value.at}{' '}{#if titleRow || plain}<time
          datetime={value.at}>{timeAgo(value.at, clock.now)}</time
        >{:else}<RelativeTime at={value.at} />{/if}{/if}{#if who}{' by '}<span class="whitespace-nowrap">{who}</span>{/if}
  {:else}{#if value.at}{#if plain}<time datetime={value.at}>{timeAgo(value.at, clock.now)}</time>{:else}<RelativeTime at={value.at} />{/if}{/if}{#if who}{value.at
        ? ' by '
        : 'By '}<span class="whitespace-nowrap">{who}</span>{/if}
  {/if}
{/snippet}

<span class={['inline-flex min-w-0 max-w-full items-center gap-x-2.5 gap-y-1', !titleRow && 'flex-wrap']}>
  {#if onhistory}
    <!-- The fact is the way to its history (the owner's walk-through, 2026-11-21): a History button beside the words was the
         one button in a row of facts. A dotted underline says it opens something; the tooltip holds the exact time and says
         what it opens, and the name ends "History" for a screen reader and for search. -->
    <Hint content={`${wholeFact}. See every change.`}>
      {#snippet children({ props })}
        <button
          {...props}
          type="button"
          aria-haspopup="dialog"
          onclick={onhistory}
          class={[
            'min-w-0 rounded-sm text-left underline decoration-dotted underline-offset-4 hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-hidden pointer-coarse:min-h-11',
            titleRow && 'truncate',
          ]}>{@render fact(true)}<span class="sr-only">. History</span></button
        >
      {/snippet}
    </Hint>
  {:else if titleRow}
    <Hint content={wholeFact}>
      {#snippet children({ props })}
        <span {...props} class="min-w-0 truncate">{@render fact(false)}</span>
      {/snippet}
    </Hint>
  {:else}
    <span class="min-w-0">{@render fact(false)}</span>
  {/if}
</span>
