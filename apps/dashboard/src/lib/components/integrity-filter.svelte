<script lang="ts">
  import type { AiAssistanceLevel, AuthenticityLevel } from '@screenhire/api-client';
  import ListFilterIcon from '@lucide/svelte/icons/list-filter';
  import StatusMark from '#lib/components/status-mark.svelte';
  import { buttonVariants } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
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
  // them, as it does the stage filter, so counts and "select all" match what is shown.
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
  const authenticityValue = $derived(filter.authenticity ?? 'all');
  const aiValue = $derived(filter.aiAssistance ?? 'all');
</script>

<DropdownMenu.Root>
  <DropdownMenu.Trigger class={cn(buttonVariants({ variant: 'outline', size: 'sm' }), active.length > 0 && 'border-primary text-primary')}>
    <ListFilterIcon aria-hidden="true" />
    Integrity: {active.length > 0 ? active.join(', ') : 'all'}
  </DropdownMenu.Trigger>
  <DropdownMenu.Content align="end" class="w-64">
    <DropdownMenu.Group>
      <DropdownMenu.GroupHeading class="font-mono text-[10.5px] tracking-[0.06em] text-muted-foreground uppercase">Authenticity</DropdownMenu.GroupHeading>
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
      <DropdownMenu.GroupHeading class="font-mono text-[10.5px] tracking-[0.06em] text-muted-foreground uppercase">AI assistance</DropdownMenu.GroupHeading>
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
