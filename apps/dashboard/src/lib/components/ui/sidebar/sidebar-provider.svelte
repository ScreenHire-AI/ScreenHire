<script lang="ts">
	import * as Tooltip from "#lib/components/ui/tooltip/index.js";
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

	// The shell keeps the open state through onOpenChange (no cookie), and there is no Ctrl B toggle: section 15 of the
	// design system lists every shortcut.
	setSidebar({
		open: () => open,
		setOpen: (value: boolean) => {
			open = value;
			onOpenChange(value);
		},
	});
</script>

<Tooltip.Provider delayDuration={0}>
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
</Tooltip.Provider>
