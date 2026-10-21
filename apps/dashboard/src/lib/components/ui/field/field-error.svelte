<script lang="ts">
	// Our change: an error is a problem to fix, so it carries the verify mark and colour, as the workflow builder's
	// FieldErrors does (design system T1, section 11 "Form field").
	import StatusMark from "#lib/components/status-mark.svelte";
	import { cn, type WithElementRef } from "#lib/utils.js";
	import type { Snippet } from "svelte";
	import type { HTMLAttributes } from "svelte/elements";

	let {
		ref = $bindable(null),
		class: className,
		children,
		errors,
		...restProps
	}: WithElementRef<HTMLAttributes<HTMLDivElement>> & {
		children?: Snippet;
		errors?: { message?: string }[];
	} = $props();

	const hasContent = $derived.by(() => {
		// has slotted error
		if (children) return true;

		// no errors
		if (!errors || errors.length === 0) return false;

		// has an error but no message
		if (errors.length === 1 && !errors[0]?.message) {
			return false;
		}

		return true;
	});

	const isMultipleErrors = $derived(errors && errors.length > 1);
	const singleErrorMessage = $derived(errors && errors.length === 1 && errors[0]?.message);
</script>

{#if hasContent}
	<div
		bind:this={ref}
		role="alert"
		data-slot="field-error"
		class={cn("text-status-verify text-body font-semibold", className)}
		{...restProps}
	>
		{#if children}
			{@render children()}
		{:else if singleErrorMessage}
			<span class="flex items-baseline gap-1.5"><StatusMark shape="square" tone="verify" class="size-1.5 shrink-0" />{singleErrorMessage}</span>
		{:else if isMultipleErrors}
			<ul class="flex flex-col gap-0.5">
				{#each errors ?? [] as error, index (index)}
					{#if error?.message}
						<li class="flex items-baseline gap-1.5"><StatusMark shape="square" tone="verify" class="size-1.5 shrink-0" />{error.message}</li>
					{/if}
				{/each}
			</ul>
		{/if}
	</div>
{/if}
