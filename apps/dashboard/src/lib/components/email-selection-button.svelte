<script lang="ts">
  import MailIcon from '@lucide/svelte/icons/mail';
  import { buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { cn } from '#lib/utils.js';

  // Opens the Email sheet for the ticked rows of a list panel. The header's pill or Move button already says how many
  // are ticked, so the label is just "Email" (W4). In a narrow panel header it is an icon, as the integrity filter is,
  // so the search keeps its room; the tooltip and the accessible name say it (A4).
  let { count, onclick }: { count: number; onclick: () => void } = $props();
</script>

<Tooltip.Root>
  <Tooltip.Trigger>
    {#snippet child({ props })}
      <button
        {...props}
        type="button"
        aria-label="Email {count} selected"
        class={cn(buttonVariants({ variant: 'outline' }), 'shrink-0 @max-3xl/panel:w-8 @max-3xl/panel:px-0 pointer-coarse:@max-3xl/panel:w-11')}
        {onclick}
      >
        <MailIcon aria-hidden="true" /><span class="@max-3xl/panel:sr-only">Email</span>
      </button>
    {/snippet}
  </Tooltip.Trigger>
  <Tooltip.Content>Email {count} selected</Tooltip.Content>
</Tooltip.Root>
