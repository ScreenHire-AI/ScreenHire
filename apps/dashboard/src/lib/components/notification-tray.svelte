<script lang="ts">
  import AtSignIcon from '@lucide/svelte/icons/at-sign';
  import BellIcon from '@lucide/svelte/icons/bell';
  import XIcon from '@lucide/svelte/icons/x';
  import IconButton from '#lib/components/icon-button.svelte';
  import type { Notification } from '#lib/notifications.svelte.js';
  import { cn } from '#lib/utils.js';

  // The shell's notifications (design system section 11): overlay cards in the bottom corner, newest on top, each with
  // an icon for what raised it (a bell for the team, an at sign for a mention), its link and a Dismiss button. They never time out, because each holds a link (WCAG 2.2.1). The region is always in
  // the page, so a screen reader hears a new one politely. On phones they sit above a page's action bar.
  let {
    items,
    ondismiss,
    class: className,
  }: { items: readonly Notification[]; ondismiss: (id: string) => void; class?: string } = $props();
</script>

<section
  aria-label="Notifications"
  class={cn('pointer-events-none fixed right-4 bottom-4 z-50 flex w-full max-w-sm flex-col gap-2 max-md:bottom-24 max-md:left-4 max-md:w-auto', className)}
>
  <ol aria-live="polite" class="flex flex-col gap-2">
    {#each items as item (item.id)}
      <li class="pointer-events-auto flex items-start gap-3 rounded-panel border border-border bg-card p-3 shadow-overlay">
        <span aria-hidden="true" class="grid size-7 shrink-0 place-items-center rounded-full bg-secondary text-muted-foreground">{#if item.kind === 'mention'}<AtSignIcon />{:else}<BellIcon />{/if}</span>
        <div class="flex min-w-0 flex-1 flex-col gap-0.5 pt-1">
          <a href={item.href} class="font-semibold break-words underline underline-offset-2">{item.title}</a>
          <p class="break-words">{item.detail}</p>
        </div>
        <IconButton label="Dismiss notification" onclick={() => ondismiss(item.id)}><XIcon aria-hidden="true" /></IconButton>
      </li>
    {/each}
  </ol>
</section>
