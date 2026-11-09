<script lang="ts">
  import { cn } from '#lib/utils.js';

  let { class: className }: { class?: string } = $props();
</script>

<!-- Microsoft's four squares, in Microsoft's own colours: its sign-in branding guidelines require the logo unchanged, so
     this is one of the two exceptions to Lucide-only icons and tokens-only colour (design system section 14).
     Decorative: the button says "Continue with Microsoft". -->
<svg data-slot="microsoft-mark" aria-hidden="true" focusable="false" viewBox="0 0 21 21" class={cn('size-4 shrink-0', className)}>
  <rect x="1" y="1" width="9" height="9" fill="#F25022" />
  <rect x="11" y="1" width="9" height="9" fill="#7FBA00" />
  <rect x="1" y="11" width="9" height="9" fill="#00A4EF" />
  <rect x="11" y="11" width="9" height="9" fill="#FFB900" />
</svg>
