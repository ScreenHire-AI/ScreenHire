<script lang="ts">
  import AtSignIcon from '@lucide/svelte/icons/at-sign';
  import BellIcon from '@lucide/svelte/icons/bell';
  import LayoutTemplateIcon from '@lucide/svelte/icons/layout-template';
  import XIcon from '@lucide/svelte/icons/x';
  import type { ToastKind } from '@screenhire/api-client';
  import type { Component } from 'svelte';
  import IconButton from '#lib/components/icon-button.svelte';
  import { eventIcons } from '#lib/activity.js';
  import { isToast, type Hold, type TrayItem } from '#lib/notifications.svelte.js';
  import { cn } from '#lib/utils.js';

  // The shell's notifications (design system section 11): overlay cards in the bottom corner, newest on top, each with
  // an icon for what raised it, its link and a Dismiss button. A card (a bell for the team, an at sign for a mention)
  // never times out, because it holds a link (WCAG 2.2.1). A toast says who changed what, with the record as its link,
  // and closes on its own unless the pointer or the focus is here (`onhold`), or the person turned that off. The region
  // is always in the page, so a screen reader hears a new one politely. On phones they sit above a page's action bar.
  let {
    items,
    ondismiss,
    onhold,
    class: className,
  }: { items: readonly TrayItem[]; ondismiss: (id: string) => void; onhold?: (reason: Hold, held: boolean) => void; class?: string } =
    $props();

  // The same icon a candidate's Activity shows for the change, and the Templates icon for a template.
  const toastIcons: Record<ToastKind, Component> = {
    Moves: eventIcons.StageChanged,
    NewCandidates: eventIcons.Submitted,
    Notes: eventIcons.NoteAdded,
    Tags: eventIcons.TagAdded,
    Emails: eventIcons.EmailSent,
    Scorecards: eventIcons.ScorecardSubmitted,
    Tasks: eventIcons.TaskCreated,
    Templates: LayoutTemplateIcon,
  };

  function focusout(event: FocusEvent) {
    const section = event.currentTarget as HTMLElement;
    if (!(event.relatedTarget instanceof Node) || !section.contains(event.relatedTarget)) onhold?.('focus', false);
  }
</script>

<section
  aria-label="Notifications"
  class={cn('pointer-events-none fixed right-4 bottom-4 z-50 flex w-full max-w-sm flex-col gap-2 max-md:bottom-24 max-md:left-4 max-md:w-auto', className)}
  onfocusin={() => onhold?.('focus', true)}
  onfocusout={focusout}
>
  <ol aria-live="polite" class="flex flex-col gap-2">
    {#each items as item (item.id)}
      <li
        class="pointer-events-auto flex items-start gap-3 rounded-panel border border-border bg-card p-3 shadow-overlay"
        onpointerenter={() => onhold?.('pointer', true)}
        onpointerleave={() => onhold?.('pointer', false)}
      >
        {#if isToast(item)}
          {@const Icon = toastIcons[item.kind]}
          <span aria-hidden="true" class="grid size-7 shrink-0 place-items-center rounded-full bg-secondary text-muted-foreground"><Icon /></span>
          <div class="flex min-w-0 flex-1 flex-col gap-0.5 pt-1">
            <p class="break-words">{item.sentence[0]}<a href={item.href} class="font-semibold underline underline-offset-2">{item.sentence[1]}</a>{item.sentence[2]}</p>
            {#if item.context}<p class="break-words text-muted-foreground">{item.context}</p>{/if}
          </div>
        {:else}
          <span aria-hidden="true" class="grid size-7 shrink-0 place-items-center rounded-full bg-secondary text-muted-foreground">{#if item.kind === 'mention'}<AtSignIcon />{:else}<BellIcon />{/if}</span>
          <div class="flex min-w-0 flex-1 flex-col gap-0.5 pt-1">
            <a href={item.href} class="font-semibold break-words underline underline-offset-2">{item.title}</a>
            <p class="break-words">{item.detail}</p>
          </div>
        {/if}
        <!-- A removed row sends no pointerleave, so dismissing lets the other toasts count down again. -->
        <IconButton
          label="Dismiss notification"
          onclick={() => {
            onhold?.('pointer', false);
            ondismiss(item.id);
          }}><XIcon aria-hidden="true" /></IconButton
        >
      </li>
    {/each}
  </ol>
</section>
