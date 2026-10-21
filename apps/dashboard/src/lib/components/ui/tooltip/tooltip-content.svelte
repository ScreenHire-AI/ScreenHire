<script lang="ts">
	import { Tooltip as TooltipPrimitive } from "bits-ui";
	import { cn } from "#lib/utils.js";
	import type { WithoutChildrenOrChild } from "#lib/utils.js";
	import TooltipPortal from "./tooltip-portal.svelte";
	import type { ComponentProps } from "svelte";

	// An overlay surface like the menus (design system section 6), so it carries key caps and long values in body text.
	let {
		ref = $bindable(null),
		class: className,
		sideOffset = 4,
		side = "top",
		children,
		portalProps,
		...restProps
	}: TooltipPrimitive.ContentProps & {
		portalProps?: WithoutChildrenOrChild<ComponentProps<typeof TooltipPortal>>;
	} = $props();
</script>

<TooltipPortal {...portalProps}>
	<TooltipPrimitive.Content
		bind:ref
		data-slot="tooltip-content"
		{sideOffset}
		{side}
		class={cn(
			"data-open:animate-in data-open:fade-in-0 data-[state=delayed-open]:animate-in data-[state=delayed-open]:fade-in-0 data-closed:animate-out data-closed:fade-out-0 motion-reduce:animate-none duration-150 inline-flex items-center gap-1.5 rounded-md border border-border bg-popover px-2.5 py-1.5 text-body text-popover-foreground shadow-md z-50 w-fit max-w-xs break-words origin-(--bits-tooltip-content-transform-origin)",
			className
		)}
		{...restProps}
	>
		{@render children?.()}
	</TooltipPrimitive.Content>
</TooltipPortal>
