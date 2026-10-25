<script lang="ts">
  import type { TeamMember } from '@screenhire/api-client';
  import Hint from '#lib/components/hint.svelte';
  import { clock } from '#lib/clock.svelte.js';
  import { formatDateTime, timeAgo } from '#lib/dates.js';
  import { mentionName, noteSegments } from '#lib/mentions.js';
  import { cn } from '#lib/utils.js';

  // A note as the team reads it (design system section 11, Mention): its latest text, each mention as the member's
  // current name, and "Edited" after it, with when in a tooltip, for a note that changed.
  let {
    text,
    members,
    editedAt,
    class: className,
  }: { text: string; members: readonly TeamMember[] | undefined; editedAt?: string | null; class?: string } = $props();
</script>

<p class={cn('max-w-measure break-words whitespace-pre-line', className)}>
  {#each noteSegments(text) as segment, index (index)}{#if segment.kind === 'mention'}<span class="font-semibold text-primary">@{mentionName(segment.userId, members)}</span>{:else}{segment.text}{/if}{/each}{#if editedAt}{' '}<Hint content={`Edited ${timeAgo(editedAt, clock.now)}, ${formatDateTime(editedAt)}`}>
      {#snippet children({ props })}<span {...props} class="text-caption text-muted-foreground">Edited</span>{/snippet}
    </Hint>{/if}
</p>
