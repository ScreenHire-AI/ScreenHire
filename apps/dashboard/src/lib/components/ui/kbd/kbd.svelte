<script lang="ts">
	import { cn, type WithElementRef } from "#lib/utils.js";
	import type { HTMLAttributes } from "svelte/elements";

	let {
		ref = $bindable(null),
		class: className,
		children,
		...restProps
	}: WithElementRef<HTMLAttributes<HTMLElement>> = $props();
</script>

<kbd
	bind:this={ref}
	data-slot="kbd"
	class={cn(
		"bg-secondary text-muted-foreground border border-input h-5 w-fit min-w-5 gap-1 rounded-sm px-1 font-mono text-code [&_svg:not([class*='size-'])]:size-3 pointer-events-none inline-flex items-center justify-center select-none",
		className
	)}
	{...restProps}
>
	{@render children?.()}
</kbd>
