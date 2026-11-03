<script lang="ts">
  import MailIcon from '~icons/lucide/mail';
  import { buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { watchTruncation } from '#lib/truncation.js';
  import { cn } from '#lib/utils.js';

  // Opens the email composer for the ticked rows of a list panel. The header's pill or Move button already says how many
  // are ticked, so the label is just "Email" (W4). In a narrow panel header it is an icon, as the verification filter is,
  // so the search keeps its room; the accessible name says it, and the tooltip too while the label is hidden (A4).
  // lg in a panel body row, beside the other 36px controls (design system section 5); the default fits a panel header.
  let { count, onclick, size = 'default' }: { count: number; onclick: () => void; size?: 'default' | 'lg' } = $props();

  let labelCut = $state(false);
  const watchLabel = watchTruncation((cut) => (labelCut = cut));
</script>

<Tooltip.Root disabled={!labelCut}>
  <Tooltip.Trigger>
    {#snippet child({ props })}
      <button
        {...props}
        type="button"
        aria-label="Email {count} selected"
        class={cn(
          buttonVariants({ variant: 'outline', size }),
          'shrink-0 @max-3xl/panel:px-0 pointer-coarse:@max-3xl/panel:w-11',
          size === 'lg' ? '@max-3xl/panel:w-9' : '@max-3xl/panel:w-8',
        )}
        {onclick}
      >
        <MailIcon aria-hidden="true" /><span {@attach watchLabel} class="@max-3xl/panel:sr-only">Email</span>
      </button>
    {/snippet}
  </Tooltip.Trigger>
  <Tooltip.Content>Email {count} selected</Tooltip.Content>
</Tooltip.Root>
