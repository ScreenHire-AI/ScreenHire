<script lang="ts">
	import { cn, type WithElementRef } from "#lib/utils.js";
	import type { HTMLTableAttributes } from "svelte/elements";

	let {
		ref = $bindable(null),
		class: className,
		children,
		label,
		...restProps
	}: WithElementRef<HTMLTableAttributes> & {
		/**
		 * Names a table that scrolls sideways and holds nothing to focus (no sort button, no link), so the keyboard can reach and
		 * scroll it (WCAG 2.1.1). A table with controls inside needs none.
		 */
		label?: string;
	} = $props();
</script>

{#if label}
	<!-- svelte-ignore a11y_no_noninteractive_tabindex: a table that scrolls sideways takes focus to be scrolled with the keyboard -->
	<div data-slot="table-container" role="region" aria-label={label} tabindex="0" class="relative w-full overflow-x-auto focus-visible:-outline-offset-2">
		<table bind:this={ref} data-slot="table" class={cn("w-full caption-bottom text-body", className)} {...restProps}>
			{@render children?.()}
		</table>
	</div>
{:else}
	<div data-slot="table-container" class="relative w-full overflow-x-auto">
		<table bind:this={ref} data-slot="table" class={cn("w-full caption-bottom text-body", className)} {...restProps}>
			{@render children?.()}
		</table>
	</div>
{/if}
