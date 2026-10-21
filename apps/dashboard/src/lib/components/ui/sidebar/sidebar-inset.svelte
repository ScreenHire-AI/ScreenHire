<script lang="ts">
	import { cn, type WithElementRef } from "#lib/utils.js";
	import type { HTMLAttributes } from "svelte/elements";

	let {
		ref = $bindable(null),
		class: className,
		children,
		element = "main",
		...restProps
	}: WithElementRef<HTMLAttributes<HTMLElement>> & {
		/** `div` for a demo inside another page, which already has its one `main` landmark. */
		element?: "main" | "div";
	} = $props();
</script>

<svelte:element
	this={element}
	bind:this={ref}
	data-slot="sidebar-inset"
	class={cn("bg-background md:peer-data-[variant=inset]:m-2 md:peer-data-[variant=inset]:ml-0 md:peer-data-[variant=inset]:rounded-xl md:peer-data-[variant=inset]:shadow-sm md:peer-data-[variant=inset]:peer-data-[state=collapsed]:ml-2 relative flex w-full flex-1 flex-col", className)}
	{...restProps}
>
	{@render children?.()}
</svelte:element>
