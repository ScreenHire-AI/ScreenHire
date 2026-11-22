<script lang="ts">
  import type { AiUsePolicy, AutomationTrigger, DefinitionError, HiringWorkflow } from '@screenhire/api-client';
  import {
    ApiError,
    createGetOrganization,
    createGetOrganizationBranding,
    createListEmailTemplates,
    createSendTryItEmails,
    createSimulateWorkflow,
  } from '@screenhire/api-client/svelte-query';
  import { DEFAULT_QUESTIONS_PER_PAGE, type AnswerValue } from '@screenhire/ui/questions';
  import { tick } from 'svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import Panel from '#lib/components/panel.svelte';
  import { describeError } from '#lib/query.js';
  import {
    arrivalTriggers,
    candidateFor,
    candidateFromForm,
    maxAutomaticMoves,
    maxEmailsPerSend,
    nextStage,
    ranOf,
    requestFor,
    thingsOf,
    trackerAt,
    waitOf,
    walkStages,
    type Candidate,
    type Ran,
    type ScenarioId,
    type Thing,
  } from '#lib/try-it.js';
  import { emailTemplateNames } from '#lib/workflow.js';
  import ProblemNotice from './problem-notice.svelte';
  import TryItEnd from './try-it-end.svelte';
  import TryItStage from './try-it-stage.svelte';
  import TryItStart from './try-it-start.svelte';

  // The Try it tab (design system section 11, Try it; workflow-engine.md section 4): one made-up candidate, Jordan, walked
  // through the pipeline a stage at a time. Step 1 picks who is applying, step 2 shows one stage and what happens there,
  // step 3 says that is the process and offers to publish or change a stage. Each move runs that stage's moments through
  // the simulate endpoint on the workflow as it is on screen; nothing is saved, and no one is emailed unless the person
  // sends the emails to themselves. While a problem blocks the workflow the tab shows only the notice with its fix, and an
  // edit anywhere starts Try it again, because the walk was about the workflow as it was.
  let {
    workflow = $bindable(),
    errors,
    onfix,
    editable = true,
    aiUse,
    finish,
    onchange,
  }: {
    workflow: HiringWorkflow;
    errors: readonly DefinitionError[];
    /** Opens the place a problem is in. */
    onfix: (path: string) => void;
    /** False for a version that cannot be changed: a problem is shown, with no fix, and no stage opens. */
    editable?: boolean;
    aiUse?: AiUsePolicy;
    /** The page's own action at the end, such as "Looks good, publish". */
    finish?: { label: string; run: () => void; disabled?: boolean };
    /** Opens a stage's sheet, for Change something. */
    onchange?: (stageId: string) => void;
  } = $props();

  const simulate = createSimulateWorkflow();
  const sender = createSendTryItEmails();
  const emailTemplates = createListEmailTemplates();
  const organization = createGetOrganization();
  const branding = createGetOrganizationBranding();
  const templateNames = $derived(emailTemplateNames(emailTemplates.data?.data.templates));
  const templates = $derived(new Map((emailTemplates.data?.data.templates ?? []).map((template) => [template.id, template])));
  const sendingEnabled = $derived(emailTemplates.data?.data.sendingEnabled ?? false);
  const organizationName = $derived(organization.data?.data.name ?? '');
  const logoUrl = $derived(branding.data?.data.logo?.url ?? null);
  const questionsPerPage = $derived(organization.data?.data.questionsPerPage ?? DEFAULT_QUESTIONS_PER_PAGE);

  const blocked = $derived(errors.length > 0);

  interface Walk {
    candidate: Candidate;
    tags: string[];
    stageId: string;
    things: Thing[];
    waited: number | null;
    visited: string[];
    emails: string[];
    ended: boolean;
  }

  let phase = $state<'start' | 'stage' | 'end'>('start');
  let form = $state(false);
  let walk = $state.raw<Walk | null>(null);
  let pending = $state(false);
  let failure = $state('');
  let sendStatus = $state('');
  let heading = $state<HTMLElement>();
  // Each walk and each move has its own number, so an answer that arrives after Start again or an edit is dropped.
  let run = 0;

  const stages = $derived(walkStages(workflow));
  const stage = $derived(walk ? workflow.stages.find((candidate) => candidate.id === walk!.stageId) : undefined);
  const next = $derived(walk && !walk.ended ? nextStage(workflow, walk.stageId) : undefined);
  const wait = $derived(stage ? waitOf(workflow, stage) : null);
  // At most as many as one send takes (TryItEmailsRequestValidator.MaxEmails): moves by itself can gather more.
  const emailsHere = $derived([...new Set((walk?.things ?? []).flatMap((thing) => (thing.kind === 'email' ? [thing.templateId] : [])))].slice(0, maxEmailsPerSend));
  // The time limit passed a moment ago, so the rail's Overdue badge shows once the days have been let pass.
  let overdueAt = $state<string | null>(null);

  // An edit anywhere, a fix in the notice included, starts again: the walk was about the workflow as it was.
  const stamp = $derived(JSON.stringify(workflow));
  let walkedFor = '';
  $effect(() => {
    const current = stamp;
    if (walkedFor && walkedFor !== current) restart(false);
  });

  async function focusHeading() {
    await tick();
    heading?.focus();
  }

  function restart(focus = true) {
    run++;
    phase = 'start';
    form = false;
    walk = null;
    pending = false;
    failure = '';
    sendStatus = '';
    overdueAt = null;
    walkedFor = '';
    if (focus) void focusHeading();
  }

  async function runAll(snapshot: HiringWorkflow, triggers: AutomationTrigger[], candidate: Candidate, tags: string[], chain: string[], hours: number | null): Promise<Ran[]> {
    const results = await Promise.all(triggers.map((trigger) => simulate.mutateAsync({ data: requestFor(snapshot, trigger, candidate, tags, chain, hours) })));
    return results.flatMap((result) => ranOf(result.data.automations));
  }

  /**
   * One move: the moments that happen, then each form they are sent and answer, then the stage an automation moves them
   * to by itself, up to `maxAutomaticMoves` times. What ran on the way is shown together, on the stage they end up in.
   */
  async function advance(from: Walk, stageId: string, first: AutomationTrigger[], hours: number | null, waited: number | null): Promise<number> {
    const ticket = ++run;
    pending = true;
    failure = '';
    sendStatus = '';
    const snapshot = $state.snapshot(workflow) as HiringWorkflow;
    walkedFor = JSON.stringify(snapshot);
    try {
      let tags = [...from.tags];
      let here = stageId;
      let triggers = first;
      let chain: string[] = [];
      const ran: Ran[] = [];
      const movedTo: string[] = [];
      const visited = from.visited.includes(here) ? [...from.visited] : [...from.visited, here];
      let stopped = false;
      let left: string | undefined;
      for (;;) {
        const now = await runAll(snapshot, triggers, from.candidate, tags, chain, hours);
        const forms = [...new Set(now.flatMap(({ action }) => (action.type === 'SendQuestionnaire' && action.questionnaireId ? [action.questionnaireId] : [])))];
        if (forms.length > 0) {
          now.push(...(await runAll(snapshot, forms.map((questionnaireId) => ({ type: 'QuestionnaireCompleted' as const, questionnaireId })), from.candidate, tags, chain, null)));
        }
        ran.push(...now);
        tags = [...new Set([...tags, ...now.flatMap(({ action }) => (action.type === 'AddTag' && action.tag ? [action.tag] : []))])];
        const move = now.find(({ action }) => action.type === 'MoveToStage' && action.stageId && action.stageId !== here);
        const target = move ? snapshot.stages.find((candidate) => candidate.id === move.action.stageId) : undefined;
        if (!move || !target) break;
        if (!walkStages(snapshot).some((candidate) => candidate.id === target.id)) {
          left = target.name;
          break;
        }
        if (movedTo.length >= maxAutomaticMoves) {
          stopped = true;
          break;
        }
        movedTo.push(target.name);
        here = target.id;
        if (!visited.includes(here)) visited.push(here);
        chain = [move.automationId, ...chain];
        triggers = [{ type: 'StageEntered', stageId: here }];
        hours = null;
      }
      if (ticket !== run) return ticket;
      const things = thingsOf(snapshot, ran, from.candidate, {
        first: waited === null && walkStages(snapshot)[0]?.id === stageId,
        templateNames,
        movedTo,
        stopped,
        left,
        waited: waited ?? undefined,
        last: !nextStage(snapshot, here),
      });
      const emails = [...new Set([...from.emails, ...things.flatMap((thing) => (thing.kind === 'email' ? [thing.templateId] : []))])];
      walk = {
        candidate: from.candidate,
        tags,
        stageId: here,
        things,
        waited,
        visited,
        emails,
        ended: things.some((thing) => thing.kind === 'ended'),
      };
      phase = 'stage';
      pending = false;
      void focusHeading();
    } catch (error) {
      if (ticket !== run) return ticket;
      pending = false;
      failure = describeError(error, 'Could not try this stage');
    }
    return ticket;
  }

  function begin(candidate: Candidate) {
    const first = stages[0];
    if (!first) return;
    overdueAt = null;
    const fresh: Walk = { candidate, tags: [], stageId: first.id, things: [], waited: null, visited: [], emails: [], ended: false };
    void advance(fresh, first.id, arrivalTriggers(workflow, first.id), null, null);
  }

  const choose = (scenario: Exclude<ScenarioId, 'form'>) => begin(candidateFor(workflow, scenario));
  const sendForm = (answers: Record<string, AnswerValue | undefined>) => begin(candidateFromForm(workflow, answers));

  function moveOn() {
    if (!walk) return;
    overdueAt = null;
    if (!next || walk.ended) {
      phase = 'end';
      void focusHeading();
      return;
    }
    void advance(walk, next.id, arrivalTriggers(workflow, next.id), null, null);
  }

  async function letDaysPass() {
    if (!walk || !wait) return;
    const overdue = wait.overdue;
    const ticket = await advance(walk, walk.stageId, wait.triggers, wait.days * 24, wait.days);
    // Only once the days have passed: a wait that failed leaves no Overdue badge behind.
    if (overdue && ticket === run && !failure) overdueAt = new Date(Date.now() - 60_000).toISOString();
  }

  async function sendEmails() {
    if (emailsHere.length === 0) return;
    sendStatus = '';
    try {
      const sent = (await sender.mutateAsync({ data: { templateIds: emailsHere } })).data;
      sendStatus = `Sent ${sent.count === 1 ? 'the email' : `${sent.count} emails`} to ${sent.to}, marked as a test.`;
    } catch (error) {
      sendStatus =
        error instanceof ApiError && error.status === 429
          ? 'You have sent a few already. Try again in a few minutes.'
          : describeError(error, 'Could not send the emails');
    }
  }
