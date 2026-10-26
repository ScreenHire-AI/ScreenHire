<script lang="ts" generics="K extends string">
  import type { Snippet } from 'svelte';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import type { AnswerKindGroup } from '#lib/form-builder.js';

  // The answer menu (design system section 11): picks a question's answer kind, each group under its heading and each
  // kind with its icon. A question's kind button opens it with the current kind ticked; Add a question opens the same
  // menu with nothing ticked. `trigger` draws the button from the trigger's props.
  let {
    groups,
    value,
    onselect,
    trigger,
  }: {
    groups: readonly AnswerKindGroup<K>[];
    /** The question's current kind, ticked; none for Add a question. */
    value?: K;
    onselect: (kind: K) => void;
    trigger: Snippet<[props: Record<string, unknown>]>;
  } = $props();
</script>

<DropdownMenu.Root>
  <DropdownMenu.Trigger>
    {#snippet child({ props })}{@render trigger(props)}{/snippet}
  </DropdownMenu.Trigger>
  <DropdownMenu.Content align="end" class="min-w-56">
    {#each groups as group, index (group.label)}
      {#if index > 0}<DropdownMenu.Separator />{/if}
      {#if value === undefined}
        <DropdownMenu.Group>
          <DropdownMenu.GroupHeading>{group.label}</DropdownMenu.GroupHeading>
          {#each group.kinds as kind (kind.value)}
            {@const Icon = kind.icon}
            <DropdownMenu.Item onSelect={() => onselect(kind.value)}><Icon aria-hidden="true" class="text-muted-foreground" />{kind.label}</DropdownMenu.Item>
          {/each}
        </DropdownMenu.Group>
      {:else}
        <DropdownMenu.RadioGroup aria-label={group.label} {value} onValueChange={(next) => next && next !== value && onselect(next as K)}>
          <DropdownMenu.GroupHeading>{group.label}</DropdownMenu.GroupHeading>
          {#each group.kinds as kind (kind.value)}
            {@const Icon = kind.icon}
            <DropdownMenu.RadioItem value={kind.value} closeOnSelect><Icon aria-hidden="true" class="text-muted-foreground" />{kind.label}</DropdownMenu.RadioItem>
          {/each}
        </DropdownMenu.RadioGroup>
      {/if}
    {/each}
  </DropdownMenu.Content>
</DropdownMenu.Root>
