<script lang="ts">
  import ListIcon from '~icons/lucide/list';
  import UserRoundCheckIcon from '~icons/lucide/user-round-check';
  import UsersIcon from '~icons/lucide/users';
  import type { CandidateList, ListCounts } from '#lib/candidate-lists.js';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { toggleVariants } from '#lib/components/ui/toggle/index.js';
  import { cn } from '#lib/utils.js';

  // The Current / Saved / All switch beside the Candidates title (design system section 11): the lists live on one page,
  // so Saved candidates needs no sidebar item of its own. Choices that cannot grow, so a toggle's look fits (S9). The
  // choice is the address (`?list=saved`, `?list=all`; Current has none), so each choice is a link (A5, A6): a link and a
  // reload keep it, and it opens in a new tab. The current one takes `accent` and `aria-current`, as a pressed toggle does.
  // Each list has filters of its own, so a choice starts the other clean: its address carries nothing else. Not the
  // bookmark: that is the Saved status (I2), which the same page shows in its Stage column.
  // Each choice carries its own count, so the header band has none: a count beside the title changed width between the
  // lists and slid this switch (S8). Until a count is in, an invisible pill holds its place. Each list passes the counts
  // from the application and saved-people queries (`listCounts`), which the other list reads too.
  let { list, counts = {} }: { list: CandidateList; counts?: ListCounts } = $props();

  const choices = [
    { value: 'current', label: 'Current', href: '/candidates', icon: UsersIcon },
    { value: 'saved', label: 'Saved', href: '/candidates?list=saved', icon: UserRoundCheckIcon },
    { value: 'all', label: 'All', href: '/candidates?list=all', icon: ListIcon },
  ] as const;
</script>

<div role="group" aria-label="List" class="flex flex-wrap gap-1">
  {#each choices as choice (choice.value)}
    {@const Icon = choice.icon}
    {@const count = counts[choice.value]}
    <a
      href={choice.href}
      aria-current={list === choice.value ? 'page' : undefined}
      class={cn(toggleVariants({ size: 'lg' }), 'no-underline', list === choice.value && 'bg-accent text-accent-foreground hover:bg-accent')}
    >
      <Icon aria-hidden="true" />{choice.label}
      {#if count !== undefined}
        <Badge variant="count">{count}</Badge>
      {:else}
        <Badge variant="count" aria-hidden="true" class="invisible">0</Badge>
      {/if}
    </a>
  {/each}
</div>