</script>

<Panel title="Try your hiring process">
  {#if blocked}
    <ProblemNotice bind:workflow {errors} {onfix} {editable} />
  {:else if stages.length === 0}
    <p class="text-muted-foreground">Add a stage to try the process.</p>
  {:else}
    {#if failure}<FailureText message={failure} />{/if}
    {#if phase === 'start' || !walk}
      <div aria-busy={pending} class={[pending && 'opacity-60']}>
        <TryItStart {workflow} bind:form bind:heading {questionsPerPage} {aiUse} onchoose={choose} onsend={sendForm} />
      </div>
    {:else if phase === 'stage' && stage}
      <TryItStage
        {stage}
        tracker={trackerAt(workflow, walk.stageId)}
        things={walk.things}
        {next}
        {pending}
        waited={walk.waited}
        {overdueAt}
        wait={wait?.days ?? null}
        {templates}
        {organizationName}
        {logoUrl}
        canSend={sendingEnabled && emailsHere.length > 0}
        {sendStatus}
        sending={sender.isPending}
        bind:heading
        onnext={moveOn}
        onwait={letDaysPass}
        onsend={sendEmails}
        onrestart={() => restart()}
      />
    {:else}
      <TryItEnd
        {stages}
        visited={walk.visited.length}
        emails={walk.emails.length}
        {finish}
        bind:heading
        onchange={editable ? onchange : undefined}
        onrestart={() => restart()}
      />
    {/if}
  {/if}
</Panel>
