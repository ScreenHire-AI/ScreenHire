<script lang="ts">
  import HistoryIcon from '~icons/lucide/history';
  import type { LastUpdate, TeamMember } from '@screenhire/api-client';
  import { mergeProps } from 'bits-ui';
  import Hint from '#lib/components/hint.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { clock } from '#lib/clock.svelte.js';
  import { formatDateTime, timeAgo } from '#lib/dates.js';
  import { actorName, lastUpdateWords } from '#lib/history.js';

  // The Last updated fact (design system T6, section 13): time ago and who, the exact time on hover, and the History
  // button that opens the sheet of every change. Built-ins say "by ScreenHire". Inside `<Fact>`, whose label is
  // `lastUpdateWords(value).label`, it is the value alone ("2 hours ago by Avery Quinn"). With `lead`, where no label
  // shows (the saved note sheet), it says its own verb: "Created 2 hours ago by Avery Quinn" when nothing has
  // changed the object since, else "Updated 2 hours ago". `titleRow`, on a header band's title row (`HistoryFact`), says
  // the verb too and never wraps (S8): the words give way with an ellipsis, the whole fact and its exact time in a tooltip
  // (F5), and History keeps only its icon and a tooltip (A4) once the band is narrower than 448px (a phone), so a title
  // with a status badge fits. Anywhere else it wraps.
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
    /** On a header band's title row: its verb, on one line, with the words and History giving way before the title. */
    titleRow?: boolean;
    /** Opens the History sheet; without it there is no History button (nothing can change the object). */
    onhistory?: () => void;
  } = $props();

  const who = $derived(actorName(value, members, currentUserId));
  const words = $derived(lastUpdateWords(value));
  const wholeFact = $derived([words.verb, value.at && formatDateTime(value.at), who && `by ${who}`].filter(Boolean).join(' '));
</script>

{#snippet historyButton(props: Record<string, unknown>)}
  <Button variant="outline" size="sm" aria-haspopup="dialog" {...props}
    ><HistoryIcon aria-hidden="true" /><span class={titleRow ? '@max-md:sr-only' : undefined}>History</span></Button
  >
{/snippet}

<span class={['inline-flex min-w-0 max-w-full items-center gap-x-2.5 gap-y-1', !titleRow && 'flex-wrap']}>
  {#if titleRow}
    <Hint content={wholeFact}>
      {#snippet children({ props })}
        <span {...props} class="min-w-0 truncate"
          ><span class="sr-only">{words.label}:</span><span aria-hidden="true">{words.verb}</span>{#if value.at}{' '}<time datetime={value.at}
              >{timeAgo(value.at, clock.now)}</time
            >{/if}{#if who}{' by '}<span class="whitespace-nowrap">{who}</span>{/if}</span
        >
      {/snippet}
    </Hint>
  {:else if lead}
    <span class="min-w-0"
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
  {#if onhistory && titleRow}
    <Tooltip.Root>
      <Tooltip.Trigger>
        {#snippet child({ props })}
          {@render historyButton(mergeProps(props, { class: 'shrink-0 pointer-coarse:min-w-11', onclick: onhistory }))}
        {/snippet}
      </Tooltip.Trigger>
      <!-- A portal cannot see the band's width. From md the band is wide enough to show the word, so no tooltip repeats it. -->
      <Tooltip.Content class="md:hidden">History</Tooltip.Content>
    </Tooltip.Root>
  {:else if onhistory}
    {@render historyButton({ onclick: onhistory })}
  {/if}
</span>
