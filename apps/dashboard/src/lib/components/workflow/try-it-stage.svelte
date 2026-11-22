<script lang="ts">
  import type { AutomationAction, EmailTemplateEntry, WorkflowStage } from '@screenhire/api-client';
  import type { Component } from 'svelte';
  import ArrowRightIcon from '~icons/lucide/arrow-right';
  import BellIcon from '~icons/lucide/bell';
  import FlagIcon from '~icons/lucide/flag';
  import HandIcon from '~icons/lucide/hand';
  import ListTodoIcon from '~icons/lucide/list-todo';
  import ShieldCheckIcon from '~icons/lucide/shield-check';
  import TagIcon from '~icons/lucide/tag';
  import UserCheckIcon from '~icons/lucide/user-check';
  import WebhookIcon from '~icons/lucide/webhook';
  import StageRail from '#lib/components/stage-rail.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { candidateName, days, splitThings, type Thing } from '#lib/try-it.js';
  import type { Tracker } from '#lib/pipeline-tracker.js';
  import TryItEmail from './try-it-email.svelte';

  // Try it's second step (design system section 11, Try it): the candidate's stage rail with the stage they are in lit,
  // then at most three things that happen there, each drawn as the real thing (an email as an inbox shows it, a task chip,
  // the line that says the person decides), then one primary button that moves them on. Under it, small text buttons:
  // let the stage's days pass, send these emails to yourself, start again.
  let {
    stage,
    tracker,
    things,
    next,
    pending = false,
    waited = null,
    overdueAt,
    wait,
    templates,
    organizationName,
    logoUrl,
    canSend = false,
    sendStatus = '',
    sending = false,
    heading = $bindable(),
    onnext,
    onwait,
    onsend,
    onrestart,
  }: {
    stage: WorkflowStage;
    tracker: Tracker;
    things: readonly Thing[];
    /** The stage the button moves them to; undefined on the last stage, or once the walk ended. */
    next: WorkflowStage | undefined;
    pending?: boolean;
    /** The days let pass in this stage, once they have been. */
    waited?: number | null;
    overdueAt?: string | null;
    /** The days "Let some days pass" waits here; null when nothing in the stage waits. */
    wait: number | null;
    templates: ReadonlyMap<string, EmailTemplateEntry>;
    organizationName: string;
    logoUrl?: string | null;
    /** Send me these emails is offered: the stage shows emails and the organization can send. */
    canSend?: boolean;
    sendStatus?: string;
    sending?: boolean;
    heading?: HTMLElement;
    onnext: () => void;
    onwait: () => void;
    onsend: () => void;
    onrestart: () => void;
  } = $props();

  let more = $state(false);
  const split = $derived(splitThings(things));
  // The closing line is always the last thing, so "more" shows them all in their own order.
  const shown = $derived(more ? things : split.shown);
  // A new stage starts with the short list again.
  $effect(() => {
    void stage.id;
    void things;
    more = false;
  });

  const taskIcons: Partial<Record<AutomationAction['type'], Component>> = {
    CreateTask: ListTodoIcon,
    AssignReviewer: UserCheckIcon,
    NotifyTeam: BellIcon,
    RequestVerification: ShieldCheckIcon,
    AddTag: TagIcon,
    CallWebhook: WebhookIcon,
  };
  const ended = $derived(things.some((thing) => thing.kind === 'ended'));
  // The quiet buttons under the primary one: text with an underline, as CountButton's link is, because they are small
  // secondary acts that would crowd the one decision as bordered buttons (W12). They are still buttons: they act here.
  const quiet =
    'rounded-sm text-left underline underline-offset-2 hover:text-primary focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:opacity-60 pointer-coarse:min-h-11';
</script>

<div class="flex flex-col gap-5" data-try-it-step="stage" data-stage={stage.id}>
  <StageRail {tracker} {overdueAt} />
  <div aria-live="polite" aria-busy={pending} class={['flex flex-col gap-3', pending && 'opacity-60']}>
    <h3 bind:this={heading} tabindex="-1" class="text-heading focus-visible:outline-none">{stage.name}</h3>
    {#if waited}<p class="text-muted-foreground">{days(waited)} later</p>{/if}
    <ul class="flex flex-col gap-2">
      <!-- Keyed by what each row shows, so a row for another stage's email is a new row, not the old one reused. -->
      {#each shown as thing, index (`${index}:${thing.kind === 'email' ? thing.templateId : thing.text}`)}
        <li data-thing={thing.kind}>
          {#if thing.kind === 'email'}
            <TryItEmail template={templates.get(thing.templateId)} {organizationName} {logoUrl} />
          {:else if thing.kind === 'task'}
            {@const Icon = taskIcons[thing.action] ?? ListTodoIcon}
            <span class="inline-flex max-w-full items-center gap-2 rounded-lg border border-border bg-secondary px-3 py-1.5">
              <Icon aria-hidden="true" class="size-4 shrink-0 text-muted-foreground" /><span class="min-w-0 break-words">{thing.text}</span>
            </span>
          {:else if thing.kind === 'decide'}
            <p class="flex items-start gap-2">
              <HandIcon aria-hidden="true" class="mt-0.5 size-4 shrink-0 text-muted-foreground" /><span class="min-w-0 break-words">{thing.text}</span>
            </p>
          {:else if thing.kind === 'moved'}
            <p class="flex items-start gap-2">
              <ArrowRightIcon aria-hidden="true" class="mt-0.5 size-4 shrink-0 text-muted-foreground" /><span class="min-w-0 break-words">{thing.text}</span>
            </p>
          {:else}
            <p class="flex items-start gap-2 font-semibold">
              <FlagIcon aria-hidden="true" class="mt-0.5 size-4 shrink-0" /><span class="min-w-0 break-words">{thing.text}</span>
            </p>
          {/if}
        </li>
      {/each}
    </ul>
    {#if !more && split.hidden.length > 0}
      <Button variant="outline" class="w-fit" onclick={() => (more = true)}>{split.hidden.length} more</Button>
    {/if}
  </div>

  <div class="flex flex-col items-start gap-3">
    <Button size="lg" onclick={onnext} disabled={pending}>
      {#if next && !ended}Move {candidateName} to {next.name}{:else}See the whole process{/if}
      <ArrowRightIcon aria-hidden="true" />
    </Button>
    <div class="flex flex-wrap items-center gap-x-5 gap-y-2">
      {#if wait && !waited && !ended}
        <button type="button" class={quiet} disabled={pending} onclick={onwait}>Let {days(wait)} pass</button>
      {/if}
      {#if canSend}
        <button type="button" class={quiet} disabled={sending || pending} onclick={onsend}>Send me these emails</button>
      {/if}
      <button type="button" class={quiet} onclick={onrestart}>Start again</button>
    </div>
    <p role="status" class="text-muted-foreground empty:hidden">{sendStatus}</p>
  </div>
</div>
