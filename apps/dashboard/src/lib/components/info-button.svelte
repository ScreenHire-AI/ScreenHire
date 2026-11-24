<script lang="ts">
  import InfoIcon from '~icons/lucide/info';
  import type { Snippet } from 'svelte';
  import { buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Popover from '#lib/components/ui/popover/index.js';
  import { cn } from '#lib/utils.js';

  // An explanation one press away (design system section 11, Info button; W14): a quiet `info` icon beside what it explains,
  // whose words open on hover, a click, a tap or keyboard focus, and close on Escape, a click elsewhere or moving on. A click
  // while it is open from a hover keeps it open. It holds words only, never a control, so focus stays on the button and
  // nothing is trapped. What people must know before they act, and anything they agree to, stays on the page (W14).
  let {
    label,
    text,
    children,
    class: className,
  }: {
    /** What it explains, for the button's name: "About Email". */
    label: string;
    /** The explanation, when it is one paragraph. */
    text?: string;
    /** The explanation, when it needs more than one paragraph or a link. */
    children?: Snippet;
    class?: string;
  } = $props();

  let open = $state(false);

  // Keyboard focus opens it, as a tooltip would; a mouse click is the trigger's own toggle, so a focus from a click does nothing.
  function focused(event: FocusEvent) {
    if ((event.currentTarget as HTMLElement).matches(':focus-visible')) open = true;
  }
</script>

<Popover.Root bind:open>
  <Popover.Trigger
    aria-label={label}
    openOnHover
    openDelay={150}
    onfocus={focused}
    onblur={() => (open = false)}
    class={cn(buttonVariants({ variant: 'ghost', size: 'icon' }), 'size-7 shrink-0 text-muted-foreground pointer-coarse:size-11', className)}
  >
    <InfoIcon aria-hidden="true" />
  </Popover.Trigger>
  <Popover.Content
    side="top"
    trapFocus={false}
    onOpenAutoFocus={(event) => event.preventDefault()}
    onCloseAutoFocus={(event) => event.preventDefault()}
    class="max-w-measure text-body"
    data-info
  >
    {#if text}<p>{text}</p>{/if}
    {@render children?.()}
  </Popover.Content>
</Popover.Root>
