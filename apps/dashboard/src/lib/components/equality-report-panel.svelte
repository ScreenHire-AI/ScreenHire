<script lang="ts">
  import { createGetEqualityReport, createListFormTemplates } from '@screenhire/api-client/svelte-query';
  import { page } from '$app/state';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import * as Table from '#lib/components/ui/table/index.js';
  import { can } from '#lib/permissions.js';
  import { describeError } from '#lib/query.js';

  // Equality monitoring totals (docs/architecture/reports.md, Equality monitoring): one panel per equality monitoring form,
  // a table per question, a count per answer. Nothing names a candidate, and any count under 5 reads "Fewer than 5", as
  // does the next smallest when it alone would give a hidden one away. Shown only where this deployment offers equality
  // monitoring forms, to someone who may edit the organization's settings, and only once the library has such a form.
  let { from, to, jobIds }: { from: string; to: string; jobIds: readonly string[] } = $props();

  const library = createListFormTemplates();
  const offered = $derived(
    Boolean(library.data?.data.equalityMonitoringEnabled) &&
      can(page.data.user, 'EditSettings') &&
      (library.data?.data.forms ?? []).some((form) => form.kind === 'EqualityMonitoring'),
  );
  const report = createGetEqualityReport(
    () => ({ from, to, jobId: jobIds.length > 0 ? [...jobIds] : undefined }),
    () => ({ query: { enabled: offered } }),
  );
  const data = $derived(report.data?.status === 200 ? report.data.data : undefined);
  const count = (value: number | null | undefined) => (value == null ? 'Fewer than 5' : String(value));
</script>

{#if offered}
  {#if report.isError}
    <Panel title="Equality monitoring">
      <QueryError message={describeError(report.error, 'Could not load the equality monitoring totals')} onretry={() => report.refetch()} inset />
    </Panel>
  {:else if data}
    {#each data.forms as form (form.formTemplateId)}
      <Panel title={form.name}>
        {#if form.responses == null}
          <p class="text-muted-foreground">Fewer than {data.minimumCount} candidates answered it in these dates, so no totals are shown.</p>
        {:else}
          <p class="mb-3 text-muted-foreground"><span class="tabular-nums">{form.responses}</span> candidates answered it in these dates.</p>
          <div class="flex flex-col gap-5">
            {#each form.questions as question (question.questionId)}
              <Table.Root>
                <Table.Caption class="caption-top text-left font-semibold text-foreground">{question.label}</Table.Caption>
                <Table.Header>
                  <Table.Row>
                    <Table.Head>Answer</Table.Head>
                    <Table.Head class="text-right">Candidates</Table.Head>
                  </Table.Row>
                </Table.Header>
                <Table.Body>
                  {#each question.answers as answer (answer.value)}
                    <Table.Row>
                      <Table.Cell>{answer.label}</Table.Cell>
                      <Table.Cell class={['text-right tabular-nums', answer.count == null && 'text-muted-foreground']}>{count(answer.count)}</Table.Cell>
                    </Table.Row>
                  {/each}
                </Table.Body>
              </Table.Root>
            {/each}
          </div>
        {/if}
      </Panel>
    {/each}
  {/if}
{/if}
