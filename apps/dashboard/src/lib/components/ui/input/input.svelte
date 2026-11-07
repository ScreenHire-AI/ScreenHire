<script lang="ts">
	import { cn, type WithElementRef } from "#lib/utils.js";
	import type { HTMLInputAttributes, HTMLInputTypeAttribute } from "svelte/elements";

	// A file is never chosen with a bare input: every file input is the drop area (@screenhire/ui/questions, design
	// system A12), so this component takes no file type.
	type InputType = Exclude<HTMLInputTypeAttribute, "file">;

	type Props = WithElementRef<Omit<HTMLInputAttributes, "type"> & { type?: InputType }>;

	let {
		ref = $bindable(null),
		value = $bindable(),
		type,
		class: className,
		"data-slot": dataSlot = "input",
		...restProps
	}: Props = $props();
</script>

<input
	bind:this={ref}
	data-slot={dataSlot}
	class={cn(
		"dark:bg-input/30 border-input focus-visible:border-ring  aria-invalid:ring-destructive/20 dark:aria-invalid:ring-destructive/40 aria-invalid:border-destructive dark:aria-invalid:border-destructive/50 disabled:bg-input/50 dark:disabled:bg-input/80 h-9 pointer-coarse:h-11 rounded-lg border bg-transparent px-2.5 py-1 text-body max-md:text-reading pointer-coarse:text-reading transition-colors file:h-6 file:text-body file:font-medium  aria-invalid:ring-3 w-full min-w-0  file:inline-flex file:border-0 file:bg-transparent file:text-foreground placeholder:text-muted-foreground disabled:opacity-50",
		className
	)}
	{type}
	bind:value
	{...restProps}
/>
