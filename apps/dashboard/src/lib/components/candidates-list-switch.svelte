<script lang="ts">
  import UserRoundCheckIcon from '~icons/lucide/user-round-check';
  import UsersIcon from '~icons/lucide/users';
  import { toggleVariants } from '#lib/components/ui/toggle/index.js';
  import { cn } from '#lib/utils.js';

  // The Current / Saved switch beside the Candidates title (design system section 11): both lists live on one page, so
  // Saved candidates needs no sidebar item of its own. Two choices that cannot grow, so a toggle's look fits (S9). The
  // choice is the address (`?list=saved`; Current has none), so each choice is a link (A5, A6): a link and a reload keep
  // it, and it opens in a new tab. The current one takes `accent` and `aria-current`, as a pressed toggle does. Each list
  // has filters of its own, so a choice starts the other clean: its address carries nothing else. Not the bookmark: that
  // is the Saved status (I2), which the same page shows in its Stage column.
  let { list }: { list: 'current' | 'saved' } = $props();

  const choices = [
    { value: 'current', label: 'Current', href: '/candidates', icon: UsersIcon },
    { value: 'saved', label: 'Saved', href: '/candidates?list=saved', icon: UserRoundCheckIcon },
  ] as const;
</script>

<div role="group" aria-label="List" class="flex gap-1">
  {#each choices as choice (choice.value)}
    {@const Icon = choice.icon}
    <a
      href={choice.href}
      aria-current={list === choice.value ? 'page' : undefined}
      class={cn(toggleVariants({ size: 'lg' }), 'no-underline', list === choice.value && 'bg-accent text-accent-foreground hover:bg-accent')}
    >
      <Icon aria-hidden="true" />{choice.label}
    </a>
  {/each}
</div>
