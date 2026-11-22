<script lang="ts" generics="T = never">
	import { Tooltip as TooltipPrimitive } from "bits-ui";
	import { isCodeFocus, listenForFocusIntent } from "./focus-intent.js";

	let { open = $bindable(false), ...restProps }: TooltipPrimitive.RootProps<T> = $props();

	// Our change: a tooltip opens on hover and on keyboard focus, never when code moves the focus to its trigger
	// (focus-intent.ts). bits-ui opens it inside the trigger's focus handler, so the open it asks for then is refused.
	listenForFocusIntent();
</script>

<TooltipPrimitive.Root bind:open={() => open, (next) => { if (next && !open && isCodeFocus()) return; open = next; }} {...restProps} />
