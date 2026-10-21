<script lang="ts">
	import { cn, type WithElementRef } from "#lib/utils.js";
	import { SIDEBAR_WIDTH, SIDEBAR_WIDTH_ICON } from "./constants.js";
	import { setSidebar } from "./context.svelte.js";
	import type { HTMLAttributes } from "svelte/elements";

	let {
		ref = $bindable(null),
		open = $bindable(true),
		onOpenChange = () => {},
		class: className,
		style,
		children,
		...restProps
	}: WithElementRef<HTMLAttributes<HTMLDivElement>> & {
		open?: boolean;
		onOpenChange?: (open: boolean) => void;
	} = $props();

	// The (app) layout drives the open state from the window width (full at lg, the rail below it), so there is no cookie
	// and no Ctrl B toggle: section 15 of the design system lists every shortcut.
	// No tooltip provider here either. The root layout's serves the whole app, and the rail's labels set their own delay
	// (sidebar-menu-button.svelte).
	setSidebar({
		open: () => open,
		setOpen: (value: boolean) => {
			open = value;
			onOpenChange(value);
		},
	});
</script>

<div
	data-slot="sidebar-wrapper"
	style="--sidebar-width: {SIDEBAR_WIDTH}; --sidebar-width-icon: {SIDEBAR_WIDTH_ICON}; {style}"
	class={cn(
		"group/sidebar-wrapper flex min-h-svh w-full has-data-[variant=inset]:bg-sidebar",
		className
	)}
	bind:this={ref}
	{...restProps}
>
	{@render children?.()}
</div>
