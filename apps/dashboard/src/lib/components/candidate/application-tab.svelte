<script lang="ts">
  import SendIcon from '~icons/lucide/send';
  import type { ApplicationDetail, FollowUpQuestionnaire, Question } from '@screenhire/api-client';
  import AnswersPanel, { answerText } from '#lib/components/candidate/answers-panel.svelte';
  import OtherApplications from '#lib/components/candidate/other-applications.svelte';
  import ExpandableRow from '#lib/components/expandable-row.svelte';
  import Panel from '#lib/components/panel.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { sentFormState, stateTail } from '#lib/application-forms.js';
  import { linkedTo } from '#lib/linked.svelte.js';
  import SendFormSheet from './send-form-sheet.svelte';

  // The whole application (design system section 21): the answers to the application form, with what the candidate
  // declared, then the other forms sent to them, one row each that opens to its answers, read against the questions as
  // the candidate saw them: "Answered 2 days ago" or "Sent 3 days ago, waiting". An equality monitoring form shows only
  // whether it was answered, because its answers only ever count toward totals in Reports. "Send a form" is here for
  // someone who may email the candidate. Then the candidate's other applications, which link to their profile.
  let {
    detail,
    questions,
    forms = [],
    canSend = false,
  }: {
    detail: ApplicationDetail;
    questions: ReadonlyMap<string, Question>;
    /** The job's forms other than the application form, for Send a form. */
    forms?: readonly FollowUpQuestionnaire[];
    /** Whether this person may email this candidate here. */
    canSend?: boolean;
  } = $props();

  let open = $state(new Set<string>());
  let sending = $state(false);
  let notice = $state('');
  const now = new Date();
  const allOpen = $derived(detail.followUps.length > 0 && detail.followUps.every((followUp) => open.has(followUp.id)));

  // A link to one form (`#follow-up-<id>`, from the Activity tab or a notification) opens its row.
  $effect(() => {
    const id = location.hash.startsWith('#follow-up-') ? location.hash.slice('#follow-up-'.length) : '';
    if (id && detail.followUps.some((followUp) => followUp.id === id)) open = new Set([...open, id]);
  });

  function toggleAll() {
    open = allOpen ? new Set() : new Set(detail.followUps.map((followUp) => followUp.id));
  }
</script>

<AnswersPanel {detail} {questions} />

{#if detail.followUps.length > 0 || (canSend && forms.length > 0)}
  <Panel title="Forms" id="forms">
    {#snippet actions()}
      {#if detail.followUps.length > 1}
        <Button variant="outline" onclick={toggleAll}>{allOpen ? 'Collapse all' : 'Expand all'}</Button>
      {/if}
      {#if canSend && forms.length > 0}
        <Button variant="outline" onclick={() => (sending = true)}><SendIcon aria-hidden="true" />Send a form</Button>
      {/if}
    {/snippet}
    <p role="status" class="mb-2 text-muted-foreground empty:hidden">{notice}</p>
    {#if detail.followUps.length === 0}
      <p class="text-muted-foreground">No form has been sent to this candidate yet.</p>
    {:else}
      <ul class="flex flex-col divide-y divide-border">
        {#each detail.followUps as followUp (followUp.id)}
          {@const state = sentFormState(followUp, now)}
          {@const asked = new Map(followUp.questions.map((question) => [question.id, question]))}
          <li class="py-2" id={`follow-up-${followUp.id}`} {@attach linkedTo(`follow-up-${followUp.id}`)}>
            <ExpandableRow
              id={`follow-up-${followUp.id}-answers`}
              bind:expanded={() => open.has(followUp.id), (next) => (open = next ? new Set([...open, followUp.id]) : new Set([...open].filter((id) => id !== followUp.id)))}
            >
              {#snippet summary()}
                <span class="font-semibold break-words">{followUp.name}</span>
                <span class="text-caption text-muted-foreground">
                  {state.kind === 'answered' ? 'Answered' : 'Sent'}
                  <RelativeTime at={state.at} class="inline" />{stateTail[state.kind]}{followUp.timesSent > 1 && state.kind !== 'answered' ? ` (sent ${followUp.timesSent} times)` : ''}
                </span>
              {/snippet}
              {#if followUp.kind === 'EqualityMonitoring'}
                <p class="text-muted-foreground">An equality monitoring form. Its answers count only toward totals in Reports, and never show here.</p>
              {:else if !followUp.completedAt}
                <p class="text-muted-foreground">Not answered yet.</p>
              {:else if followUp.answers.length === 0}
                <p class="text-muted-foreground">No answers to the optional questions.</p>
              {:else}
                <SummaryList>
                  {#each followUp.answers as answer (answer.questionId)}
                    {@const question = asked.get(answer.questionId)}
                    <SummaryItem label={question?.label ?? answer.questionId} question>
                      <span class="whitespace-pre-line">{answerText(answer, question)}</span>
                    </SummaryItem>
                  {/each}
                </SummaryList>
              {/if}
            </ExpandableRow>
          </li>
        {/each}
      </ul>
    {/if}
  </Panel>
{/if}

<OtherApplications candidateId={detail.candidate.id} applicationId={detail.id} />

{#if canSend}
  <SendFormSheet bind:open={sending} applicationId={detail.id} {forms} sent={detail.followUps} onsent={(name) => (notice = `Sent ${name}. The candidate gets the link by email.`)} />
{/if}
