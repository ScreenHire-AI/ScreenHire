<script lang="ts">
  import AtSignIcon from '~icons/lucide/at-sign';
  import FileTextIcon from '~icons/lucide/file-text';
  import GlobeIcon from '~icons/lucide/globe';
  import KeyboardIcon from '~icons/lucide/keyboard';
  import MessageSquareIcon from '~icons/lucide/message-square';
  import UsersIcon from '~icons/lucide/users';
  import type { CheckGroup, CheckGroupView } from '@screenhire/api-client';
  import Hint from '#lib/components/hint.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import { groupAnchor, groupStateText, groupStatusOf, type Finding } from '#lib/verification.js';

  // The six groups of checks as a map (design system section 16): one tile per group with its state in icon, colour and
  // words, and, for a group with findings, a link to its panel. It says what ScreenHire looked at, never what it means about
  // the person. A group's icon sits on its tile only; "Nothing found" is a state here and a line nowhere else. The findings
  // tell an information-only group of AI help (the sparkles mark) from one of context notes (grey).
  let { groups, findings }: { groups: readonly CheckGroupView[]; findings: Record<CheckGroup, readonly Finding[]> } = $props();

  const icons: Record<CheckGroup, typeof GlobeIcon> = {
    Network: GlobeIcon,
    Contact: AtSignIcon,
    Documents: FileTextIcon,
    Answers: MessageSquareIcon,
    Duplicates: UsersIcon,
    FormUse: KeyboardIcon,
  };

  // A tile links to its panel and moves focus there, so a keyboard reader lands on what it found.
  function goTo(event: MouseEvent, group: CheckGroup) {
    const panel = document.getElementById(groupAnchor(group));
    if (!panel) return;
    event.preventDefault();
    panel.scrollIntoView({ block: 'start' });
    panel.focus({ preventScroll: true });
  }
</script>

<!-- 22rem holds the longest name beside the longest state ("Documents", "1 note, information only") on one line (F5): three
     tiles a row on a desktop, one below that. -->
<ul aria-label="Check groups" data-group-map class="grid grid-cols-[repeat(auto-fill,minmax(min(22rem,100%),1fr))] gap-2">
  {#each groups as group (group.group)}
    {@const Icon = icons[group.group]}
    {@const links = group.found + group.notes > 0}
    <li data-group={group.group} data-state={group.state} class="rounded-md border border-border-subtle">
      {#snippet tile()}
        <span class="flex min-w-0 items-center gap-2.5">
          <span aria-hidden="true" class="grid size-7 shrink-0 place-items-center rounded-full bg-secondary text-muted-foreground"><Icon class="size-4" /></span>
          <!-- A tile that links underlines its name on hover and focus, as navigation does (A5). -->
          <span class="min-w-0 font-semibold group-hover/tile:underline group-focus-visible/tile:underline">{group.label}</span>
        </span>
        {#snippet badge()}
          <StatusBadge status={groupStatusOf(group, findings[group.group] ?? [])} label={groupStateText(group)} quiet />
        {/snippet}
        {#if group.note && !links}
          <!-- What the organization's settings changed here, one hover or focus away; the state's words already say it. A tile that
               links has its note in its panel instead, so a link never holds a second tab stop. -->
          <Hint content={group.note}>
            {#snippet children({ props })}<span {...props} class="inline-flex rounded-sm">{@render badge()}</span>{/snippet}
          </Hint>
        {:else}
          {@render badge()}
        {/if}
      {/snippet}
      {#if links}
        <a
          href={`#${groupAnchor(group.group)}`}
          onclick={(event) => goTo(event, group.group)}
          class="group/tile flex min-h-11 flex-wrap items-center justify-between gap-x-3 gap-y-1 rounded-md px-3 py-2 hover:bg-secondary/60"
        >
          {@render tile()}
        </a>
      {:else}
        <div class="flex min-h-11 flex-wrap items-center justify-between gap-x-3 gap-y-1 px-3 py-2">{@render tile()}</div>
      {/if}
    </li>
  {/each}
</ul>
