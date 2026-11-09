<script lang="ts">
  import type { HiringTemplateSummary } from '@screenhire/api-client';
  import { createGetHiringTemplateVersion } from '@screenhire/api-client/svelte-query';
  import { isTerminal } from '#lib/workflow.js';

  // One starting point on the new template screen (design system section 11, Start from card): the template's name, one
  // example line, and its stages as plain text. The card is a radio, so the choice is one control the keyboard reaches. No
  // icon tile, no arrows between the stages, no third caption.
  let {
    template,
    chosen,
    onchoose,
  }: {
    template: HiringTemplateSummary | undefined;
    chosen: boolean;
    onchoose: () => void;
  } = $props();

  const version = createGetHiringTemplateVersion(
    () => template?.id ?? '',
    () => 1,
    () => ({ query: { enabled: Boolean(template) } }),
  );
  const stages = $derived(
    version.data?.data.definition.workflow.stages.filter((stage) => !isTerminal(stage.kind)).map((stage) => stage.name) ?? [],
  );
</script>

<label
  class={[
    'flex min-w-0 flex-col gap-1.5 rounded-panel border bg-card p-4 shadow-surface has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-ring',
    chosen ? 'border-primary inset-ring-1 inset-ring-primary' : 'border-border hover:bg-muted/40',
  ]}
>
  <input type="radio" name="starting-point" class="sr-only" checked={chosen} onchange={onchoose} />
  <span class="font-semibold break-words">{template?.name ?? ''}</span>
  <span class="break-words text-muted-foreground">{template?.description ?? ''}</span>
  <span class="break-words">{stages.join(', ')}</span>
</label>
