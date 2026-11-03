<script lang="ts">
	import MenuIcon from '~icons/lucide/menu';
	import PanelLeftIcon from '~icons/lucide/panel-left';
	import { Button } from "#lib/components/ui/button/index.js";
	import * as Tooltip from "#lib/components/ui/tooltip/index.js";
	import { cn } from "#lib/utils.js";
	import { useSidebar } from "./context.svelte.js";
	import type { ComponentProps } from "svelte";

	let {
		ref = $bindable(null),
		class: className,
		onclick,
		...restProps
	}: ComponentProps<typeof Button> & {
		onclick?: (e: MouseEvent) => void;
	} = $props();

	const sidebar = useSidebar();
	// An icon button names its action and shows the name in a tooltip (A4). On a phone it is the menu button that opens
	// the navigation sheet (design system section 8).
	const label = $derived(sidebar.isMobile ? "Open navigation" : sidebar.open ? "Collapse sidebar" : "Expand sidebar");
</script>

<Tooltip.Root>
	<Tooltip.Trigger>
		{#snippet child({ props })}
			<Button
				bind:ref
				{...props}
				data-sidebar="trigger"
				data-slot="sidebar-trigger"
				variant="ghost"
				size="icon"
				class={cn("pointer-coarse:size-11", className)}
				type="button"
				aria-label={label}
				onclick={(e) => {
					onclick?.(e);
					sidebar.toggle();
				}}
				{...restProps}
			>
				{#if sidebar.isMobile}<MenuIcon />{:else}<PanelLeftIcon class="cn-rtl-flip" />{/if}
			</Button>
		{/snippet}
	</Tooltip.Trigger>
	<Tooltip.Content side="right">{label}</Tooltip.Content>
</Tooltip.Root>
