<script lang="ts">
  import TagIcon from '@lucide/svelte/icons/tag';
  import { buttonVariants } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { watchTruncation } from '#lib/truncation.js';
  import { cn } from '#lib/utils.js';

  // Filters the candidate table by one tag, from the tags the listed applications carry. Like the verification filter, the
  // page applies it to the rows before the table sees them, and in a narrow panel header the trigger is its icon with
  // the tooltip saying what it filters (F5, A4). Without any tags there is nothing to filter by, so it is not shown (A3).
  let { tag = $bindable(), tags }: { tag: string | null; tags: readonly string[] } = $props();

  let labelCut = $state(false);
  const watchLabel = watchTruncation((cut) => (labelCut = cut));
  const summary = $derived(`Tag: ${tag ?? 'all'}`);
  // bits-ui radio groups take strings; a key no tag can have stands for no filter.
  const anyTag = '\u0000any';
</script>

{#if tags.length > 0 || tag !== null}
  <DropdownMenu.Root>
    <Tooltip.Root disabled={!labelCut}>
      <Tooltip.Trigger>
        {#snippet child({ props })}
          <DropdownMenu.Trigger
            {...props}
            class={cn(
              buttonVariants({ variant: 'outline' }),
              'max-w-56 min-w-0 shrink @max-3xl/panel:w-8 @max-3xl/panel:shrink-0 @max-3xl/panel:px-0 pointer-coarse:@max-3xl/panel:w-11',
              tag !== null && 'border-primary text-primary',
            )}
          >
            <TagIcon aria-hidden="true" />
            <span {@attach watchLabel} class="min-w-0 truncate @max-3xl/panel:sr-only">{summary}</span>
          </DropdownMenu.Trigger>
        {/snippet}
      </Tooltip.Trigger>
      <Tooltip.Content>{summary}</Tooltip.Content>
    </Tooltip.Root>
    <DropdownMenu.Content align="end" class="max-h-80 w-64">
      <DropdownMenu.Group>
        <DropdownMenu.GroupHeading class="text-caption text-muted-foreground">Tag</DropdownMenu.GroupHeading>
        <DropdownMenu.RadioGroup value={tag ?? anyTag} onValueChange={(value) => (tag = value === anyTag ? null : value)}>
          <DropdownMenu.RadioItem value={anyTag}>Any tag</DropdownMenu.RadioItem>
          {#each tags as option (option)}
            <DropdownMenu.RadioItem value={option}><span class="min-w-0 break-words">{option}</span></DropdownMenu.RadioItem>
          {/each}
        </DropdownMenu.RadioGroup>
      </DropdownMenu.Group>
    </DropdownMenu.Content>
  </DropdownMenu.Root>
{/if}
