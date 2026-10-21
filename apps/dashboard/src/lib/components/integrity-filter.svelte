<script lang="ts">
  import type { AiAssistanceLevel, AuthenticityLevel } from '@screenhire/api-client';
  import ListFilterIcon from '@lucide/svelte/icons/list-filter';
  import StatusMark from '#lib/components/status-mark.svelte';
  import { buttonVariants } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import {
    aiAssistanceLevels,
    aiAssistanceOrder,
    authenticityLevels,
    authenticityOrder,
    pendingLevel,
    type IntegrityFilter,
  } from '#lib/integrity.js';
  import { cn } from '#lib/utils.js';

  // Filters the candidate table by either assessment's level. The page applies it to the rows before the table sees
  // them, as it does the stage filter, so counts and "select all" match what is shown. In a narrow panel header the
  // trigger is its icon, and the tooltip says what it filters (F5, A4).
  let { filter = $bindable() }: { filter: IntegrityFilter } = $props();

  const authenticityOptions = [
    ...authenticityOrder.map((id) => ({ id, view: authenticityLevels[id] })),
    { id: 'Pending' as const, view: pendingLevel },
  ];
  const aiOptions = aiAssistanceOrder.map((id) => ({ id, view: aiAssistanceLevels[id] }));

  const active = $derived(
    [
      filter.authenticity && (filter.authenticity === 'Pending' ? pendingLevel : authenticityLevels[filter.authenticity]).label,
      filter.aiAssistance && `AI ${aiAssistanceLevels[filter.aiAssistance].label.toLowerCase()}`,
    ].filter(Boolean),
  );

  // bits-ui radio groups take strings; "all" stands for no filter.
  // The label is visually hidden in a narrow panel header; only then does the tooltip say it.
  let labelWidth = $state(0);
  const summary = $derived(`Integrity: ${active.length > 0 ? active.join(', ') : 'all'}`);

  const authenticityValue = $derived(filter.authenticity ?? 'all');
  const aiValue = $derived(filter.aiAssistance ?? 'all');
</script>

<DropdownMenu.Root>
  <Tooltip.Root disabled={labelWidth > 1}>
    <Tooltip.Trigger>
      {#snippet child({ props })}
        <DropdownMenu.Trigger
          {...props}
          class={cn(buttonVariants({ variant: 'outline' }), 'min-w-0 @max-3xl/panel:w-8 @max-3xl/panel:px-0 pointer-coarse:@max-3xl/panel:w-11', active.length > 0 && 'border-primary text-primary')}
        >
          <ListFilterIcon aria-hidden="true" />
          <span bind:clientWidth={labelWidth} class="max-w-56 truncate @max-3xl/panel:sr-only">{summary}</span>
        </DropdownMenu.Trigger>
      {/snippet}
    </Tooltip.Trigger>
    <Tooltip.Content>{summary}</Tooltip.Content>
  </Tooltip.Root>
  <DropdownMenu.Content align="end" class="w-64">
    <DropdownMenu.Group>
      <DropdownMenu.GroupHeading class="text-caption text-muted-foreground">Authenticity</DropdownMenu.GroupHeading>
      <DropdownMenu.RadioGroup
        value={authenticityValue}
        onValueChange={(value) => (filter = { ...filter, authenticity: value === 'all' ? null : (value as AuthenticityLevel | 'Pending') })}
      >
        <DropdownMenu.RadioItem value="all">Any level</DropdownMenu.RadioItem>
        {#each authenticityOptions as option (option.id)}
          <DropdownMenu.RadioItem value={option.id}>
            <StatusMark shape={option.view.shape} tone={option.view.tone} />
            {option.view.label}
          </DropdownMenu.RadioItem>
        {/each}
      </DropdownMenu.RadioGroup>
    </DropdownMenu.Group>
    <DropdownMenu.Separator />
    <DropdownMenu.Group>
      <DropdownMenu.GroupHeading class="text-caption text-muted-foreground">AI assistance</DropdownMenu.GroupHeading>
      <DropdownMenu.RadioGroup
        value={aiValue}
        onValueChange={(value) => (filter = { ...filter, aiAssistance: value === 'all' ? null : (value as AiAssistanceLevel) })}
      >
        <DropdownMenu.RadioItem value="all">Any level</DropdownMenu.RadioItem>
        {#each aiOptions as option (option.id)}
          <DropdownMenu.RadioItem value={option.id}>
            <StatusMark shape={option.view.shape} tone={option.view.tone} />
            {option.view.label}
          </DropdownMenu.RadioItem>
        {/each}
      </DropdownMenu.RadioGroup>
    </DropdownMenu.Group>
  </DropdownMenu.Content>
</DropdownMenu.Root>
