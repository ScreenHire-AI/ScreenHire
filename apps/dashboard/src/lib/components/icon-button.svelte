<script lang="ts">
  import { mergeProps } from 'bits-ui';
  import type { Snippet } from 'svelte';
  import { Button, type ButtonProps } from '#lib/components/ui/button/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';

  // A quiet icon-only button (A4): `label` is both its accessible name and its tooltip. `children` is the icon.
  let { label, children, ...restProps }: Omit<ButtonProps, 'aria-label' | 'children'> & { label: string; children: Snippet } = $props();
</script>

<Tooltip.Root>
  <Tooltip.Trigger>
    {#snippet child({ props })}
      <Button variant="ghost" size="icon" {...mergeProps(props, restProps)} aria-label={label}>{@render children()}</Button>
    {/snippet}
  </Tooltip.Trigger>
  <Tooltip.Content>{label}</Tooltip.Content>
</Tooltip.Root>
