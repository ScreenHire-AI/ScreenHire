<script lang="ts">
	import { cn, type WithElementRef } from "#lib/utils.js";
	import type { HTMLAttributes } from "svelte/elements";

	let {
		ref = $bindable(null),
		class: className,
		children,
		...restProps
	}: WithElementRef<HTMLAttributes<HTMLDivElement>> = $props();
</script>

<!-- Our change: a row with its buttons at the right at their own width, never a full-width primary; a "..." menu takes
     `mr-auto` to stand at the left (design system A19). -->
<div
	bind:this={ref}
	data-slot="sheet-footer"
	class={cn("gap-2 p-4 mt-auto flex flex-row items-center justify-end", className)}
	{...restProps}
>
	{@render children?.()}
</div>
