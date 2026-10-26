<script lang="ts">
  import { Badge } from '#lib/components/ui/badge/index.js';

  // Marks a template or role ScreenHire supplies, after its name (design system W6, L1). It marks only the exception:
  // nothing marks the team's own.
  let { class: className }: { class?: string } = $props();
</script>

<Badge variant="muted" class={className}>Built in</Badge>
