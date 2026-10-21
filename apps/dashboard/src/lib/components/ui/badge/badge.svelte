<script lang="ts" module>
	import type { VariantProps } from "tailwind-variants";
	import { tv } from "#lib/utils.js";

	// Badges, pills and counts are never clickable (design system A5), so a badge is always a span.
	export const badgeVariants = tv({
		base: "h-5 gap-1 rounded-full border border-transparent px-2 py-0.5 text-caption font-medium has-data-[icon=inline-end]:pr-1.5 has-data-[icon=inline-start]:pl-1.5 [&>svg]:size-3! group/badge inline-flex w-fit shrink-0 items-center justify-center overflow-hidden whitespace-nowrap [&>svg]:pointer-events-none",
		variants: {
			variant: {
				default: "bg-primary text-primary-foreground",
				secondary: "bg-secondary text-secondary-foreground",
				destructive: "bg-destructive/10 text-destructive dark:bg-destructive/20",
				outline: "border-border text-foreground",
				ghost: "text-muted-foreground",
				// ScreenHire status semantics (design system section 3.2): always pair with a shape mark and a text label. Badges do
				// not transition: a chip changes when its data does (a live level, a theme switch), and should be read at once.
				clear: "bg-status-clear-soft font-bold text-status-clear",
				review: "bg-status-review-soft font-bold text-status-review",
				verify: "bg-status-verify-soft font-bold text-status-verify",
				ai: "bg-status-ai-soft font-bold text-status-ai",
				// A calm status in a list or table: its mark and muted text, no fill (T3).
				quiet: "px-0 text-muted-foreground",
				// A fact pill (section 11).
				muted: "border-input bg-secondary text-secondary-foreground",
				// A count after its label (T4): a wide pill, never a square.
				count: "min-w-7 bg-secondary text-muted-foreground tabular-nums",
			},
		},
		defaultVariants: {
			variant: "default",
		},
	});

	export type BadgeVariant = VariantProps<typeof badgeVariants>["variant"];
</script>

<script lang="ts">
	import { cn, type WithElementRef } from "#lib/utils.js";
	import type { HTMLAttributes } from "svelte/elements";

	let {
		ref = $bindable(null),
		class: className,
		variant = "default",
		children,
		...restProps
	}: WithElementRef<HTMLAttributes<HTMLSpanElement>> & {
		variant?: BadgeVariant;
	} = $props();
</script>

<span bind:this={ref} data-slot="badge" class={cn(badgeVariants({ variant }), className)} {...restProps}>
	{@render children?.()}
</span>
