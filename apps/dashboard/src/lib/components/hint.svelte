<script lang="ts">
  import { mergeProps } from 'bits-ui';
  import type { Snippet } from 'svelte';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';

  // A tooltip on text that is not a control: a time's exact value, a truncated name's full one. It opens on hover and
  // keyboard focus like any tooltip, and on tap too, because a touch screen has no hover (design system F4). A click only
  // opens it, so a mouse click after hovering does not close it; leaving or blurring the text does.
  let {
    content,
    disabled = false,
    children,
  }: {
    content: string;
    /** No tooltip, and no tab stop: the text already shows everything. */
    disabled?: boolean;
    /** The text element, as `{#snippet children({ props })}`. Spread `props` onto it. */
    children: Snippet<[{ props: Record<string, unknown> }]>;
  } = $props();

  let open = $state(false);

  // The trigger props are a button's; text is not one.
  function textProps(props: Record<string, unknown>) {
    const { type, ...rest } = props;
    void type;
    return mergeProps(rest, { tabindex: disabled ? undefined : 0, onclick: () => (open = true) });
  }
</script>

<Tooltip.Root bind:open {disabled} disableCloseOnTriggerClick>
  <Tooltip.Trigger>
    {#snippet child({ props })}
      {@render children({ props: textProps(props) })}
    {/snippet}
  </Tooltip.Trigger>
  <Tooltip.Content>{content}</Tooltip.Content>
</Tooltip.Root>
