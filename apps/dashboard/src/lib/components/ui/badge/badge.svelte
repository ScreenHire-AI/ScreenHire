<script lang="ts" module>
	import type { VariantProps } from "tailwind-variants";
	import { tv } from "#lib/utils.js";

	// Badges, pills and counts are never clickable (design system A5), so a badge is always a span.
	export const badgeVariants = tv({
		base: "h-5 gap-1 rounded-full border border-transparent px-2 py-0.5 text-caption font-medium has-data-[icon=inline-end]:pr-1.5 has-data-[icon=inline-start]:pl-1.5 [&>svg]:size-3.5 group/badge inline-flex w-fit shrink-0 items-center justify-center overflow-hidden whitespace-nowrap [&>svg]:pointer-events-none",
		variants: {
			variant: {
				default: "bg-primary text-primary-foreground",
				secondary: "bg-secondary text-secondary-foreground",
				destructive: "bg-destructive/10 text-destructive dark:bg-destructive/20",
				outline: "border-border text-foreground",
				ghost: "text-muted-foreground",
				// ScreenHire status semantics (design system section 3.2): always pair with the status's icon and a text label
				// (StatusBadge). Badges do not transition: a chip changes when its data does (a live level, a theme switch), and
				// should be read at once.
				clear: "bg-status-clear-soft font-semibold text-status-clear",
				review: "bg-status-review-soft font-semibold text-status-review",
				verify: "bg-status-verify-soft font-semibold text-status-verify",
				ai: "bg-status-ai-soft font-semibold text-status-ai",
				// Grey: waiting, at work, not started or ended (Sent, Scanning, a draft, a closed job), on a muted fill (section 3.2).
				neutral: "bg-secondary font-semibold text-muted-foreground",
				// A calm status in a list or table: its icon and muted text, no fill (T3).
				quiet: "px-0 text-muted-foreground",
				// A fact pill (section 11).
				muted: "border-input bg-secondary text-muted-foreground",
				// A count after its label (T4): a wide pill, never a square. On a pressed toggle it turns white, so it still reads as
				// a pill on the accent fill.
				count: "min-w-7 bg-secondary font-semibold text-muted-foreground tabular-nums group-data-[state=on]/toggle:bg-card",
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
